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
using Bytex.ModuleShared;
using Blish_HUD.Controls.Extern;
using Keyboard = Blish_HUD.Controls.Intern.Keyboard;
using Mouse = Blish_HUD.Controls.Intern.Mouse;

namespace Bytex.TyriaSearch
{
    public enum SearchLanguage { None, English, German, French, Spanish }

    /// <summary>What clicking a result does. Blish HUD shows the names split into words.</summary>
    public enum ResultAction { CopyChatLink, PasteIntoChat, WhisperToYourself, ShowOnMap, OpenWikiPage, CopyWikiCommand, ToggleFavourite, ShowMenu }

    /// <summary>What clicking the corner icon does. Blish HUD shows the names split into words.</summary>
    public enum IconAction { OpenSettings, OpenSearch, Nothing }

    [Export(typeof(Module))]
    public class TyriaSearchModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<TyriaSearchModule>();

        // Full Download Every (Days): the slider's minimum stands for Every Update and its maximum for Never; otherwise
        // days between full downloads, with game updates in between only fetching what was added.
        private const int EveryUpdate = 6;
        private const int Never = 366;

        private SettingEntry<KeyBinding> _openKey;
        private SettingEntry<KeyBinding> _clearMarkerKey;
        private SettingEntry<bool> _openMapOnShow;
        private SettingEntry<bool> _keepSearchOpenOnMap;
        private SettingEntry<bool> _zoomOutMap;
        private SettingEntry<bool> _showPortals;
        private SettingEntry<bool> _showOnMinimap;
        private SettingEntry<bool> _learnPortals;
        private SettingEntry<KeyBinding> _gameMapKey;
        private SettingEntry<SearchLanguage> _secondLanguage;
        private SettingEntry<int> _fullDownloadDays;
        private SettingEntry<bool> _searchItems;
        private SettingEntry<bool> _searchNpcs;
        private SettingEntry<bool> _searchEvents;
        private SettingEntry<bool> _searchEffects;
        private SettingEntry<bool> _searchWaypoints, _searchPointsOfInterest, _searchVistas, _searchPortals, _searchSkills, _searchTraits;
        private SettingEntry<bool> _searchHearts, _searchHeroChallenges, _searchMasteryPoints, _searchAdventures;
        private SettingEntry<bool> _searchWiki, _wikiBelowResults;
        private SettingEntry<IconAction> _iconLeftClick;
        private SettingEntry<IconAction> _iconRightClick;
        private SettingEntry<bool> _registerCornerIcon;
        private SettingEntry<bool> _overwriteUnsentText;
        private SettingEntry<bool> _switchBackToSay;
        private SettingEntry<int> _keyDelayMs;
        private SettingEntry<ResultAction> _leftClick;
        private SettingEntry<ResultAction> _rightClick;
        private SettingEntry<List<string>> _favourites;
        private SettingEntry<int> _portalPanelX, _portalPanelY; // NoPosition until dragged
        private const int NoPosition = int.MinValue;

        private Texture2D _icon;
        // Shown for results without an icon of their own, by name (npc, item, skill, location, page).
        private readonly Dictionary<string, Texture2D> _fallbackIcons = new Dictionary<string, Texture2D>();
        private RegisteredCornerIcon _cornerIcon;
        private SettingsUsedWhen _mapOpenSettings; // Keep Search Open / Zoom Out only matter when Show on Map opens the map
        private StandardWindow _settingsWindow;
        private SearchWindow _window;
        private MapMarker _mapMarker;
        private Gw2Data _data;
        private List<Portal> _bundledPortals;
        private PortalLearner _portalLearner;
        private PortalReview _portalReview;
        private string _returnPromptMap; // where to go back to for the return portal of the last new one
        private PortalKind _returnKind; // the kind chosen for that portal, the return's likely kind too
        // Bundled and learned; set in Initialize, before any refresh reads it, and replaced when a portal is learned.
        private volatile List<Portal> _portals;

        // Written by the background refresh, read by the UI each frame.
        private volatile SearchIndex _index = SearchIndex.Empty;
        private volatile string[] _indexLanguages = { "en" };
        private volatile Dictionary<int, int> _continents = new Dictionary<int, int>();
        private volatile Places _places; // null until English map data is loaded
        private volatile DataSet _publishedData; // what the index was last built from, to rebuild it after a setting change
        private volatile Dictionary<string, string> _fileIcons; // /v2/files icons, saved with the NPCs; null without them

        // Live wiki searches by query, so retyping a query doesn't ask the wiki again. Main thread only.
        private readonly Dictionary<string, Task<List<SearchItem>>> _wikiSearches = new Dictionary<string, Task<List<SearchItem>>>();
        private const int WikiResults = 8;
        private volatile string _status = "Loading…";
        private volatile bool _downloading;

        private readonly CancellationTokenSource _unloading = new CancellationTokenSource();
        // Wiki icons by URL, loaded on first use (UI thread only), and saved under wiki-icons so each is downloaded once.
        // Also holds game icons that Blish HUD failed to download (see IconFor), fetched from the render service the same way.
        private readonly Dictionary<string, AsyncTexture2D> _wikiIcons = new Dictionary<string, AsyncTexture2D>();
        // Wiki icons dropped by Clear Cache, kept until unload since result rows may still draw them.
        private readonly List<AsyncTexture2D> _discardedWikiIcons = new List<AsyncTexture2D>();
        private string _wikiIconDirectory;
        private CancellationTokenSource _refreshCancel;
        private Task _refreshTask = Task.CompletedTask;
        private bool _refreshForced; // the latest refresh downloads everything
        private bool _busy;

        [ImportingConstructor]
        public TyriaSearchModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters)
        {
            Blocked.ModuleName = "Tyria Search";
        }

        internal SearchIndex Index => _index;
        internal string Status => _status;
        // A refresh replaced mid-download can leave the flag set; only the latest refresh running counts.
        internal bool Downloading => _downloading && !_refreshTask.IsCompleted;
        internal bool SearchWiki => _searchWiki.Value;
        internal bool WikiBelowResults => _wikiBelowResults.Value;
        internal ResultAction LeftClick => _leftClick.Value;
        internal ResultAction RightClick => _rightClick.Value;
        internal IReadOnlyList<string> Favourites => _favourites.Value;
        /// <summary>Game-style tooltips for skill, trait, item and effect results.</summary>
        internal GameTooltips Tooltips { get; private set; }
        internal bool IsFavourite(SearchItem item) => _favourites.Value.Contains(item.Entry.Key);
        internal int MapMarkerCount => _mapMarker?.Targets.Count ?? 0;

        // Index rebuilds create new entries, so match by what the marker shows rather than by reference.
        private SearchItem MarkerFor(SearchItem item) => _mapMarker.Targets.FirstOrDefault(t =>
            t.Entry.Name == item.Entry.Name && t.Entry.MapId == item.Entry.MapId && t.Entry.X == item.Entry.X && t.Entry.Y == item.Entry.Y);

        internal bool IsMarked(SearchItem item) => MarkerFor(item) != null;

        protected override void DefineSettings(SettingCollection settings)
        {
            _openKey = settings.DefineSetting("OpenKey", new KeyBinding(),
                () => "Open Search Hotkey",
                () => "Shows or hides the search window. You can also right-click the magnifier icon at the top left of the screen.");
            _clearMarkerKey = settings.DefineSetting("ClearMarkerKey", new KeyBinding(),
                () => "Clear Map Markers Hotkey",
                () => "Removes all Show on Map markers from the world map. Works with the map open.");
            _openMapOnShow = settings.DefineSetting("OpenMapOnShow", true,
                () => "Open Map After Show on Map",
                () => "After Show on Map, open the world map if it isn't already open, by pressing Game's Map Key below. Chat must be closed.");
            _keepSearchOpenOnMap = settings.DefineSetting("KeepSearchOpenOnMap", true,
                () => "Keep Search Open on the Map",
                () => "When Open Map After Show on Map opens the world map, leave the search window open. Off: hide it so it doesn't cover the map.");
            _zoomOutMap = settings.DefineSetting("ZoomOutMapOnShow", true,
                () => "Zoom Out Map After Opening",
                () => "When Open Map After Show on Map opens the world map, zoom it all the way out so the marker is on screen. Scrolls the mouse wheel, so keep the cursor over the game.");
            _showPortals = settings.DefineSetting("ShowPortals", false,
                () => "Show Portals on the World Map",
                () => "Marks every portal on the world map with the game's icons: zone portals, asura gates, dungeon, raid and fractal entrances. Each is labelled with where it leads once you zoom in.");
            _showOnMinimap = settings.DefineSetting("ShowOnMinimap", true,
                () => "Show on the Minimap",
                () => "Also draw Show on Map markers on the minimap, and portals when Show Portals on the World Map is on. A marker out of range sits at the minimap's edge, pointing the way.");
            // Saved under its old key, so the choice made with Show Corner Icon carries over.
            _registerCornerIcon = settings.DefineSetting("ShowCornerIcon", true,
                () => "Register Corner Icon",
                () => "Whether the module should register a corner icon. Off: open the search with Open Search Hotkey, and the settings with Settings on the module's page in Blish HUD's settings.");
            _iconLeftClick = settings.DefineSetting("IconLeftClick", IconAction.OpenSettings,
                () => "Corner Icon Left Click Action",
                () => "Defines the action of the corner icon when left clicked. Open Settings and Open Search close the window again if it's already open.");
            _iconRightClick = settings.DefineSetting("IconRightClick", IconAction.OpenSearch,
                () => "Corner Icon Right Click Action",
                () => "Defines the action of the corner icon when right clicked. Open Settings and Open Search close the window again if it's already open.");
            _learnPortals = settings.DefineSetting("LearnPortals", false,
                () => "Learn New Portals",
                () => "When you take a portal Tyria Search doesn't know, ask at the top of the screen whether to add it to the map and the search, named after where it took you, then ask you to go back through it for the return portal. Added portals are saved to learned-portals.json in the tyriasearch folder; Forget Learned Portals below removes them. Spots you choose Do Not Ask Again for This Location for are saved to ignored-portals.json; Forget Ignored Locations clears them. Maps you choose Do Not Ask Again for This Destination for are saved to ignored-destinations.json; Forget Ignored Destinations clears them. Portals to and from fractals, guild halls, homesteads, the Mistlock Sanctuary, PvP and WvW are never asked about, since the game takes you there from anywhere and their exits lead back to where you came from.");
            // Never enabled: it's the key to press in the game, not a Blish HUD hotkey.
            _gameMapKey = settings.DefineSetting("GameMapKey", new KeyBinding(Microsoft.Xna.Framework.Input.Keys.M),
                () => "Game's Map Key",
                () => "Set this to the World Map key in the game's options (Control Options, User Interface). M by default.");
            _secondLanguage = settings.DefineSetting("SecondLanguage", SearchLanguage.None,
                () => "Second Search Language",
                () => "Names are shown in Blish HUD's language (Blish HUD settings, Overlay, Language). Searches also match names in this language, so you can find what guildmates call something. Each language is a separate download.");
            _fullDownloadDays = settings.DefineSetting("FullDownloadDays", 30,
                () => "Full Download Every (Days)",
                () => "How often to download all search data again, to pick up renamed items and changes to existing maps. In between, a game update only fetches what was added. Every Update: download everything after each game update instead. Never: only what game updates add, and whatever is missing. Update Data downloads everything now.");
            _fullDownloadDays.SetRange(EveryUpdate, Never);
            _searchNpcs = settings.DefineSetting("SearchNpcs", true,
                () => "Search NPCs",
                () => "Include NPCs from the wiki, with where they are, their nearest waypoint and a map marker. NPC names are in English. About 2 MB from the wiki (a few minutes), downloaded again as often as Full Download Every (Days) says; also downloads English map data if neither search language is English.");
            _searchEvents = settings.DefineSetting("SearchEvents", true,
                () => "Search Events",
                () => "Include current events from the wiki, with where they start, their nearest waypoint and a map marker. Event names are in English. About 1 MB from the wiki (under a minute), downloaded again as often as Full Download Every (Days) says; also downloads English map data if neither search language is English.");
            _searchEffects = settings.DefineSetting("SearchEffects", true,
                () => "Search Effects",
                () => "Include effects: boons, conditions, control effects, auras, transformations and every other effect from the wiki (names in English, about 1 MB, a few minutes, downloaded again as often as Full Download Every (Days) says), and the effects of food, utility items and other consumables, which come with the item data while Search Items is on.");
            _searchItems = settings.DefineSetting("SearchItems", true,
                () => "Search Items",
                () => "Include all 75,000 items. The first download takes a few minutes per language and about 10 MB of disk space each.");
            // Downloaded with the places, which every result's map position needs; turning one off only leaves it out of the results.
            _searchWaypoints = settings.DefineSetting("SearchWaypoints", true, () => "Search Waypoints", () => "Include waypoints in the results.");
            _searchPointsOfInterest = settings.DefineSetting("SearchPointsOfInterest", true, () => "Search Points of Interest", () => "Include points of interest and other landmarks in the results.");
            _searchVistas = settings.DefineSetting("SearchVistas", true, () => "Search Vistas", () => "Include vistas in the results.");
            _searchHearts = settings.DefineSetting("SearchRenownHearts", true, () => "Search Renown Hearts", () => "Include renown hearts, by their task, in the results.");
            _searchHeroChallenges = settings.DefineSetting("SearchHeroChallenges", true, () => "Search Hero Challenges", () => "Include hero challenges in the results. They have no names; search for the map or \"hero challenge\".");
            _searchMasteryPoints = settings.DefineSetting("SearchMasteryPoints", true, () => "Search Mastery Points", () => "Include mastery points in the results. They have no names; search for the map or \"mastery point\".");
            _searchAdventures = settings.DefineSetting("SearchAdventures", true, () => "Search Adventures", () => "Include adventures in the results.");
            _searchPortals = settings.DefineSetting("SearchPortals", true, () => "Search Portals", () => "Include zone portals, asura gates and dungeon, raid and fractal entrances, bundled and learned, in the results.");
            _searchSkills = settings.DefineSetting("SearchSkills", true, () => "Search Skills",
                () => "Include skills in the results. Off: skills aren't downloaded, but those already downloaded are kept for when you turn it back on.");
            _searchTraits = settings.DefineSetting("SearchTraits", true, () => "Search Traits",
                () => "Include traits in the results. Off: traits aren't downloaded, but those already downloaded are kept for when you turn it back on.");
            _searchWiki = settings.DefineSetting("SearchWiki", true,
                () => "Search the Wiki",
                () => "List the Wiki category in the dropdown, and in Everything also ask the wiki once you pause typing and list the pages it finds below the results here. A row to search the wiki in your browser is always offered when nothing matches.");
            _wikiBelowResults = settings.DefineSetting("WikiBelowResults", true,
                () => "List Wiki Pages Below Results",
                () => "In every category (Everything included, but not Favourites), also ask the wiki once you pause typing, and list the pages it finds below the results here. Off: other categories only list the results here.");
            _overwriteUnsentText = settings.DefineSetting("OverwriteUnsentText", false,
                () => "Overwrite Unsent Chat Text",
                () => "For Whisper to Yourself: if the chat box still holds text you typed but didn't send, delete it and send the whisper. The deleted text can't be recovered. Off: stop and leave your text in chat.");
            _switchBackToSay = settings.DefineSetting("SwitchBackToSay", false,
                () => "Switch to /s After Whisper",
                () => "After Whisper to Yourself, switch chat back to /s, so the next message you type isn't also whispered to yourself. Off: chat stays on the whisper channel.");
            _keyDelayMs = settings.DefineSetting("KeyDelayMs", 60,
                () => "Key Press Delay (ms)",
                () => "Time between simulated key presses for Paste into Chat and Whisper to Yourself. Raise it if text is sometimes typed wrong or not sent. Below one game frame (about 16 ms at 60 FPS) keys may be missed.");
            _keyDelayMs.SetRange(20, 250);
            _leftClick = settings.DefineSetting("LeftClickAction", ResultAction.CopyChatLink,
                () => "Left-Click a Result",
                () => "What clicking a result does. When it can't be done for that result (no chat link, or no map position), the wiki page opens instead. Shift-click always shows the menu.");
            _rightClick = settings.DefineSetting("RightClickAction", ResultAction.ShowMenu,
                () => "Right-Click a Result",
                () => "What right-clicking a result does. When it can't be done for that result (no chat link, or no map position), the wiki page opens instead. Shift-click always shows the menu.");

            // Hidden from the settings panel; changed from the results' right-click menu.
            var saved = settings.AddSubCollection("Saved", renderInUi: false, lazyLoaded: false);
            _favourites = saved.DefineSetting("Favourites", new List<string>(),
                () => "Favourites",
                () => "Keys of favourite results.");
            _portalPanelX = saved.DefineSetting("PortalPanelX", NoPosition, () => "Portal panel X", () => "Where the new portal panel was dragged to.");
            _portalPanelY = saved.DefineSetting("PortalPanelY", NoPosition, () => "Portal panel Y", () => "Where the new portal panel was dragged to.");
        }

        // Blish HUD's settings page only opens the module's own settings window.
        public override IView GetSettingsView() => new OpenSettingsView(OpenSettingsWindow);

        /// <summary>The settings, in sections, for the settings window.</summary>
        private IView SettingsView() => new SettingsWithValuesView(
            new SettingsSection("Search Window", new SettingEntry[] { _registerCornerIcon,_iconLeftClick, _iconRightClick, _openKey, _leftClick, _rightClick }),
            new SettingsSection("Search Options", new SettingEntry[]
                {
                    _searchWaypoints, _searchPointsOfInterest, _searchVistas, _searchPortals, _searchHearts, _searchHeroChallenges,
                    _searchMasteryPoints, _searchAdventures, _searchNpcs, _searchEvents, _searchEffects, _searchItems, _searchSkills, _searchTraits, _searchWiki, _wikiBelowResults,
                }),
            new SettingsSection("Search Data", new SettingEntry[] { _secondLanguage, _fullDownloadDays },
                ("Update Data", "Download all search data again now: places, plus skills, traits, NPCs, events, effects and items when they're on. A few minutes with items. Game updates are handled automatically.", UpdateData),
                ("Clear Cache", "Delete all saved search data and wiki icons, then download them again. Use it if Update Data doesn't fix missing or wrong results or icons. Searches keep working with the old data until the new data arrives. Learned portals are kept.", ClearCache)),
            new SettingsSection("World Map", new SettingEntry[] { _clearMarkerKey, _openMapOnShow, _keepSearchOpenOnMap, _zoomOutMap, _gameMapKey, _showOnMinimap }),
            new SettingsSection("Portals", new SettingEntry[] { _showPortals, _learnPortals },
                ("Forget Learned Portals", "Remove every portal that Learn New Portals added. The bundled portals stay.", ForgetLearnedPortals),
                ("Forget Ignored Locations", "Ask again about portals at every spot you chose Do Not Ask Again for This Location for.", ForgetIgnoredLocations),
                ("Forget Ignored Destinations", "Ask again about portals to every map you chose Do Not Ask Again for This Destination for. Portals to and from fractals, guild halls, homesteads, the Mistlock Sanctuary, PvP and WvW are never asked about.", ForgetIgnoredDestinations))
                { BuildExtra = panel => LearnedPortalsList.Build(panel, _portalLearner) },
            new SettingsSection("Chat", new SettingEntry[] { _overwriteUnsentText, _switchBackToSay, _keyDelayMs }))
        {
            NamedValues = { [_fullDownloadDays] = new Dictionary<int, string> { [EveryUpdate] = "Every Update", [Never] = "Never" } },
        };

        internal void OpenSettingsWindow()
        {
            if (_settingsWindow == null) return; // not loaded yet
            if (!_settingsWindow.Visible) _settingsWindow.Show(SettingsView());
            _settingsWindow.BringWindowToFront();
        }

        private void ToggleSettingsWindow()
        {
            if (_settingsWindow.Visible) _settingsWindow.Hide();
            else OpenSettingsWindow();
        }

        // Textures and bundled files are loaded here rather than in Initialize, as Blish HUD's module guide asks.
        protected override Task LoadAsync()
        {
            if (_favourites.Value == null) _favourites.Value = new List<string>();

            string directory = ModuleParameters.DirectoriesManager.GetFullDirectoryPath("tyriasearch");
            _data = new Gw2Data(directory, ModuleParameters.Manifest.Version.ToString());
            Tooltips = new GameTooltips(_data, () => _indexLanguages[0], IconFor, RenderIcon, BundledIconFor, _unloading.Token);
            _wikiIconDirectory = System.IO.Path.Combine(directory, "wiki-icons");
            _icon = ModuleParameters.ContentsManager.GetTexture(@"textures\TyriaSearch.png");
            foreach (string name in KindIconNames.Concat(Gw2Data.BundledIconNames).Distinct())
                _fallbackIcons[name] = ModuleParameters.ContentsManager.GetTexture($@"textures\icon-{name}.png");
            _window = new SearchWindow(this, _icon);
            _mapMarker = new MapMarker(ContinentOf, IconFor, BundledIconFor) { Parent = GameService.Graphics.SpriteScreen };
            _mapMarker.RemoveRequested += MapMarker_RemoveRequested;
            using (var portals = ModuleParameters.ContentsManager.GetFileStream("portals.json"))
                _bundledPortals = Portal.Load(portals);
            using (var ignoredDestinations = ModuleParameters.ContentsManager.GetFileStream("ignored-destinations.json"))
                _portalLearner = new PortalLearner(directory, ignoredDestinations);
            _portalLearner.Changed += PortalLearner_Changed;
            _portalReview = new PortalReview(AddLearnedPortal, DiscardLearnedPortal, IgnorePortal, IgnorePortalDestination, _portalLearner.CancelReturn,
                () => _portalPanelX.Value == NoPosition ? (Point?)null : new Point(_portalPanelX.Value, _portalPanelY.Value),
                point => (_portalPanelX.Value, _portalPanelY.Value) = (point.X, point.Y));
            SetPortals();
            _mapOpenSettings = new SettingsUsedWhen(() => _openMapOnShow.Value, _keepSearchOpenOnMap, _zoomOutMap).On(_openMapOnShow);
            _cornerIcon = new RegisteredCornerIcon(_registerCornerIcon, _icon,
                () => RunIconAction(_iconLeftClick.Value), () => RunIconAction(_iconRightClick.Value),
                rightClickOnRelease: false, _iconLeftClick, _iconRightClick);
            // The same window as the search window, so the two look alike.
            _settingsWindow = new StandardWindow(AsyncTexture2D.FromAssetId(155997), new Rectangle(25, 26, 560, 640), new Rectangle(40, 50, 540, 590))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Tyria Search",
                Subtitle = "Settings",
                Emblem = _icon,
                Id = "bytex.tyriasearch.settings",
                SavesPosition = true,
            };

            _openKey.Value.Enabled = true;
            _openKey.Value.Activated += OpenKey_Activated;
            _clearMarkerKey.Value.Enabled = true;
            _clearMarkerKey.Value.Activated += ClearMarkerKey_Activated;
            _secondLanguage.SettingChanged += SecondLanguage_SettingChanged;
            _fullDownloadDays.SettingChanged += FullDownloadDays_SettingChanged;
            _searchItems.SettingChanged += DataSetting_SettingChanged;
            _searchNpcs.SettingChanged += DataSetting_SettingChanged;
            _searchEvents.SettingChanged += DataSetting_SettingChanged;
            _searchEffects.SettingChanged += DataSetting_SettingChanged;
            foreach (var option in SearchOptions) option.SettingChanged += SearchOption_SettingChanged;
            _searchWiki.SettingChanged += SearchWiki_SettingChanged;
            _showPortals.SettingChanged += MapDrawing_SettingChanged;
            _showOnMinimap.SettingChanged += MapDrawing_SettingChanged;
            GameService.Overlay.UserLocaleChanged += Overlay_UserLocaleChanged;
            GameService.Gw2Mumble.Info.BuildIdChanged += Info_BuildIdChanged;
            return Task.CompletedTask;
        }

        protected override void OnModuleLoaded(EventArgs e)
        {
            base.OnModuleLoaded(e);
            RequestRefresh(force: false, restart: false);
            ApplyMapDrawing();
        }

        private void ApplyMapDrawing()
        {
            _mapMarker.ShowPortals = _showPortals.Value;
            _mapMarker.ShowOnMinimap = _showOnMinimap.Value;
        }

        private void SetPortals() => _mapMarker.Portals = _portals = _bundledPortals.Concat(_portalLearner.Learned).ToList();

        private void LearnPortals()
        {
            if (_learnPortals.Value)
            {
                var known = _portals.Concat(_portalReview.Pending).ToList();
                var portal = _portalLearner.Update(_places, known, out bool isReturn);
                if (portal != null)
                {
                    if (isReturn) portal.Kind = _returnKind;
                    else if (!known.Any(p => p.MapId == portal.DestinationMapId && p.Destination == portal.Origin))
                    {
                        _portalLearner.ExpectReturn(portal.DestinationMapId, portal.MapId);
                        _returnPromptMap = portal.Origin;
                        _returnKind = portal.Kind;
                    }
                    _portalReview.Enqueue(portal, isReturn);
                }
            }
            _portalReview.Update(_portalLearner.ExpectingReturn ? _returnPromptMap : null);
        }

        private void AddLearnedPortal(Portal portal, bool isReturn)
        {
            _portalLearner.Add(portal);
            if (!isReturn) _returnKind = portal.Kind;
            ScreenNotification.ShowNotification($"Tyria Search added the portal from {portal.Origin} to {portal.Destination}.");
        }

        // Without the portal there's no return to add.
        private void DiscardLearnedPortal(Portal portal, bool isReturn)
        {
            if (!isReturn && _returnPromptMap == portal.Origin) _portalLearner.CancelReturn();
        }

        private void IgnorePortal(Portal portal, bool isReturn)
        {
            _portalLearner.Ignore(portal);
            DiscardLearnedPortal(portal, isReturn);
            ScreenNotification.ShowNotification($"Tyria Search won't ask about this portal on {portal.Origin} again.");
        }

        private void IgnorePortalDestination(Portal portal, bool isReturn)
        {
            _portalLearner.IgnoreDestination(portal.DestinationMap);
            DiscardLearnedPortal(portal, isReturn);
            ScreenNotification.ShowNotification($"Tyria Search won't ask about portals to {portal.DestinationMap} again.");
        }

        private void ForgetIgnoredDestinations()
        {
            int count = _portalLearner.IgnoredDestinations.Count;
            if (count == 0)
            {
                ScreenNotification.ShowNotification("Tyria Search has no ignored portal destinations.");
                return;
            }
            _portalLearner.ForgetIgnoredDestinations();
            ScreenNotification.ShowNotification($"Tyria Search will ask about portals to {count} ignored destination{(count == 1 ? "" : "s")} again.");
        }

        private void ForgetIgnoredLocations()
        {
            int count = _portalLearner.Ignored.Count;
            if (count == 0)
            {
                ScreenNotification.ShowNotification("Tyria Search has no ignored portal locations.");
                return;
            }
            _portalLearner.ForgetIgnored();
            ScreenNotification.ShowNotification($"Tyria Search will ask about {count} ignored portal location{(count == 1 ? "" : "s")} again.");
        }

        private void ForgetLearnedPortals()
        {
            int count = _portalLearner.Learned.Count;
            if (count == 0)
            {
                ScreenNotification.ShowNotification("Tyria Search hasn't learned any portals.");
                return;
            }
            _portalLearner.Forget();
            ScreenNotification.ShowNotification($"Tyria Search forgot {count} learned portal{(count == 1 ? "" : "s")}.");
        }

        protected override void Update(GameTime gameTime)
        {
            _window.Update();
            LearnPortals();
            _mapMarker.Size = GameService.Graphics.SpriteScreen.Size;
            var icon = _cornerIcon.Icon;
            string loading = Downloading ? _status : null;
            if (icon != null && icon.LoadingMessage != loading)
            {
                icon.LoadingMessage = loading;
                // Blish HUD only clears the loading tooltip if the mouse is on the icon when loading ends;
                // otherwise the last message stays as its tooltip for good.
                if (loading == null) icon.BasicTooltipText = null;
            }
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
            _fullDownloadDays.SettingChanged -= FullDownloadDays_SettingChanged;
            _searchItems.SettingChanged -= DataSetting_SettingChanged;
            _searchNpcs.SettingChanged -= DataSetting_SettingChanged;
            _searchEvents.SettingChanged -= DataSetting_SettingChanged;
            _searchEffects.SettingChanged -= DataSetting_SettingChanged;
            foreach (var option in SearchOptions) option.SettingChanged -= SearchOption_SettingChanged;
            _searchWiki.SettingChanged -= SearchWiki_SettingChanged;
            if (_portalLearner != null) _portalLearner.Changed -= PortalLearner_Changed;
            if (_mapMarker != null) _mapMarker.RemoveRequested -= MapMarker_RemoveRequested;
            _showPortals.SettingChanged -= MapDrawing_SettingChanged;
            _showOnMinimap.SettingChanged -= MapDrawing_SettingChanged;
            GameService.Overlay.UserLocaleChanged -= Overlay_UserLocaleChanged;
            GameService.Gw2Mumble.Info.BuildIdChanged -= Info_BuildIdChanged;
            _settingsWindow?.Dispose();
            _cornerIcon?.Dispose();
            _mapOpenSettings?.Dispose();
            _mapMarker?.Dispose();
            _portalReview?.Dispose();
            _window?.Dispose();
            _icon?.Dispose();
            foreach (var texture in _fallbackIcons.Values) texture.Dispose();
            foreach (var texture in _wikiIcons.Values.Concat(_discardedWikiIcons)) texture.Texture?.Dispose();
            _data?.Dispose();
        }

        private void RunIconAction(IconAction action)
        {
            switch (action)
            {
                case IconAction.OpenSettings: ToggleSettingsWindow(); break;
                case IconAction.OpenSearch: _window.Toggle(); break;
            }
        }

        // Search options that filter the downloaded data. Skills and traits are also left out of later downloads, and
        // downloaded when turned back on; NPCs, events, effects and items are handled by DataSetting_SettingChanged.
        private IEnumerable<SettingEntry<bool>> SearchOptions => new[]
        {
            _searchWaypoints, _searchPointsOfInterest, _searchVistas, _searchPortals, _searchSkills, _searchTraits,
            _searchHearts, _searchHeroChallenges, _searchMasteryPoints, _searchAdventures,
        };

        private void SearchOption_SettingChanged(object sender, ValueChangedEventArgs<bool> e)
        {
            _window.UpdateCategories();
            // Turned on, the refresh shows what was saved, then downloads it if it was left out.
            if (e.NewValue && (sender == _searchSkills || sender == _searchTraits)) RequestRefresh(force: false, restart: true);
            else RebuildIndex();
        }

        // Only the Wiki category comes and goes; the search data doesn't change.
        private void SearchWiki_SettingChanged(object sender, ValueChangedEventArgs<bool> e) => _window.UpdateCategories();

        private void PortalLearner_Changed(bool listChanged)
        {
            SetPortals();
            RebuildIndex();
        }
        private void OpenKey_Activated(object sender, EventArgs e) => _window.Toggle();
        private void ClearMarkerKey_Activated(object sender, EventArgs e) => ClearMapMarker();
        private void MapDrawing_SettingChanged(object sender, ValueChangedEventArgs<bool> e) => ApplyMapDrawing();
        private void SecondLanguage_SettingChanged(object sender, ValueChangedEventArgs<SearchLanguage> e) => RequestRefresh(force: false, restart: true);
        private void DataSetting_SettingChanged(object sender, ValueChangedEventArgs<bool> e)
        {
            _window.UpdateCategories();
            RequestRefresh(force: false, restart: true);
        }
        // Shortening it may make saved data due now. Not restarted: dragging the slider changes it many times.
        private void FullDownloadDays_SettingChanged(object sender, ValueChangedEventArgs<int> e) => RequestRefresh(force: false, restart: false);
        private void Overlay_UserLocaleChanged(object sender, ValueEventArgs<System.Globalization.CultureInfo> e) => RequestRefresh(force: false, restart: true);
        // Runs once Mumble connects, and again after a game update; data is only downloaded if the build changed.
        private void Info_BuildIdChanged(object sender, ValueEventArgs<int> e) => RequestRefresh(force: false, restart: false);

        private void UpdateData() => RequestRefresh(force: true, restart: true);

        /// <summary>
        /// Deletes the saved search data and wiki icons, then downloads everything again. Stops a refresh that's running
        /// and waits for it first, so it can't save its file again after the delete. The index in use stays until new
        /// data is published.
        /// </summary>
        private void ClearCache()
        {
            // Rows may still be drawing these, so they're disposed at unload rather than now.
            _discardedWikiIcons.AddRange(_wikiIcons.Values);
            _wikiIcons.Clear();

            _refreshCancel?.Cancel();
            var cancel = _refreshCancel = new CancellationTokenSource();
            _refreshForced = true;
            var previous = _refreshTask;
            _refreshTask = Task.Run(async () =>
            {
                try { await previous; }
                catch (Exception) { /* it logged its own failure */ }
                if (cancel.IsCancellationRequested) return; // a newer refresh replaced this one
                try
                {
                    _data.ClearCache();
                    if (System.IO.Directory.Exists(_wikiIconDirectory)) System.IO.Directory.Delete(_wikiIconDirectory, recursive: true);
                }
                catch (Exception ex)
                {
                    Blocked.Check(ex, _wikiIconDirectory);
                    Logger.Warn(ex, "Couldn't delete all the saved search data.");
                }
                await RefreshAsync(force: true, cancel.Token);
            });
            ScreenNotification.ShowNotification("Tyria Search cleared its cache and is downloading the data again.");
        }

        /// <summary>The result's own icon, else a bundled one for its kind.</summary>
        internal AsyncTexture2D IconFor(SearchEntry entry)
        {
            string icon = entry.Icon;
            if (icon?.StartsWith(SearchEntry.AssetIcon) == true && int.TryParse(icon.Substring(SearchEntry.AssetIcon.Length), out int assetId))
                return AsyncTexture2D.FromAssetId(assetId);
            // Skills, traits and items the wiki has no icon for either keep the API's missing icon, and a few wiki
            // pages give the wiki's placeholder: both show the kind's bundled icon.
            if (Gw2Data.IsMissingIcon(icon) || icon.StartsWith(SearchEntry.BundledIcon)) return BundledIconFor(entry);
            if (icon.StartsWith(Gw2Data.WikiImages)) return WikiIcon(icon);
            return RenderIcon(icon);
        }

        /// <summary>
        /// A render service icon by URL. Blish HUD only holds it weakly, so a caller drawing it every frame must keep it
        /// (asking again after it's collected gives a fresh, transparent texture until it reloads: a flicker), and ask
        /// again if <see cref="IsErrorTexture"/>, for the module's own download.
        /// </summary>
        internal AsyncTexture2D RenderIcon(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return null;
            if (_wikiIcons.TryGetValue(icon, out var fallback)) return fallback;
            // Blish HUD fetches render service icons from its asset mirror. If that fails (it can time out on hundreds at once),
            // Blish swaps in its error texture (a pink square with a blue X) and keeps returning it for the session. So the
            // module downloads the icon from the render service itself; rows built later pick it up.
            var texture = GameService.Content.GetRenderServiceTexture(icon);
            return IsErrorTexture(texture) ? WikiIcon(icon, trim: false) : texture;
        }

        /// <summary>Whether an icon has arrived and can be drawn: not still downloading, nor Blish HUD's error texture after a failed download.</summary>
        internal static bool IsLoaded(AsyncTexture2D icon) => icon != null && icon.HasSwapped && !IsErrorTexture(icon);

        /// <summary>Whether Blish HUD gave up downloading the icon and swapped in its error texture.</summary>
        internal static bool IsErrorTexture(AsyncTexture2D icon) => icon?.Texture == ContentService.Textures.Error;

        /// <summary>
        /// An icon downloaded by the module (from the wiki, or the render service as a fallback): unswapped (so the bundled
        /// placeholder shows) until it has loaded from disk or downloaded, and stays so if that fails.
        /// </summary>
        private AsyncTexture2D WikiIcon(string url, bool trim = true)
        {
            if (_wikiIcons.TryGetValue(url, out var texture)) return texture;
            texture = _wikiIcons[url] = new AsyncTexture2D();
            _ = LoadWikiIconAsync(url, texture, trim);
            return texture;
        }

        private async Task LoadWikiIconAsync(string url, AsyncTexture2D texture, bool trim)
        {
            string file;
            using (var sha = System.Security.Cryptography.SHA1.Create())
                file = System.IO.Path.Combine(_wikiIconDirectory,
                    string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url)).Select(b => b.ToString("x2"))) + ".png");
            try
            {
                byte[] bytes;
                if (System.IO.File.Exists(file)) bytes = System.IO.File.ReadAllBytes(file);
                else
                {
                    bytes = await _data.GetBytesAsync(url, _unloading.Token);
                    System.IO.Directory.CreateDirectory(_wikiIconDirectory);
                    System.IO.File.WriteAllBytes(file, bytes);
                }
                if (_unloading.IsCancellationRequested) return;
                using (var stream = new System.IO.MemoryStream(bytes))
                using (var context = GameService.Graphics.LendGraphicsDeviceContext())
                {
                    var loaded = TextureUtil.FromStreamPremultiplied(context.GraphicsDevice, stream);
                    texture.SwapTexture(trim ? TrimPadding(context.GraphicsDevice, loaded) : loaded);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Blocked.Check(ex, file);
                Logger.Debug(ex, $"Couldn't load the icon {url}.");
            }
        }

        // Game icons are 64 pixels or less; a bigger image whose picture fills less than this share of it is padded.
        private const int PaddedIconMinSize = 65;
        private const float PaddedIconMaxFill = 0.85f;

        /// <summary>
        /// Some wiki icons (the generic Monster Skill one) are a small picture in the middle of a large black or
        /// transparent canvas, so they'd look tiny. Cuts out the square around the picture so it fills the icon's space.
        /// </summary>
        private static Texture2D TrimPadding(GraphicsDevice device, Texture2D texture)
        {
            int width = texture.Width, height = texture.Height;
            if (width < PaddedIconMinSize || height < PaddedIconMinSize) return texture;
            var pixels = new Color[width * height];
            texture.GetData(pixels);

            // Near-black or near-transparent pixels are canvas.
            int left = width, top = height, right = -1, bottom = -1;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var pixel = pixels[y * width + x];
                if (pixel.A <= 16 || pixel.R + pixel.G + pixel.B <= 30) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
            if (right < 0) return texture; // all canvas
            int pictureWidth = right - left + 1, pictureHeight = bottom - top + 1;
            if (pictureWidth > width * PaddedIconMaxFill || pictureHeight > height * PaddedIconMaxFill) return texture;

            // A square around the picture's centre, inside the image, so the icon isn't stretched.
            int size = Math.Min(Math.Max(pictureWidth, pictureHeight), Math.Min(width, height));
            int startX = MathHelper.Clamp(left + pictureWidth / 2 - size / 2, 0, width - size);
            int startY = MathHelper.Clamp(top + pictureHeight / 2 - size / 2, 0, height - size);
            var cropped = new Color[size * size];
            for (int y = 0; y < size; y++) Array.Copy(pixels, (startY + y) * width + startX, cropped, y * size, size);

            var trimmed = new Texture2D(device, size, size);
            trimmed.SetData(cropped);
            texture.Dispose();
            return trimmed;
        }

        /// <summary>The result's bundled icon, else its kind's; also shown while its own icon downloads.</summary>
        internal Texture2D BundledIconFor(SearchEntry entry)
        {
            string icon = entry.Icon;
            string name = icon?.StartsWith(SearchEntry.BundledIcon) == true ? icon.Substring(SearchEntry.BundledIcon.Length) : KindIconName(entry.Kind);
            return BundledIcon(name);
        }

        /// <summary>A bundled icon by name (ref/textures/icon-&lt;name&gt;.png); null if it isn't loaded.</summary>
        internal Texture2D BundledIcon(string name) => _fallbackIcons.TryGetValue(name, out var texture) ? texture : null;

        /// <summary>
        /// Each kind's default icon, ref/textures/icon-&lt;name&gt;.png: the game's map icon where there is one (from /v2/files,
        /// or from Blish HUD's asset source: zone portals, the Hero panel's Skills and Traits tab icon for skills and
        /// traits (asset 156720), and the menu bar's Inventory icon for items (asset 157098) and Contacts icon for NPCs
        /// (asset 157112)), else a wiki one: the minimap's Compass icon (places). Wiki pages get a plain drawn one.
        /// </summary>
        private static string KindIconName(EntryKind kind) => kind switch
        {
            EntryKind.Waypoint => "waypoint",
            EntryKind.PointOfInterest => "poi",
            EntryKind.Vista => "vista",
            EntryKind.Portal => "portal",
            EntryKind.RenownHeart => "heart",
            EntryKind.HeroChallenge => "hero-challenge",
            EntryKind.MasteryPoint => "mastery-tyria",
            EntryKind.Adventure => "adventure",
            EntryKind.Event => "event",
            EntryKind.Effect => "effect",
            EntryKind.Npc => "npc",
            EntryKind.Item => "item",
            EntryKind.Skill => "skill",
            EntryKind.Trait => "trait",
            _ => "page",
        };

        // "location" is for wiki pages about places (see WikiEntries).
        private static IEnumerable<string> KindIconNames =>
            ((EntryKind[])Enum.GetValues(typeof(EntryKind))).Select(KindIconName).Append("location");

        // ---- Data ----

        /// <summary>Everything one refresh has loaded or downloaded so far.</summary>
        private sealed class DataSet
        {
            public string[] Languages;
            public readonly Dictionary<string, Gw2Data.CacheFile> Main = new Dictionary<string, Gw2Data.CacheFile>();
            public readonly Dictionary<string, Gw2Data.CacheFile> Items = new Dictionary<string, Gw2Data.CacheFile>();
            public Gw2Data.NpcCacheFile Npcs;
            public Gw2Data.EventCacheFile Events;
            public Gw2Data.EffectCacheFile Effects;
            public bool UseItems, UseNpcs, UseEvents, UseEffects;
            // Skill, Trait: what's downloaded with the places. Left out, the saved ones are kept but not updated.
            public List<EntryKind> MainParts;

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
            // A settings change during Update Data mustn't turn it into a partial update.
            force |= _refreshForced && !_refreshTask.IsCompleted;
            _refreshForced = force;
            _refreshCancel?.Cancel();
            var cancel = _refreshCancel = new CancellationTokenSource();
            _refreshTask = Task.Run(() => RefreshAsync(force, cancel.Token));
        }

        private async Task RefreshAsync(bool force, CancellationToken ct)
        {
            var data = new DataSet
            {
                Languages = SearchLanguages(), UseItems = _searchItems.Value, UseNpcs = _searchNpcs.Value, UseEvents = _searchEvents.Value, UseEffects = _searchEffects.Value,
                MainParts = new List<EntryKind>(),
            };
            if (_searchSkills.Value) data.MainParts.Add(EntryKind.Skill);
            if (_searchTraits.Value) data.MainParts.Add(EntryKind.Trait);
            string mainWhat = PartsName(data.MainParts, "places");
            // NPC and event locations are matched by English place names, since the wiki is English.
            var mainLanguages = data.UseNpcs || data.UseEvents ? data.Languages.Union(new[] { "en" }).ToArray() : data.Languages;
            var failed = new List<string>();

            T TryLoad<T>(Func<T> load, string what) where T : class
            {
                try { return load(); }
                catch (Exception ex) { Logger.Warn(ex, $"Couldn't read saved {what}; downloading it again."); return null; }
            }

            void ShowStatus(string text)
            {
                if (!ct.IsCancellationRequested) _status = text;
            }

            // Each download stands alone: if the wiki is down, items still update. The status names what's being
            // downloaded, then each step replaces it ("Updating English map data, 3 of 40 floors…").
            // language: null for the wiki's data, which is English only and isn't named by language.
            async Task Download(string what, string language, Func<IProgress<string>, Task> download)
            {
                _downloading = true;
                string subject = language == null ? "" : LanguageName(language) + " ";
                ShowStatus($"Updating {subject}{what}…");
                var progress = new StatusProgress(text => ShowStatus($"Updating {subject}{text}"));
                try
                {
                    await download(progress);
                    Publish(data, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Logger.Warn(ex, $"Updating {subject}{what} failed.");
                    failed.Add(subject + what);
                }
            }

            try
            {
                foreach (string language in mainLanguages) data.Main[language] = TryLoad(() => _data.LoadMain(language), $"{language} data");
                if (data.UseItems) foreach (string language in data.Languages) data.Items[language] = TryLoad(() => _data.LoadItems(language), $"{language} items");
                if (data.UseNpcs) data.Npcs = TryLoad(_data.LoadNpcs, "NPCs");
                if (data.UseEvents) data.Events = TryLoad(_data.LoadEvents, "events");
                if (data.UseEffects) data.Effects = TryLoad(_data.LoadEffects, "effects");
                Publish(data, ct);

                int build = GameService.Gw2Mumble.Info.BuildId;
                // Asked for once per refresh, and only if some language needs it; the same in every language.
                Task<string> fingerprint = null;
                Task<string> Fingerprint() => fingerprint ??= _data.MainFingerprintAsync(ct);

                // Most useful first: places, skills and traits, then NPCs and events, then the slow item download.
                // A game update only re-downloads places, skills and traits if maps, skills or traits were added or removed.
                // Skills and traits whose search option is off aren't downloaded; what was saved of them is kept.
                foreach (string language in mainLanguages)
                {
                    var saved = data.MainFor(language);
                    if (force || IsStale(saved?.DownloadedUtc, saved?.GameBuild ?? 0, build))
                        await Download(mainWhat, language, async progress =>
                            data.Main[language] = await _data.DownloadMainAsync(language, build, await Fingerprint(), saved, true, data.MainParts, progress, ct));
                    else if (IsOlderBuild(saved.GameBuild, build))
                        await Download(mainWhat, language, async progress =>
                        {
                            ShowStatus($"Checking {LanguageName(language)} {mainWhat} for changes…");
                            string current = await Fingerprint();
                            if (current == saved.Fingerprint) _data.ConfirmMain(saved, build);
                            else data.Main[language] = await _data.DownloadMainAsync(language, build, current, saved, false, data.MainParts, progress, ct);
                        });

                    // Turned back on after a download that left them out.
                    var latest = data.MainFor(language);
                    var missing = data.MainParts.Where(k => latest?.Skipped?.Contains(k) == true).ToList();
                    if (missing.Count > 0)
                        await Download(PartsName(missing, null), language,
                            async progress => data.Main[language] = await _data.DownloadSkippedAsync(latest, missing, progress, ct));
                }

                // The wiki changes a little at a time, so only Every Update ties it to game updates.
                // Saved without a build (an older file, or before Mumble connected): note the build rather than download.
                try
                {
                    if (data.UseNpcs && data.Npcs != null && data.Npcs.GameBuild == 0 && build > 0) _data.StampNpcs(data.Npcs, build);
                    if (data.UseEvents && data.Events != null && data.Events.GameBuild == 0 && build > 0) _data.StampEvents(data.Events, build);
                    if (data.UseEffects && data.Effects != null && data.Effects.GameBuild == 0 && build > 0) _data.StampEffects(data.Effects, build);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Logger.Warn(ex, "Couldn't save the game build with the wiki data.");
                }
                if (data.UseNpcs && (force || IsStale(data.Npcs?.DownloadedUtc, data.Npcs?.GameBuild ?? 0, build)))
                    await Download("NPCs", null, async progress => data.Npcs = await _data.DownloadNpcsAsync(build, progress, ct));
                if (data.UseEvents && (force || IsStale(data.Events?.DownloadedUtc, data.Events?.GameBuild ?? 0, build)))
                    await Download("events", null, async progress => data.Events = await _data.DownloadEventsAsync(build, progress, ct));
                if (data.UseEffects && (force || IsStale(data.Effects?.DownloadedUtc, data.Effects?.GameBuild ?? 0, build)))
                    await Download("effects", null, async progress => data.Effects = await _data.DownloadEffectsAsync(build, progress, ct));

                // A game update only fetches the items it added.
                if (data.UseItems)
                {
                    foreach (string language in data.Languages)
                    {
                        var saved = data.ItemsFor(language);
                        if (force || IsStale(saved?.DownloadedUtc, saved?.GameBuild ?? 0, build))
                            await Download("items", language,
                                async progress => data.Items[language] = await _data.DownloadItemsAsync(language, build, progress, ct));
                        else if (IsOlderBuild(saved.GameBuild, build))
                            await Download("items", language,
                                async progress => data.Items[language] = await _data.UpdateItemsAsync(saved, build, progress, ct));
                    }
                }

                ct.ThrowIfCancellationRequested();
                Publish(data, ct);
                if (failed.Count > 0)
                {
                    _status = _index.Count > 0
                        ? $"Couldn't update {string.Join(", ", failed)}; using what was saved earlier. Try Update Data later."
                        : "Couldn't download the data. Check your connection, then press Update Data.";
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
            if (data.UseEvents && data.Events != null && places != null)
                entries = entries.Concat(WikiEntries.BuildEvents(data.Events, places));
            // Effects have no place, so don't wait for the map data.
            if (data.UseEffects && data.Effects != null)
                entries = entries.Concat(WikiEntries.BuildEffects(data.Effects));
            if (places != null && _searchPortals.Value)
                entries = entries.Concat(Portal.Entries(_portals, places));
            var kinds = SearchedKinds();
            entries = entries.Where(e => kinds.Contains(e.Kind));
            alternates = alternates.Where(e => kinds.Contains(e.Kind));

            var index = new SearchIndex(entries, alternates);
            if (ct.IsCancellationRequested) return;
            _places = places;
            _fileIcons = data.Npcs?.Icons;
            _continents = (main.Places ?? new List<Gw2Data.Place>()).Where(p => p.IsMap)
                .GroupBy(p => p.MapId).ToDictionary(g => g.Key, g => g.First().Continent);
            _indexLanguages = second != null && data.MainFor(second) != null ? new[] { display, second } : new[] { display };
            _index = index;
            _publishedData = data;

            int days = (int)(DateTime.UtcNow - (main.CheckedUtc ?? main.DownloadedUtc)).TotalDays;
            string age = days <= 0 ? "today" : days == 1 ? "yesterday" : $"{days} days ago";
            _status = $"{index.Count:N0} results available, updated {age}"
                      + (_indexLanguages.Length > 1 ? $". Also matches {LanguageName(second)} names." : ".");
        }

        /// <summary>Whether that kind's search option is on, so the search window lists its category.</summary>
        internal bool Searches(EntryKind kind) => kind switch
        {
            EntryKind.Waypoint => _searchWaypoints.Value,
            EntryKind.PointOfInterest => _searchPointsOfInterest.Value,
            EntryKind.Vista => _searchVistas.Value,
            EntryKind.Portal => _searchPortals.Value,
            EntryKind.RenownHeart => _searchHearts.Value,
            EntryKind.HeroChallenge => _searchHeroChallenges.Value,
            EntryKind.MasteryPoint => _searchMasteryPoints.Value,
            EntryKind.Adventure => _searchAdventures.Value,
            EntryKind.Npc => _searchNpcs.Value,
            EntryKind.Event => _searchEvents.Value,
            EntryKind.Effect => _searchEffects.Value,
            EntryKind.Item => _searchItems.Value,
            EntryKind.Skill => _searchSkills.Value,
            EntryKind.Trait => _searchTraits.Value,
            EntryKind.WikiPage => _searchWiki.Value,
            _ => true,
        };

        /// <summary>The kinds of result the search options leave in. NPCs, events and items are left out of the data when off.</summary>
        private HashSet<EntryKind> SearchedKinds()
        {
            var kinds = new HashSet<EntryKind> { EntryKind.Npc, EntryKind.Event, EntryKind.Item, EntryKind.WikiPage, EntryKind.Portal };
            if (_searchWaypoints.Value) kinds.Add(EntryKind.Waypoint);
            if (_searchPointsOfInterest.Value) kinds.Add(EntryKind.PointOfInterest);
            if (_searchVistas.Value) kinds.Add(EntryKind.Vista);
            if (_searchHearts.Value) kinds.Add(EntryKind.RenownHeart);
            if (_searchHeroChallenges.Value) kinds.Add(EntryKind.HeroChallenge);
            if (_searchMasteryPoints.Value) kinds.Add(EntryKind.MasteryPoint);
            if (_searchAdventures.Value) kinds.Add(EntryKind.Adventure);
            if (_searchSkills.Value) kinds.Add(EntryKind.Skill);
            if (_searchTraits.Value) kinds.Add(EntryKind.Trait);
            // Consumables' effects come with the items whether or not effects are searched.
            if (_searchEffects.Value) kinds.Add(EntryKind.Effect);
            return kinds;
        }

        /// <summary>
        /// Builds the index again from the data last published, after a search option or the learned portals changed.
        /// Waits for a refresh still running, which may publish newer data first.
        /// </summary>
        private void RebuildIndex()
        {
            var cancel = _refreshCancel;
            if (cancel == null) return; // nothing loaded yet; the first refresh reads the settings
            var ct = cancel.Token;
            _refreshTask = _refreshTask.ContinueWith(_ =>
            {
                var data = _publishedData;
                if (data == null) return;
                try { Publish(data, ct); }
                catch (Exception ex) { Logger.Warn(ex, "Rebuilding the search index failed."); }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Missing, or due a full download to pick up renames and fixes that adding new things doesn't: after
        /// Full Download Every (Days), or with Every Update, once the game has updated since; with Never, only when missing.
        /// Data saved before Mumble connected has no build; its first check is the usual partial one, not a second full download.
        /// </summary>
        private bool IsStale(DateTime? downloadedUtc, int savedBuild, int currentBuild)
        {
            if (downloadedUtc == null) return true;
            int days = _fullDownloadDays.Value;
            if (days >= Never) return false;
            if (days <= EveryUpdate) return savedBuild > 0 && IsOlderBuild(savedBuild, currentBuild);
            return DateTime.UtcNow - downloadedUtc.Value > TimeSpan.FromDays(days);
        }

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

        /// <summary>"places, skills and traits", "places and skills", "traits"…: <paramref name="first"/> (or nothing), then the parts.</summary>
        private static string PartsName(IEnumerable<EntryKind> parts, string first)
        {
            var names = new List<string>();
            if (first != null) names.Add(first);
            if (parts.Contains(EntryKind.Skill)) names.Add("skills");
            if (parts.Contains(EntryKind.Trait)) names.Add("traits");
            return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
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
        internal int ContinentOf(int mapId) => _continents.TryGetValue(mapId, out int continent) ? continent : 1;

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
            _ = RunChatActionAsync(delay => ChatActions.WhisperToSelfAsync(item.Entry.ChatLink, delay, _overwriteUnsentText.Value, _switchBackToSay.Value));

        private async Task RunChatActionAsync(Func<int, Task> action)
        {
            if (_busy) return;
            _busy = true;
            // Those actions type through the clipboard, behind the player's back, so it's always put back.
            string previousClipboard = null;
            try
            {
                int delay = _keyDelayMs.Value;
                _window.ReleaseFocus();
                await ChatActions.FocusGameAsync();
                await Task.Delay(delay);

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
                if (previousClipboard != null)
                    await ClipboardUtil.WindowsClipboardService.SetTextAsync(previousClipboard);
                _busy = false;
            }
        }

        internal void ShowOnMap(SearchItem item)
        {
            if (!IsMarked(item)) _mapMarker.Targets.Add(item);
            _window.ShowMessage($"{item.Entry.Name} is marked on the world map. Open the map (M) to see it; right-click the marker to remove it.");
            if (_openMapOnShow.Value && !GameService.Gw2Mumble.UI.IsMapOpen) _ = OpenMapAsync();
        }

        private async Task OpenMapAsync()
        {
            try
            {
                _window.ReleaseFocus(); // the map key must reach the game
                if (!_keepSearchOpenOnMap.Value) _window.Hide(); // it would cover the map
                await ChatActions.FocusGameAsync();
                int delay = _keyDelayMs.Value;
                await Task.Delay(delay);

                var binding = _gameMapKey.Value;
                if (binding.PrimaryKey == Microsoft.Xna.Framework.Input.Keys.None)
                    throw new AbortException("Set Game's Map Key in Tyria Search's settings.");
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
                if (_zoomOutMap.Value) await ZoomMapOutAsync();
            }
            catch (AbortException ex)
            {
                ScreenNotification.ShowNotification($"Tyria Search couldn't open the map: {ex.Message}", ScreenNotification.NotificationType.Warning);
            }
        }

        // Fully zoomed in to fully out took 11 notches in testing; more at the widest zoom do nothing.
        private const int MaxZoomOutSteps = 15;
        // Notches sent one at a time, each waiting for the map to start zooming, before the rest are sent regardless:
        // the map may still be opening and miss the first.
        private const int MaxUnansweredSteps = 2;
        private const int AnswerTimeoutMs = 200;
        // Once the map takes the wheel, the rest are sent about a game frame apart, like a quick flick of the wheel.
        private const int FastStepMs = 20;
        // Blish HUD's Mouse.RotateWheel takes the distance modulo a notch (120), so a whole notch would send nothing:
        // this is the most it can send.
        private const int WheelStep = 119;

        /// <summary>
        /// Zooms the just-opened map all the way out, so the marker is on screen wherever it is. Sends a notch at a
        /// time until the map starts zooming, then the rest in a quick burst.
        /// </summary>
        private static async Task ZoomMapOutAsync()
        {
            var ui = GameService.Gw2Mumble.UI;
            if (!await ChatInput.WaitAsync(() => ui.IsMapOpen, 2000)) return;
            double startScale = await SettledMapScaleAsync();

            bool answered = false;
            for (int step = 1; step <= MaxZoomOutSteps; step++)
            {
                if (!ui.IsMapOpen) return; // the player closed it
                ChatInput.EnsureSafe(chatOpen: false);
                // Blish HUD only works out which of its controls is under the cursor when the mouse moves, so after the
                // click that started this it still has the hidden result row or menu item, and its mouse hook keeps the
                // wheel from the game. Forget it; the next mouse move works it out again.
                GameService.Input.Mouse.UnsetActiveControl();
                // Negative, towards the player: zoom out. Sent as real input; the game ignores wheel messages posted to it.
                Mouse.RotateWheel(-WheelStep, false, -1, -1, true);

                if (answered || step > MaxUnansweredSteps) await Task.Delay(FastStepMs);
                else answered = await ChatInput.WaitAsync(() => ui.MapScale > startScale * 1.01, AnswerTimeoutMs);
            }
        }

        /// <summary>
        /// The map's scale once the game reports it: right as the map opens, Mumble can still have the minimap's.
        /// Waits until it's the same for a few readings in a row, or half a second at most.
        /// </summary>
        private static async Task<double> SettledMapScaleAsync()
        {
            var ui = GameService.Gw2Mumble.UI;
            double scale = ui.MapScale;
            int same = 0;
            for (int waited = 0; waited < 500 && same < 3; waited += 20)
            {
                await Task.Delay(20);
                same = ui.MapScale == scale ? same + 1 : 0;
                scale = ui.MapScale;
            }
            return scale;
        }

        internal void ClearMapMarker()
        {
            if (_mapMarker.Targets.Count == 0) return;
            _mapMarker.Targets.Clear(); // the markers vanishing says enough
        }

        internal void RemoveMapMarker(SearchItem item)
        {
            _mapMarker.Targets.Remove(MarkerFor(item));
            _window.ShowMessage($"{item.Entry.Name} removed from the world map.");
        }

        // Remove Icon in a marker's right-click menu; the marker vanishing says enough.
        private void MapMarker_RemoveRequested(SearchItem item) => RemoveMapMarker(item);

        internal void ToggleFavourite(SearchItem item)
        {
            var favourites = new List<string>(_favourites.Value);
            if (!favourites.Remove(item.Entry.Key)) favourites.Add(item.Entry.Key);
            _favourites.Value = favourites; // a new list, so the setting notices the change and saves
            _window.RunSearch();
        }

        internal void OpenWiki(SearchItem item)
        {
            // NPCs, events, portals, the wiki's effects and live results have English names; open that exact page.
            var kind = item.Entry.Kind;
            if (kind == EntryKind.Npc || kind == EntryKind.Event || kind == EntryKind.WikiPage || kind == EntryKind.Portal
                || (kind == EntryKind.Effect && item.Entry.Id?.StartsWith(WikiEntries.WikiEffectPrefix) == true))
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

        /// <summary>
        /// Unnamed vistas, hero challenges and mastery points are looked up by their map: the last part of their context
        /// ("Shaemoor Fields · Queensdale").
        /// </summary>
        private static string WikiTerm(SearchEntry entry) =>
            entry.Kind == EntryKind.Vista && entry.Name == Gw2Data.UnnamedVistaName ? MapOf(entry)
            : entry.Kind == EntryKind.HeroChallenge || entry.Kind == EntryKind.MasteryPoint ? MapOf(entry)
            : entry.Kind == EntryKind.Portal ? entry.Name.Substring(entry.Name.IndexOf(Portal.NameSeparator) + Portal.NameSeparator.Length)
            // The wiki's effect pages are titled "Guard! (effect)" when a skill has the same name.
            : entry.Kind == EntryKind.Effect && entry.Id?.StartsWith(WikiEntries.WikiEffectPrefix) == true ? entry.Id.Substring(WikiEntries.WikiEffectPrefix.Length)
            : entry.Name;

        private static string MapOf(SearchEntry entry)
        {
            string context = entry.Context ?? "";
            int last = context.LastIndexOf(" · ", StringComparison.Ordinal);
            return last < 0 ? context : context.Substring(last + 3);
        }

        private sealed class StatusProgress : IProgress<string>
        {
            private readonly Action<string> _report;
            public StatusProgress(Action<string> report) => _report = report;
            public void Report(string value) => _report(value);
        }
    }
}
