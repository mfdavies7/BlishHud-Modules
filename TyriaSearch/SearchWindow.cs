using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace mfdavies7.TyriaSearch
{
    /// <summary>The search box, category filter, status line and results list.</summary>
    internal sealed class SearchWindow : IDisposable
    {
        private static readonly Logger Logger = Logger.GetLogger<SearchWindow>();

        private const int MaxResults = 100;
        private const int SearchDelayMs = 150; // wait for a pause in typing before searching
        private const int WikiSearchDelayMs = 700; // longer for the Wiki category, so typing doesn't ask the wiki for every word
        private const string EverythingCategory = "Everything";
        private const string WikiCategory = "Wiki";
        private const string FavouritesCategory = "Favourites";
        private const int MessageSeconds = 4;

        // The game's rarity colours, Legendary lightened to read on the dark window.
        private static readonly Dictionary<string, Color> RarityColors = new Dictionary<string, Color>
        {
            ["Junk"] = new Color(170, 170, 170),
            ["Fine"] = new Color(98, 164, 218),
            ["Masterwork"] = new Color(26, 147, 6),
            ["Rare"] = new Color(252, 208, 11),
            ["Exotic"] = new Color(255, 164, 5),
            ["Ascended"] = new Color(251, 62, 141),
            ["Legendary"] = new Color(180, 90, 255),
        };

        private sealed class Results
        {
            public int Version;
            public string Query;
            public List<SearchItem> Items;
            public bool Live;     // from the live wiki search
            public bool Failed;   // the live wiki search couldn't reach the wiki
            public bool WikiOnly; // the Wiki category: ask the wiki without searching here first
            public bool Appended; // Everything: live wiki results to list below the ones already shown
        }

        private const int MinLiveQueryLength = 3; // shorter queries match too much of the wiki to be useful

        // kind: the search option that lists the category, or null for one that's always there.
        private readonly (string label, Func<SearchItem, bool> filter, EntryKind? kind)[] _categories;
        private readonly TyriaSearchModule _module;
        private readonly StandardWindow _window;
        private readonly TextBox _query;
        private readonly Dropdown _category;
        private readonly Label _status;
        private readonly StandardButton _clearMarkers;
        private readonly FlowPanel _results;
        private readonly ContextMenuStrip _menu;

        private SearchItem _menuItem;
        private SearchIndex _shownIndex;
        private DateTime? _searchDue;
        private string _message;
        private DateTime _messageUntil;
        private List<SearchItem> _shownResults = new List<SearchItem>();

        // Searching 100,000 entries can take longer than a frame, so it runs in the background.
        private int _searchVersion;
        private int _shownVersion;
        private volatile Results _pending;
        private bool _enterQueued; // Enter pressed before the results for the latest text arrived

        // Everything also asks the wiki, once typing pauses for as long as the Wiki category waits.
        private DateTime? _wikiDue;
        private Results _wikiFor; // the results shown, which the wiki's are added to
        private ResultRow _wikiRow; // "Searching the wiki" below them

        public SearchWindow(TyriaSearchModule module, Texture2D emblem)
        {
            _module = module;
            _categories = new (string, Func<SearchItem, bool>, EntryKind?)[]
            {
                (EverythingCategory, _ => true, null), // also asks the wiki, see ShowResults
                ("Waypoints", i => i.Entry.Kind == EntryKind.Waypoint, EntryKind.Waypoint),
                ("Points of Interest", i => i.Entry.Kind == EntryKind.PointOfInterest, EntryKind.PointOfInterest),
                ("Vistas", i => i.Entry.Kind == EntryKind.Vista, EntryKind.Vista),
                ("Portals", i => i.Entry.Kind == EntryKind.Portal, EntryKind.Portal),
                ("Renown Hearts", i => i.Entry.Kind == EntryKind.RenownHeart, EntryKind.RenownHeart),
                ("Hero Challenges", i => i.Entry.Kind == EntryKind.HeroChallenge, EntryKind.HeroChallenge),
                ("Mastery Points", i => i.Entry.Kind == EntryKind.MasteryPoint, EntryKind.MasteryPoint),
                ("Adventures", i => i.Entry.Kind == EntryKind.Adventure, EntryKind.Adventure),
                ("NPCs", i => i.Entry.Kind == EntryKind.Npc, EntryKind.Npc),
                ("Events", i => i.Entry.Kind == EntryKind.Event, EntryKind.Event),
                ("Items", i => i.Entry.Kind == EntryKind.Item, EntryKind.Item),
                ("Skills", i => i.Entry.Kind == EntryKind.Skill, EntryKind.Skill),
                ("Traits", i => i.Entry.Kind == EntryKind.Trait, EntryKind.Trait),
                (FavouritesCategory, module.IsFavourite, null),
                (WikiCategory, _ => false, null), // searched live, see RunSearch
            };

            // The window background and regions from the Blish HUD window guide.
            _window = new StandardWindow(AsyncTexture2D.FromAssetId(155997), new Rectangle(25, 26, 560, 640), new Rectangle(40, 50, 540, 590))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Tyria Search",
                Emblem = emblem,
                Id = "mfdavies7.tyriasearch.window",
                SavesPosition = true,
            };

            int width = _window.ContentRegion.Width;
            const int categoryWidth = 160;
            const int clearWidth = 140;

            _query = new TextBox
            {
                Parent = _window,
                Location = new Point(0, 0),
                Size = new Point(width - categoryWidth - 10, 30),
                PlaceholderText = "Place, NPC, event, item, skill or trait, or a chat code",
            };
            _query.TextChanged += (_, __) => _searchDue = DateTime.UtcNow.AddMilliseconds(WikiSelected ? WikiSearchDelayMs : SearchDelayMs);
            _query.EnterPressed += Query_EnterPressed;

            _category = new Dropdown
            {
                Parent = _window,
                Location = new Point(width - categoryWidth, 2),
                Width = categoryWidth,
            };
            UpdateCategories();
            _category.SelectedItem = _categories[0].label;
            _category.ValueChanged += (_, __) => RunSearch();

            _status = new Label
            {
                Parent = _window,
                Location = new Point(2, 36),
                Size = new Point(width - 4 - clearWidth, 24),
                Font = GameService.Content.DefaultFont12,
                TextColor = new Color(200, 200, 200),
                VerticalAlignment = VerticalAlignment.Middle,
            };

            _clearMarkers = new StandardButton
            {
                Parent = _window,
                Location = new Point(width - clearWidth, 36),
                Size = new Point(clearWidth, 24),
                BasicTooltipText = "Remove all Show on Map markers from the world map.",
                Visible = false,
            };
            _clearMarkers.Click += (_, __) => _module.ClearMapMarker();

            _results = new FlowPanel
            {
                Parent = _window,
                Location = new Point(0, 66),
                Size = new Point(width, _window.ContentRegion.Height - 66),
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0, 2),
                CanScroll = true,
            };

            _menu = new ContextMenuStrip(() => MenuItems(_menuItem));
        }

        public void Toggle()
        {
            _window.ToggleWindow();
            if (!_window.Visible) return;
            _query.Focused = true;
            _query.SelectionStart = 0;
            _query.SelectionEnd = _query.Length;
        }

        public void Hide() => _window.Hide();

        /// <summary>
        /// Lists only the categories whose search option is on. If the selected one goes, Everything is selected instead.
        /// </summary>
        public void UpdateCategories()
        {
            var shown = _categories.Where(c => c.kind == null || _module.Searches(c.kind.Value)).Select(c => c.label).ToList();
            if (shown.SequenceEqual(_category.Items)) return;

            string selected = _category.SelectedItem;
            _category.Items.Clear();
            foreach (string label in shown) _category.Items.Add(label);
            _category.SelectedItem = shown.Contains(selected) ? selected : EverythingCategory;
        }

        private bool WikiSelected => _category.SelectedItem == WikiCategory;

        /// <summary>Hands keyboard focus back so simulated keys and the player's typing reach the game.</summary>
        public void ReleaseFocus() => _query.UnsetFocus();

        public void ShowMessage(string message)
        {
            _message = message;
            _messageUntil = DateTime.UtcNow.AddSeconds(MessageSeconds);
            _status.Text = message;
        }

        /// <summary>Called every frame by the module.</summary>
        public void Update()
        {
            if (_searchDue.HasValue && DateTime.UtcNow >= _searchDue.Value) RunSearch();
            if (_module.Index != _shownIndex) RunSearch(); // new data arrived

            var pending = _pending;
            if (pending != null && pending.Version == _searchVersion)
            {
                _pending = null;
                ShowResults(pending);
            }

            if (_wikiDue.HasValue && DateTime.UtcNow >= _wikiDue.Value)
            {
                _wikiDue = null;
                if (_wikiFor.Version == _searchVersion) AskWiki(_wikiFor.Version, _wikiFor.Query.Trim(), wikiOnly: false, appended: true);
            }

            int markers = _module.MapMarkerCount;
            _clearMarkers.Visible = markers > 0;
            _clearMarkers.Text = markers == 1 ? "Clear Map Marker" : $"Clear {markers} Markers";

            _status.Text = _message != null && DateTime.UtcNow < _messageUntil ? _message : StatusText();
        }

        private string StatusText()
        {
            if (_query.Text.Trim().Length > 0 || _module.Index.Count == 0 || _module.Downloading) return _module.Status;
            if (WikiSelected) return "Type to search the wiki. It's asked once you pause typing.";
            string menu = _module.RightClick == ResultAction.ShowMenu ? "right-click" : _module.LeftClick == ResultAction.ShowMenu ? "click" : "shift-click";
            return _shownResults.Count > 0
                ? $"Favourites. Type to search; {menu} a result for more."
                : $"Type to search. {char.ToUpper(menu[0])}{menu.Substring(1)} a result to add it to favourites.";
        }

        private void Query_EnterPressed(object sender, EventArgs e)
        {
            // Typed fast and pressed Enter: act once the results for what's in the box are in.
            if (_searchDue.HasValue || _shownVersion != _searchVersion)
            {
                _enterQueued = true;
                if (_searchDue.HasValue) RunSearch();
                return;
            }
            CopyTopResult();
        }

        private void CopyTopResult()
        {
            _enterQueued = false;
            var first = _shownResults.FirstOrDefault();
            if (first != null) _module.CopyAndReturnToGame(first);
        }

        /// <summary>Searches again in the background, e.g. after new data arrived or favourites changed.</summary>
        public void RunSearch()
        {
            _searchDue = null;
            _wikiDue = null;
            var index = _shownIndex = _module.Index;
            string query = _query.Text ?? "";
            var filter = _categories.First(c => c.label == _category.SelectedItem).filter;
            var favourites = _module.Favourites;
            int version = ++_searchVersion;

            // Straight to the wiki; ShowResults asks it.
            if (WikiSelected)
            {
                _pending = new Results { Version = version, Query = query, Items = new List<SearchItem>(), WikiOnly = true };
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var items = query.Trim().Length == 0
                        ? favourites.Select(index.Find).Where(i => i != null && filter(i)).ToList()
                        : index.Search(query, filter, MaxResults);
                    _pending = new Results { Version = version, Query = query, Items = items };
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"Search for \"{query}\" failed.");
                }
            });
        }

        private void ShowResults(Results results)
        {
            int rowWidth = _results.ContentRegion.Width - 20; // room for the scrollbar
            string query = results.Query.Trim();
            if (results.Appended)
            {
                AppendWikiResults(results, query, rowWidth);
                return;
            }

            _shownVersion = results.Version;
            _shownResults = results.Items;
            foreach (var child in _results.Children.ToList()) child.Dispose();
            _wikiRow = null;
            foreach (var item in _shownResults) AddRow(item, rowWidth);

            // Nothing found locally: ask the wiki, which knows places, NPCs and objects the downloaded data doesn't.
            bool askingWiki = false;
            if (results.Live)
            {
                AddBrowserRow(query, rowWidth, results.Failed
                    ? "Couldn't reach the wiki just now. Opens it in your browser instead."
                    : _shownResults.Count > 0 ? "More results in your browser."
                    : results.WikiOnly ? "Nothing on the wiki. Opens a full wiki search in your browser."
                    : "Nothing on the wiki either. Opens a full wiki search in your browser.");
            }
            else if (_shownResults.Count == 0 && query.Length > 0)
            {
                // The Wiki category asks even with Search the Wiki off: the player chose it.
                askingWiki = (results.WikiOnly || _module.SearchWiki) && query.Length >= MinLiveQueryLength && _category.SelectedItem != FavouritesCategory;
                if (askingWiki)
                {
                    _ = new ResultRow(null)
                    {
                        Parent = _results,
                        Width = rowWidth,
                        Title = $"Searching the wiki for \"{query}\"…",
                        Subtitle = results.WikiOnly ? "Asking the wiki." : "Nothing here matched.",
                    };
                    AskWiki(results.Version, query, results.WikiOnly, appended: false);
                }
                else
                {
                    AddBrowserRow(query, rowWidth, results.WikiOnly
                        ? $"Type at least {MinLiveQueryLength} letters to search here. Opens the wiki in your browser."
                        : "No results here. Opens the wiki in your browser.");
                }
            }
            // Everything: the wiki's pages go below what was found here, once typing pauses. Not for a pasted chat code.
            else if (_shownResults.Count > 0 && _category.SelectedItem == EverythingCategory && _module.SearchWiki
                     && query.Length >= MinLiveQueryLength && !query.StartsWith("[&"))
            {
                _wikiRow = new ResultRow(null)
                {
                    Parent = _results,
                    Width = rowWidth,
                    Title = $"Searching the wiki for \"{query}\"…",
                    Subtitle = "Asked once you pause typing; its pages are listed here.",
                };
                _wikiFor = results;
                _wikiDue = DateTime.UtcNow.AddMilliseconds(WikiSearchDelayMs - SearchDelayMs);
            }

            if (_enterQueued && !askingWiki) CopyTopResult();
        }

        private void AddRow(SearchItem item, int width)
        {
            var entry = item.Entry;
            var row = new ResultRow(item)
            {
                Parent = _results,
                Width = width,
                Title = entry.Name,
                Subtitle = Describe(item),
                Icon = _module.IconFor(entry),
                PlaceholderIcon = _module.BundledIconFor(entry),
                TitleColor = entry.Rarity != null && RarityColors.TryGetValue(entry.Rarity, out var color) ? color : Color.White,
                BasicTooltipText = Tooltip(item),
            };
            row.Click += Row_Click;
            row.RightMouseButtonReleased += Row_RightMouseButtonReleased;
        }

        /// <summary>
        /// Lists the wiki's pages below the results already shown, which stay as they are (and scrolled where they were).
        /// Pages named like a result here are left out.
        /// </summary>
        private void AppendWikiResults(Results results, string query, int rowWidth)
        {
            _wikiRow?.Dispose();
            _wikiRow = null;
            var names = new HashSet<string>(_shownResults.SelectMany(i => new[] { i.Entry.Name, i.Alternate?.Name }).Where(n => n != null),
                StringComparer.OrdinalIgnoreCase);
            var added = results.Items.Where(i => !names.Contains(i.Entry.Name)).ToList();
            _shownResults = _shownResults.Concat(added).ToList();
            foreach (var item in added) AddRow(item, rowWidth);
            AddBrowserRow(query, rowWidth, results.Failed ? "Couldn't reach the wiki just now. Opens it in your browser instead."
                : added.Count > 0 ? "More results in your browser."
                : "Nothing more on the wiki. Opens a full wiki search in your browser.");
        }

        private void AddBrowserRow(string query, int width, string subtitle)
        {
            var row = new ResultRow(null)
            {
                Parent = _results, Width = width, Title = $"Search the wiki for \"{query}\"", Subtitle = subtitle,
                PlaceholderIcon = _module.BundledIcon("page"),
            };
            row.Click += (_, __) => _module.OpenWikiSearch(query);
        }

        /// <param name="appended">Everything: the pages go below the results already shown instead of replacing them.</param>
        private void AskWiki(int version, string query, bool wikiOnly, bool appended)
        {
            _module.SearchWikiLiveAsync(query).ContinueWith(t =>
            {
                bool ok = t.Status == TaskStatus.RanToCompletion;
                // Shown only if nothing was typed since (same version).
                _pending = new Results
                {
                    Version = version, Query = query, Items = ok ? t.Result : new List<SearchItem>(), Live = true, Failed = !ok,
                    WikiOnly = wikiOnly, Appended = appended,
                };
            });
        }

        private string Describe(SearchItem item)
        {
            string kind = item.Entry.Kind switch
            {
                EntryKind.Waypoint => "Waypoint",
                EntryKind.PointOfInterest => "Point of Interest",
                EntryKind.Vista => "Vista",
                EntryKind.Skill => "Skill",
                EntryKind.Trait => "Trait",
                EntryKind.Item => "Item",
                EntryKind.Npc => "NPC",
                EntryKind.Portal => "Portal",
                EntryKind.RenownHeart => "Renown Heart",
                EntryKind.HeroChallenge => "Hero Challenge",
                EntryKind.MasteryPoint => "Mastery Point",
                EntryKind.Adventure => "Adventure",
                EntryKind.Event => "Event",
                _ => "Wiki Page",
            };
            var parts = new List<string> { kind };
            if (_module.IsFavourite(item)) parts.Insert(0, "Favourite");
            if (!string.IsNullOrEmpty(item.Entry.Context)) parts.Add(item.Entry.Context);
            // Show the other language's name when it differs, so players can tell guildmates either one.
            if (item.Alternate != null && item.Alternate.Name != item.Entry.Name) parts.Add(item.Alternate.Name);
            return string.Join(" · ", parts);
        }

        private string Tooltip(SearchItem item)
        {
            var entry = item.Entry;
            string position = !entry.X.HasValue ? ""
                : entry.Approximate ? "\nPosition: centre of the area (the wiki has no exact spot)."
                : entry.Kind == EntryKind.Npc || entry.Kind == EntryKind.Event || entry.Kind == EntryKind.WikiPage ? "\nPosition: exact, from the wiki."
                : entry.Kind == EntryKind.Portal ? "\nPosition: exact, from Tekkit's marker pack or where you took it." : "";
            string link = entry.ChatLink == null ? "No chat link or waypoint known."
                : entry.LinkIsNearestWaypoint ? $"Nearest waypoint {entry.ChatLink}" : entry.ChatLink;
            string clicks = $"Click: {ActionText(_module.LeftClick, item)}. Right-click: {ActionText(_module.RightClick, item)}.";
            if (_module.LeftClick != ResultAction.ShowMenu && _module.RightClick != ResultAction.ShowMenu) clicks += " Shift-click: show the menu.";
            return $"{link}\n{clicks}{position}";
        }

        /// <summary>Whether the action can be done for this result; if not, clicking opens the wiki page.</summary>
        private static bool CanDo(ResultAction action, SearchEntry entry) => action switch
        {
            ResultAction.CopyChatLink => entry.ChatLink != null,
            ResultAction.PasteIntoChat => entry.ChatLink != null,
            ResultAction.WhisperToYourself => entry.ChatLink != null,
            ResultAction.ShowOnMap => entry.X.HasValue,
            ResultAction.ToggleFavourite => entry.Kind != EntryKind.WikiPage, // live wiki results aren't kept
            _ => true,
        };

        private string ActionText(ResultAction action, SearchItem item)
        {
            var entry = item.Entry;
            string link = entry.LinkIsNearestWaypoint ? "the nearest waypoint" : "the chat link";
            if (!CanDo(action, entry)) action = ResultAction.OpenWikiPage;
            return action switch
            {
                ResultAction.CopyChatLink => $"copy {link}",
                ResultAction.PasteIntoChat => $"paste {link} into chat",
                ResultAction.WhisperToYourself => $"whisper {link} to yourself",
                ResultAction.ShowOnMap => "show on the map",
                ResultAction.CopyWikiCommand => "copy the /wiki command",
                ResultAction.ToggleFavourite => _module.IsFavourite(item) ? "remove from favourites" : "add to favourites",
                ResultAction.ShowMenu => "show the menu",
                _ => "open the wiki page",
            };
        }

        private void Row_Click(object sender, MouseEventArgs e) => Act(_module.LeftClick, ((ResultRow)sender).Item);

        private void Row_RightMouseButtonReleased(object sender, MouseEventArgs e) => Act(_module.RightClick, ((ResultRow)sender).Item);

        private void Act(ResultAction action, SearchItem item)
        {
            // Shift-click keeps the menu reachable whatever both buttons are set to.
            if (action == ResultAction.ShowMenu || GameService.Input.Keyboard.ActiveModifiers.HasFlag(ModifierKeys.Shift))
            {
                _menuItem = item;
                _menu.Show(GameService.Input.Mouse.Position);
                return;
            }
            // Nothing to copy or place (e.g. an NPC somewhere vague): the wiki page is the next best thing.
            if (!CanDo(action, item.Entry)) action = ResultAction.OpenWikiPage;
            switch (action)
            {
                case ResultAction.CopyChatLink: _module.Copy(item); break;
                case ResultAction.PasteIntoChat: _module.PasteIntoChat(item); break;
                case ResultAction.WhisperToYourself: _module.WhisperToSelf(item); break;
                case ResultAction.ShowOnMap: _module.ShowOnMap(item); break;
                case ResultAction.CopyWikiCommand: _module.CopyWikiCommand(item); break;
                case ResultAction.ToggleFavourite: _module.ToggleFavourite(item); break;
                default: _module.OpenWiki(item); break;
            }
        }

        private IEnumerable<ContextMenuStripItem> MenuItems(SearchItem item)
        {
            if (item == null) yield break;

            ContextMenuStripItem Add(string text, Action action, string tooltip)
            {
                var menuItem = new ContextMenuStripItem(text) { BasicTooltipText = tooltip };
                menuItem.Click += (_, __) => action();
                return menuItem;
            }

            var entry = item.Entry;
            if (entry.ChatLink != null)
            {
                string link = entry.LinkIsNearestWaypoint ? "Nearest Waypoint" : "Chat Link";
                yield return Add($"Copy {link}", () => _module.Copy(item), "Copy the chat code to the clipboard.");
                yield return Add($"Paste {link} into Chat", () => _module.PasteIntoChat(item), "Open chat and paste the link, ready to send.");
                yield return Add($"Whisper {link} to Yourself", () => _module.WhisperToSelf(item), "Whisper the link to your own character so you can click it in chat.");
            }
            if (_module.IsMarked(item))
                yield return Add("Remove from Map", () => _module.RemoveMapMarker(item), "Remove this result's marker from the world map.");
            else if (entry.X.HasValue)
                yield return Add("Show on Map", () => _module.ShowOnMap(item), "Mark it on the world map, alongside any other markers. Open the map (M) to see it.");
            yield return Add("Copy /wiki Command", () => _module.CopyWikiCommand(item), "Paste it in chat to open the wiki page.");
            yield return Add("Open Wiki Page", () => _module.OpenWiki(item), "Open the wiki in your browser.");
            // Live wiki results aren't kept, so they can't be favourites.
            if (entry.Kind != EntryKind.WikiPage)
            {
                yield return _module.IsFavourite(item)
                    ? Add("Remove from Favourites", () => _module.ToggleFavourite(item), "Favourites show when the search box is empty.")
                    : Add("Add to Favourites", () => _module.ToggleFavourite(item), "Favourites show when the search box is empty.");
            }
            if (_module.MapMarkerCount > 0)
                yield return Add("Clear All Map Markers", _module.ClearMapMarker, "Remove every marker from the world map.");
        }

        public void Dispose()
        {
            _menu.Dispose();
            _window.Dispose();
        }
    }
}
