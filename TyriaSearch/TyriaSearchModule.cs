using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Gw2Sharp.WebApi;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ModuleShared;
using Blish_HUD.Controls.Extern;
using Keyboard = Blish_HUD.Controls.Intern.Keyboard;

namespace TyriaSearch
{
    public enum SearchLanguage { None, English, German, French, Spanish }

    /// <summary>What clicking a result does. Blish HUD shows the names split into words.</summary>
    public enum ResultAction { CopyChatLink, PasteIntoChat, WhisperToYourself, ShowOnMap, OpenWikiPage, CopyWikiCommand, ToggleFavourite, ShowMenu }

    [Export(typeof(Module))]
    public class TyriaSearchModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<TyriaSearchModule>();

        // Between full downloads; game updates in between only fetch what was added.
        private static readonly TimeSpan MaxDataAge = TimeSpan.FromDays(30);

        private SettingEntry<KeyBinding> _openKey;
        private SettingEntry<KeyBinding> _clearMarkerKey;
        private SettingEntry<bool> _openMapOnShow;
        private SettingEntry<bool> _showPortals;
        private SettingEntry<KeyBinding> _gameMapKey;
        private SettingEntry<SearchLanguage> _secondLanguage;
        private SettingEntry<bool> _searchItems;
        private SettingEntry<bool> _searchNpcs;
        private SettingEntry<bool> _restoreClipboard;
        private SettingEntry<bool> _overwriteUnsentText;
        private SettingEntry<int> _keyDelayMs;
        private SettingEntry<ResultAction> _leftClick;
        private SettingEntry<ResultAction> _rightClick;
        private SettingEntry<List<string>> _favourites;

        private Texture2D _icon;
        // Shown for results without an icon of their own, by name (npc, item, skill, location, page).
        private readonly Dictionary<string, Texture2D> _fallbackIcons = new Dictionary<string, Texture2D>();
        private CornerIcon _cornerIcon;
        private SearchWindow _window;
        private MapMarker _mapMarker;
        private Gw2Data _data;
        private List<Portal> _portals; // bundled; set in Initialize, before any refresh reads it

        // Written by the background refresh, read by the UI each frame.
        private volatile SearchIndex _index = SearchIndex.Empty;
        private volatile string[] _indexLanguages = { "en" };
        private volatile Dictionary<int, int> _continents = new Dictionary<int, int>();
        private volatile Places _places; // null until English map data is loaded
        private volatile Dictionary<string, string> _fileIcons; // /v2/files icons, saved with the NPCs; null without them

        // Live wiki searches by query, so retyping a query doesn't ask the wiki again. Main thread only.
        private readonly Dictionary<string, Task<List<SearchItem>>> _wikiSearches = new Dictionary<string, Task<List<SearchItem>>>();
        private const int WikiResults = 8;
        private volatile string _status = "Loading…";
        private volatile bool _downloading;

        private readonly CancellationTokenSource _unloading = new CancellationTokenSource();
        private CancellationTokenSource _refreshCancel;
        private Task _refreshTask = Task.CompletedTask;
        private bool _refreshForced; // the latest refresh downloads everything
        private bool _busy;

        [ImportingConstructor]
        public TyriaSearchModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { }

        internal SearchIndex Index => _index;
        internal string Status => _status;
        // A refresh replaced mid-download can leave the flag set; only the latest refresh running counts.
        internal bool Downloading => _downloading && !_refreshTask.IsCompleted;
        internal ResultAction LeftClick => _leftClick.Value;
        internal ResultAction RightClick => _rightClick.Value;
        internal IReadOnlyList<string> Favourites => _favourites.Value;
        internal bool IsFavourite(SearchItem item) => _favourites.Value.Contains(item.Entry.Key);
        internal int MapMarkerCount => _mapMarker?.Targets.Count ?? 0;

        // Index rebuilds create new entries, so match by what the marker shows rather than by reference.
        private SearchItem MarkerFor(SearchItem item) => _mapMarker.Targets.FirstOrDefault(t =>
            t.Entry.Name == item.Entry.Name && t.Entry.MapId == item.Entry.MapId && t.Entry.X == item.Entry.X && t.Entry.Y == item.Entry.Y);

        internal bool IsMarked(SearchItem item) => MarkerFor(item) != null;

        protected override void DefineSettings(SettingCollection settings)
        {
            _openKey = settings.DefineSetting("OpenKey", new KeyBinding(),
                () => "Open search hotkey",
                () => "Shows or hides the search window. You can also click the magnifier icon at the top left of the screen.");
            _clearMarkerKey = settings.DefineSetting("ClearMarkerKey", new KeyBinding(),
                () => "Clear map markers hotkey",
                () => "Removes all Show on map markers from the world map. Works with the map open.");
            _openMapOnShow = settings.DefineSetting("OpenMapOnShow", true,
                () => "Open map on Show on map",
                () => "After Show on map, open the world map if it isn't already open, by pressing the game's map key below. Chat must be closed.");
            _showPortals = settings.DefineSetting("ShowPortals", false,
                () => "Show zone portals on the world map",
                () => "Marks every portal on the world map with the game's icons: zone portals, asura gates, dungeon, raid and fractal entrances. Each is labelled with where it leads once you zoom in.");
            // Never enabled: it's the key to press in the game, not a Blish HUD hotkey.
            _gameMapKey = settings.DefineSetting("GameMapKey", new KeyBinding(Microsoft.Xna.Framework.Input.Keys.M),
                () => "Game's map key",
                () => "Set this to the World Map key in the game's options (Control Options, User Interface). M by default.");
            _secondLanguage = settings.DefineSetting("SecondLanguage", SearchLanguage.None,
                () => "Second search language",
                () => "Names are shown in Blish HUD's language (Blish HUD settings, Overlay, Language). Searches also match names in this language, so you can find what guildmates call something. Each language is a separate download.");
            _searchNpcs = settings.DefineSetting("SearchNpcs", true,
                () => "Search NPCs",
                () => "Include NPCs from the wiki, with where they are, their nearest waypoint and a map marker. NPC names are in English. About 2 MB from the wiki (a few minutes), downloaded monthly; also downloads English map data if neither search language is English.");
            _searchItems = settings.DefineSetting("SearchItems", true,
                () => "Search items",
                () => "Include all 75,000 items. The first download takes a few minutes per language and about 10 MB of disk space each.");
            _restoreClipboard = settings.DefineSetting("RestoreClipboard", true,
                () => "Restore clipboard",
                () => "After Paste into chat or Whisper to yourself, put back what you had copied before. Those actions type through the clipboard.");
            _overwriteUnsentText = settings.DefineSetting("OverwriteUnsentText", false,
                () => "Overwrite unsent chat text",
                () => "For Whisper to yourself: if the chat box still holds text you typed but didn't send, delete it and send the whisper. The deleted text can't be recovered. Off: stop and leave your text in chat.");
            _keyDelayMs = settings.DefineSetting("KeyDelayMs", 40,
                () => "Key press delay (ms)",
                () => "Time between simulated key presses for Paste into chat and Whisper to yourself. Raise it if text is sometimes typed wrong or not sent. Below one game frame (about 16 ms at 60 FPS) keys may be missed.");
            _keyDelayMs.SetRange(20, 250);
            _leftClick = settings.DefineSetting("LeftClickAction", ResultAction.CopyChatLink,
                () => "Left-click a result",
                () => "What clicking a result does. When it can't be done for that result (no chat link, or no map position), the wiki page opens instead. Shift-click always shows the menu.");
            _rightClick = settings.DefineSetting("RightClickAction", ResultAction.ShowMenu,
                () => "Right-click a result",
                () => "What right-clicking a result does. When it can't be done for that result (no chat link, or no map position), the wiki page opens instead. Shift-click always shows the menu.");

            // Hidden from the settings panel; changed from the results' right-click menu.
            var saved = settings.AddSubCollection("Saved", renderInUi: false, lazyLoaded: false);
            _favourites = saved.DefineSetting("Favourites", new List<string>(),
                () => "Favourites",
                () => "Keys of favourite results.");
        }

        public override IView GetSettingsView() => new SettingsWithValuesView(ModuleParameters.SettingsManager.ModuleSettings,
            ("Update data", "Download all search data again now: places, skills and traits, plus NPCs and items when they're on. A few minutes with items. Game updates are handled automatically.", UpdateData));

        protected override void Initialize()
        {
            if (_favourites.Value == null) _favourites.Value = new List<string>();

            _data = new Gw2Data(ModuleParameters.DirectoriesManager.GetFullDirectoryPath("tyriasearch"));
            _icon = ModuleParameters.ContentsManager.GetTexture(@"textures\TyriaSearch.png");
            foreach (string name in new[] { "npc", "item", "skill", "location", "page" })
                _fallbackIcons[name] = ModuleParameters.ContentsManager.GetTexture($@"textures\icon-{name}.png");
            _window = new SearchWindow(this, _icon);
            _mapMarker = new MapMarker(ContinentOf, IconFor) { Parent = GameService.Graphics.SpriteScreen };
            using (var portals = ModuleParameters.ContentsManager.GetFileStream("portals.json"))
                _portals = Portal.Load(portals);
            _mapMarker.Portals = _portals;
            _cornerIcon = new CornerIcon(_icon, "Tyria Search");
            _cornerIcon.Click += CornerIcon_Click;

            _openKey.Value.Enabled = true;
            _openKey.Value.Activated += OpenKey_Activated;
            _clearMarkerKey.Value.Enabled = true;
            _clearMarkerKey.Value.Activated += ClearMarkerKey_Activated;
            _secondLanguage.SettingChanged += SecondLanguage_SettingChanged;
            _searchItems.SettingChanged += DataSetting_SettingChanged;
            _searchNpcs.SettingChanged += DataSetting_SettingChanged;
            _showPortals.SettingChanged += ShowPortals_SettingChanged;
            GameService.Overlay.UserLocaleChanged += Overlay_UserLocaleChanged;
            GameService.Gw2Mumble.Info.BuildIdChanged += Info_BuildIdChanged;
        }

        protected override void OnModuleLoaded(EventArgs e)
        {
            base.OnModuleLoaded(e);
            RequestRefresh(force: false, restart: false);
            ShowPortals();
        }

        private void ShowPortals() => _mapMarker.ShowPortals = _showPortals.Value;

        protected override void Update(GameTime gameTime)
        {
            _window.Update();
            _mapMarker.Size = GameService.Graphics.SpriteScreen.Size;
            _cornerIcon.LoadingMessage = Downloading ? _status : null;
        }

        protected override void Unload()
        {
            _unloading.Cancel();
            _refreshCancel?.Cancel();
            _openKey.Value.Enabled = false;
            _openKey.Value.Activated -= OpenKey_Activated;
            _clearMarkerKey.Value.Enabled = false;
            _clearMarkerKey.Value.Activated -= ClearMarkerKey_Activated;
            _secondLanguage.SettingChanged -= SecondLanguage_SettingChanged;
            _searchItems.SettingChanged -= DataSetting_SettingChanged;
            _searchNpcs.SettingChanged -= DataSetting_SettingChanged;
            _showPortals.SettingChanged -= ShowPortals_SettingChanged;
            GameService.Overlay.UserLocaleChanged -= Overlay_UserLocaleChanged;
            GameService.Gw2Mumble.Info.BuildIdChanged -= Info_BuildIdChanged;
            if (_cornerIcon != null) _cornerIcon.Click -= CornerIcon_Click;
            _cornerIcon?.Dispose();
            _mapMarker?.Dispose();
            _window?.Dispose();
            _icon?.Dispose();
            foreach (var texture in _fallbackIcons.Values) texture.Dispose();
            _data?.Dispose();
        }

        private void CornerIcon_Click(object sender, Blish_HUD.Input.MouseEventArgs e) => _window.Toggle();
        private void OpenKey_Activated(object sender, EventArgs e) => _window.Toggle();
        private void ClearMarkerKey_Activated(object sender, EventArgs e) => ClearMapMarker();
        private void ShowPortals_SettingChanged(object sender, ValueChangedEventArgs<bool> e) => ShowPortals();
        private void SecondLanguage_SettingChanged(object sender, ValueChangedEventArgs<SearchLanguage> e) => RequestRefresh(force: false, restart: true);
        private void DataSetting_SettingChanged(object sender, ValueChangedEventArgs<bool> e) => RequestRefresh(force: false, restart: true);
        private void Overlay_UserLocaleChanged(object sender, ValueEventArgs<System.Globalization.CultureInfo> e) => RequestRefresh(force: false, restart: true);
        // Runs once Mumble connects, and again after a game update; data is only downloaded if the build changed.
        private void Info_BuildIdChanged(object sender, ValueEventArgs<int> e) => RequestRefresh(force: false, restart: false);

        private void UpdateData() => RequestRefresh(force: true, restart: true);

        /// <summary>The result's own icon, else a bundled one for its kind.</summary>
        internal AsyncTexture2D IconFor(SearchEntry entry)
        {
            string icon = entry.Icon;
            if (icon?.StartsWith(SearchEntry.AssetIcon) == true && int.TryParse(icon.Substring(SearchEntry.AssetIcon.Length), out int assetId))
                return AsyncTexture2D.FromAssetId(assetId);
            bool bundled = icon?.StartsWith(SearchEntry.BundledIcon) == true;
            if (!string.IsNullOrEmpty(icon) && !bundled) return GameService.Content.GetRenderServiceTexture(icon);
            string name = bundled ? icon.Substring(SearchEntry.BundledIcon.Length) : entry.Kind switch
            {
                EntryKind.Npc => "npc",
                EntryKind.Item => "item",
                EntryKind.Skill => "skill",
                EntryKind.Trait => "skill",
                EntryKind.WikiPage => "page",
                _ => "location",
            };
            return _fallbackIcons.TryGetValue(name, out var texture) ? texture : null;
        }

        // ---- Data ----

        /// <summary>Everything one refresh has loaded or downloaded so far.</summary>
        private sealed class DataSet
        {
            public string[] Languages;
            public readonly Dictionary<string, Gw2Data.CacheFile> Main = new Dictionary<string, Gw2Data.CacheFile>();
            public readonly Dictionary<string, Gw2Data.CacheFile> Items = new Dictionary<string, Gw2Data.CacheFile>();
            public Gw2Data.NpcCacheFile Npcs;
            public bool UseItems, UseNpcs;

            public Gw2Data.CacheFile MainFor(string language) => language != null && Main.TryGetValue(language, out var c) ? c : null;
            public Gw2Data.CacheFile ItemsFor(string language) => language != null && Items.TryGetValue(language, out var c) ? c : null;
        }

        /// <summary>
        /// Loads saved data, then downloads anything missing or out of date in the background.
        /// Without restart, a refresh already running is left to finish.
        /// </summary>
        private void RequestRefresh(bool force, bool restart)
        {
            if (!_refreshTask.IsCompleted && !restart) return;
            // A settings change during Update data mustn't turn it into a partial update.
            force |= _refreshForced && !_refreshTask.IsCompleted;
            _refreshForced = force;
            _refreshCancel?.Cancel();
            var cancel = _refreshCancel = new CancellationTokenSource();
            _refreshTask = Task.Run(() => RefreshAsync(force, cancel.Token));
        }

        private async Task RefreshAsync(bool force, CancellationToken ct)
        {
            var data = new DataSet { Languages = SearchLanguages(), UseItems = _searchItems.Value, UseNpcs = _searchNpcs.Value };
            // NPC locations are matched by English place names, since the wiki is English.
            var mainLanguages = data.UseNpcs ? data.Languages.Union(new[] { "en" }).ToArray() : data.Languages;
            var failed = new List<string>();

            T TryLoad<T>(Func<T> load, string what) where T : class
            {
                try { return load(); }
                catch (Exception ex) { Logger.Warn(ex, $"Couldn't read saved {what}; downloading it again."); return null; }
            }

            // Each download stands alone: if the wiki is down, items still update.
            async Task Download(string what, Func<IProgress<string>, Task> download)
            {
                _downloading = true;
                var progress = new StatusProgress(text => { if (!ct.IsCancellationRequested) _status = $"Updating {what}: {text}"; });
                progress.Report("starting…");
                try
                {
                    await download(progress);
                    Publish(data, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Logger.Warn(ex, $"Updating {what} failed.");
                    failed.Add(what);
                }
            }

            try
            {
                foreach (string language in mainLanguages) data.Main[language] = TryLoad(() => _data.LoadMain(language), $"{language} data");
                if (data.UseItems) foreach (string language in data.Languages) data.Items[language] = TryLoad(() => _data.LoadItems(language), $"{language} items");
                if (data.UseNpcs) data.Npcs = TryLoad(_data.LoadNpcs, "NPCs");
                Publish(data, ct);

                int build = GameService.Gw2Mumble.Info.BuildId;
                // Asked for once per refresh, and only if some language needs it; the same in every language.
                Task<string> fingerprint = null;
                Task<string> Fingerprint() => fingerprint ??= _data.MainFingerprintAsync(ct);

                // Most useful first: places, skills and traits, then NPCs, then the slow item download.
                // A game update only re-downloads places, skills and traits if maps, skills or traits were added or removed.
                foreach (string language in mainLanguages)
                {
                    var saved = data.MainFor(language);
                    if (force || IsStale(saved?.DownloadedUtc))
                        await Download($"{LanguageName(language)} places, skills and traits",
                            async progress => data.Main[language] = await _data.DownloadMainAsync(language, build, await Fingerprint(), progress, ct));
                    else if (IsOlderBuild(saved.GameBuild, build))
                        await Download($"{LanguageName(language)} places, skills and traits", async progress =>
                        {
                            progress.Report("checking for changes…");
                            string current = await Fingerprint();
                            if (current == saved.Fingerprint) _data.ConfirmMain(saved, build);
                            else data.Main[language] = await _data.DownloadMainAsync(language, build, current, progress, ct);
                        });
                }

                // The wiki changes a little at a time; game updates don't matter to it.
                if (data.UseNpcs && (force || IsStale(data.Npcs?.DownloadedUtc)))
                    await Download("NPCs", async progress => data.Npcs = await _data.DownloadNpcsAsync(progress, ct));

                // A game update only fetches the items it added.
                if (data.UseItems)
                {
                    foreach (string language in data.Languages)
                    {
                        var saved = data.ItemsFor(language);
                        if (force || IsStale(saved?.DownloadedUtc))
                            await Download($"{LanguageName(language)} items",
                                async progress => data.Items[language] = await _data.DownloadItemsAsync(language, build, progress, ct));
                        else if (IsOlderBuild(saved.GameBuild, build))
                            await Download($"{LanguageName(language)} items",
                                async progress => data.Items[language] = await _data.UpdateItemsAsync(saved, build, progress, ct));
                    }
                }

                ct.ThrowIfCancellationRequested();
                Publish(data, ct);
                if (failed.Count > 0)
                {
                    _status = _index.Count > 0
                        ? $"Couldn't update {string.Join(", ", failed)}; using what was saved earlier. Try Update data later."
                        : "Couldn't download the data. Check your connection, then press Update data.";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // A newer refresh replaced this one.
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Updating search data failed.");
                if (!ct.IsCancellationRequested) _status = "Couldn't load the search data. See the Blish HUD log for details.";
            }
            finally
            {
                if (!ct.IsCancellationRequested) _downloading = false;
            }
        }

        /// <summary>Builds a new index from what's loaded so far and swaps it in.</summary>
        private void Publish(DataSet data, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            string display = data.Languages[0], second = data.Languages.Length > 1 ? data.Languages[1] : null;
            var main = data.MainFor(display);
            if (main == null)
            {
                _status = "Downloading data for the first time…";
                return;
            }

            IEnumerable<SearchEntry> entries = main.Entries;
            IEnumerable<SearchEntry> alternates = data.MainFor(second)?.Entries ?? Enumerable.Empty<SearchEntry>();
            if (data.UseItems)
            {
                entries = entries.Concat(data.ItemsFor(display)?.Entries ?? Enumerable.Empty<SearchEntry>());
                alternates = alternates.Concat(data.ItemsFor(second)?.Entries ?? Enumerable.Empty<SearchEntry>());
            }
            // The wiki names places in English.
            var english = data.MainFor("en");
            var places = english != null ? new Places(english, main) : null;
            if (data.UseNpcs && data.Npcs != null && places != null)
                entries = entries.Concat(WikiEntries.BuildNpcs(data.Npcs, places));
            if (places != null)
                entries = entries.Concat(Portal.Entries(_portals, places));

            var index = new SearchIndex(entries, alternates);
            if (ct.IsCancellationRequested) return;
            _places = places;
            _fileIcons = data.Npcs?.Icons;
            _continents = (main.Places ?? new List<Gw2Data.Place>()).Where(p => p.IsMap)
                .GroupBy(p => p.MapId).ToDictionary(g => g.Key, g => g.First().Continent);
            _indexLanguages = second != null && data.MainFor(second) != null ? new[] { display, second } : new[] { display };
            _index = index;

            int days = (int)(DateTime.UtcNow - (main.CheckedUtc ?? main.DownloadedUtc)).TotalDays;
            string age = days <= 0 ? "today" : days == 1 ? "yesterday" : $"{days} days ago";
            _status = $"{index.Count:N0} results available, updated {age}"
                      + (_indexLanguages.Length > 1 ? $". Also matches {LanguageName(second)} names." : ".");
        }

        /// <summary>Missing, or due a full download to pick up renames and fixes that adding new things doesn't.</summary>
        private static bool IsStale(DateTime? downloadedUtc) => downloadedUtc == null || DateTime.UtcNow - downloadedUtc.Value > MaxDataAge;

        /// <summary>The game updated since the data was last checked. The build is 0 until Mumble connects.</summary>
        private static bool IsOlderBuild(int savedBuild, int currentBuild) => currentBuild > 0 && savedBuild != currentBuild;

        /// <summary>API language codes: Blish HUD's language first (names are shown in it), then the second search language.</summary>
        private string[] SearchLanguages()
        {
            string primary = GameService.Overlay.UserLocale.Value switch
            {
                Locale.German => "de",
                Locale.French => "fr",
                Locale.Spanish => "es",
                Locale.Chinese => "zh",
                _ => "en", // the API has no Korean
            };
            string second = _secondLanguage.Value switch
            {
                SearchLanguage.English => "en",
                SearchLanguage.German => "de",
                SearchLanguage.French => "fr",
                SearchLanguage.Spanish => "es",
                _ => null,
            };
            return second == null || second == primary ? new[] { primary } : new[] { primary, second };
        }

        private static string LanguageName(string code) => code switch
        {
            "de" => "German",
            "fr" => "French",
            "es" => "Spanish",
            "zh" => "Chinese",
            _ => "English",
        };

        /// <summary>Tyria (1) or the Mists (2); maps the data doesn't know are taken to be in Tyria.</summary>
        private int ContinentOf(int mapId) => _continents.TryGetValue(mapId, out int continent) ? continent : 1;

        // ---- Actions ----

        internal void Copy(SearchItem item) => _ = CopyAsync(item, returnToGame: false);

        /// <summary>Enter in the search box: copy the top result and go back to the game, ready to paste.</summary>
        internal void CopyAndReturnToGame(SearchItem item) => _ = CopyAsync(item, returnToGame: true);

        private async Task CopyAsync(SearchItem item, bool returnToGame)
        {
            var entry = item.Entry;
            if (entry.ChatLink == null)
            {
                _window.ShowMessage($"No waypoint is known for {entry.Name}. Its wiki page may say where it is.");
                return;
            }

            // For an NPC, name the waypoint that was copied.
            string description = entry.LinkIsNearestWaypoint
                ? $"{_index.Find(entry.ChatLink)?.Entry.Name ?? "nearest waypoint"} {entry.ChatLink} (nearest to {entry.Name})"
                : $"{entry.Name} {entry.ChatLink}";
            if (!await CopyTextAsync(entry.ChatLink, description) || !returnToGame) return;

            _window.ReleaseFocus();
            _window.Hide();
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) GameService.GameIntegration.Gw2Instance.FocusGw2();
        }

        internal void CopyWikiCommand(SearchItem item) => _ = CopyTextAsync($"/wiki {WikiTerm(item.Entry)}", $"/wiki {WikiTerm(item.Entry)}");

        private async Task<bool> CopyTextAsync(string text, string description)
        {
            if (!await ClipboardUtil.WindowsClipboardService.SetTextAsync(text))
            {
                _window.ShowMessage("Couldn't use the clipboard. Another program may be using it; try again.");
                return false;
            }
            _window.ShowMessage($"Copied {description}");
            return true;
        }

        internal void PasteIntoChat(SearchItem item) =>
            _ = RunChatActionAsync(delay => ChatActions.InsertIntoChatAsync(item.Entry.ChatLink, delay));

        internal void WhisperToSelf(SearchItem item) =>
            _ = RunChatActionAsync(delay => ChatActions.WhisperToSelfAsync(item.Entry.ChatLink, delay, _overwriteUnsentText.Value));

        private async Task RunChatActionAsync(Func<int, Task> action)
        {
            if (_busy) return;
            _busy = true;
            bool restoreClipboard = false;
            string previousClipboard = null;
            try
            {
                int delay = _keyDelayMs.Value;
                _window.ReleaseFocus();
                await ChatActions.FocusGameAsync();
                await Task.Delay(delay);

                restoreClipboard = _restoreClipboard.Value;
                previousClipboard = await ClipboardUtil.WindowsClipboardService.GetTextAsync() ?? "";
                await action(delay);
            }
            catch (AbortException ex)
            {
                ScreenNotification.ShowNotification($"Tyria Search: {ex.Message}", ScreenNotification.NotificationType.Warning);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Chat action failed.");
                ScreenNotification.ShowNotification("Tyria Search failed. See the Blish HUD log for details.", ScreenNotification.NotificationType.Warning);
            }
            finally
            {
                if (restoreClipboard)
                    await ClipboardUtil.WindowsClipboardService.SetTextAsync(previousClipboard);
                _busy = false;
            }
        }

        internal void ShowOnMap(SearchItem item)
        {
            if (!IsMarked(item)) _mapMarker.Targets.Add(item);
            _window.ShowMessage($"{item.Entry.Name} is marked on the world map. Open the map (M) to see it.");
            if (_openMapOnShow.Value && !GameService.Gw2Mumble.UI.IsMapOpen) _ = OpenMapAsync();
        }

        private async Task OpenMapAsync()
        {
            try
            {
                _window.ReleaseFocus();
                _window.Hide(); // it would cover the map
                await ChatActions.FocusGameAsync();
                int delay = _keyDelayMs.Value;
                await Task.Delay(delay);

                var binding = _gameMapKey.Value;
                if (binding.PrimaryKey == Microsoft.Xna.Framework.Input.Keys.None)
                    throw new AbortException("set the game's map key in Tyria Search's settings.");
                // XNA key codes are Windows virtual-key codes.
                var modifiers = new List<VirtualKeyShort>();
                if (binding.ModifierKeys.HasFlag(Microsoft.Xna.Framework.Input.ModifierKeys.Ctrl)) modifiers.Add(VirtualKeyShort.LCONTROL);
                if (binding.ModifierKeys.HasFlag(Microsoft.Xna.Framework.Input.ModifierKeys.Alt)) modifiers.Add(VirtualKeyShort.LMENU);
                if (binding.ModifierKeys.HasFlag(Microsoft.Xna.Framework.Input.ModifierKeys.Shift)) modifiers.Add(VirtualKeyShort.LSHIFT);
                ChatInput.EnsureSafe(chatOpen: false);
                try
                {
                    foreach (var key in modifiers) Keyboard.Press(key, true);
                    if (modifiers.Count > 0) await Task.Delay(delay); // GW2 must see the modifier held for a frame
                    ChatInput.EnsureSafe(chatOpen: false);
                    Keyboard.Stroke((VirtualKeyShort)binding.PrimaryKey, true);
                    if (modifiers.Count > 0) await Task.Delay(delay);
                }
                finally
                {
                    // Never leave a simulated modifier held down system-wide.
                    foreach (var key in modifiers) Keyboard.Release(key, true);
                }
            }
            catch (AbortException ex)
            {
                ScreenNotification.ShowNotification($"Tyria Search couldn't open the map: {ex.Message}", ScreenNotification.NotificationType.Warning);
            }
        }

        internal void ClearMapMarker()
        {
            if (_mapMarker.Targets.Count == 0) return;
            _mapMarker.Targets.Clear();
            _window.ShowMessage("Map markers cleared.");
            if (GameService.Gw2Mumble.UI.IsMapOpen) ScreenNotification.ShowNotification("Tyria Search: map markers cleared.");
        }

        internal void RemoveMapMarker(SearchItem item)
        {
            _mapMarker.Targets.Remove(MarkerFor(item));
            _window.ShowMessage($"{item.Entry.Name} removed from the world map.");
        }

        internal void ToggleFavourite(SearchItem item)
        {
            var favourites = new List<string>(_favourites.Value);
            if (!favourites.Remove(item.Entry.Key)) favourites.Add(item.Entry.Key);
            _favourites.Value = favourites; // a new list, so the setting notices the change and saves
            _window.RunSearch();
        }

        internal void OpenWiki(SearchItem item)
        {
            // NPCs, portals and live results have English names; open that exact page.
            if (item.Entry.Kind == EntryKind.Npc || item.Entry.Kind == EntryKind.WikiPage || item.Entry.Kind == EntryKind.Portal)
            {
                OpenWikiSearch(WikiTerm(item.Entry), "en");
                return;
            }

            // Otherwise the first of the two languages that has its own wiki (there's no Chinese wiki).
            var languages = _indexLanguages;
            var entries = new[] { item.Entry, item.Alternate };
            for (int i = 0; i < languages.Length; i++)
            {
                if (entries[i] != null && WikiHost(languages[i]) != null)
                {
                    OpenWikiSearch(WikiTerm(entries[i]), languages[i]);
                    return;
                }
            }
            OpenWikiSearch(WikiTerm(item.Entry), "en");
        }

        internal void OpenWikiSearch(string term) => OpenWikiSearch(term, _indexLanguages[0]);

        /// <summary>
        /// Asks the English wiki when nothing matched locally: finds pages about places, NPCs, objects and items the
        /// downloaded data doesn't have, placed on the map where possible. Failed searches aren't remembered.
        /// </summary>
        internal Task<List<SearchItem>> SearchWikiLiveAsync(string query)
        {
            string key = SearchIndex.Normalize(query);
            if (_wikiSearches.TryGetValue(key, out var existing) && !existing.IsFaulted && !existing.IsCanceled) return existing;

            var places = _places;
            var index = _index;
            var icons = _fileIcons;
            var search = Task.Run(async () =>
            {
                var pages = await _data.SearchWikiAsync(query, WikiResults, _unloading.Token);
                return pages.Select(page => new SearchItem(WikiEntries.FromPage(page, places, index, icons), null)).ToList();
            });
            _wikiSearches[key] = search;
            search.ContinueWith(t => Logger.Warn(t.Exception, $"Live wiki search for \"{query}\" failed."), TaskContinuationOptions.OnlyOnFaulted);
            return search;
        }

        private void OpenWikiSearch(string term, string language)
        {
            // Search rather than a page URL: the wiki jumps straight to an exact title match and lists the rest.
            string url = $"https://{WikiHost(language) ?? WikiHost("en")}/index.php?search={Uri.EscapeDataString(term)}";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Couldn't open {url}");
                _window.ShowMessage("Couldn't open your browser.");
            }
        }

        private static string WikiHost(string language) => language switch
        {
            "en" => "wiki.guildwars2.com",
            "de" => "wiki-de.guildwars2.com",
            "fr" => "wiki-fr.guildwars2.com",
            "es" => "wiki-es.guildwars2.com",
            _ => null,
        };

        /// <summary>Unnamed vistas are looked up by their map.</summary>
        private static string WikiTerm(SearchEntry entry) =>
            entry.Kind == EntryKind.Vista && entry.Name == Gw2Data.UnnamedVistaName ? entry.Context
            : entry.Kind == EntryKind.Portal ? entry.Name.Substring(entry.Name.IndexOf(Portal.NameSeparator) + Portal.NameSeparator.Length)
            : entry.Name;

        private sealed class StatusProgress : IProgress<string>
        {
            private readonly Action<string> _report;
            public StatusProgress(Action<string> report) => _report = report;
            public void Report(string value) => _report(value);
        }
    }
}
