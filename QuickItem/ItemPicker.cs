using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules.Managers;
using Gw2Sharp.ChatLinks;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using Color = Microsoft.Xna.Framework.Color;

namespace Bytex.QuickItem
{
    /// <summary>An item the player can pick, with where they have it.</summary>
    internal sealed class PickerItem
    {
        public int Id;
        public string Name;
        public string Icon;
        public string Rarity;
        public int Count;
        /// <summary>Where it is: "This character", "Shared slots", another character's name, "Chat link", or "Inventory button".</summary>
        public string Where;
        public bool OnThisCharacter;
        /// <summary>A built-in action (<see cref="BarItem.DepositMaterials"/>) instead of an item; null for an item.</summary>
        public string Action;
        /// <summary>The picture to show instead of the item's icon (for an action); null to use <see cref="Icon"/>.</summary>
        public AsyncTexture2D Texture;
    }

    /// <summary>
    /// The window for choosing an item: the ready-made actions (Deposit All Materials), the items in your bags and
    /// shared inventory slots (from the API, so it needs an API key), or any item by chat link or id.
    /// </summary>
    internal sealed class ItemPicker : IDisposable
    {
        private static readonly Logger Logger = Logger.GetLogger<ItemPicker>();

        private static readonly TokenPermission[] Needed = { TokenPermission.Account, TokenPermission.Characters, TokenPermission.Inventories };
        private const int MaxRows = 100;
        // The API refreshes inventories every few minutes; no point asking more often.
        private static readonly TimeSpan OwnedFreshFor = TimeSpan.FromMinutes(2);

        private readonly Gw2ApiManager _api;
        private readonly StandardWindow _window;
        private readonly TextBox _query;
        private readonly Label _status;
        private readonly FlowPanel _results;
        private readonly CancellationToken _unloading;
        /// <summary>Ready-made entries that aren't items, listed first.</summary>
        private readonly List<PickerItem> _actions;

        private Action<PickerItem> _chosen;
        private List<PickerItem> _owned;
        private DateTime _ownedAtUtc;
        private string _ownedProblem;
        private Task _loading = Task.CompletedTask;
        private int _lookupVersion;

        public ItemPicker(Gw2ApiManager api, Texture2D emblem, AsyncTexture2D depositIcon, CancellationToken unloading)
        {
            _api = api;
            _unloading = unloading;
            _actions = new List<PickerItem>
            {
                new PickerItem
                {
                    Name = BarItem.DepositMaterialsName, Action = BarItem.DepositMaterials, Texture = depositIcon,
                    Where = "Inventory button", OnThisCharacter = true,
                },
            };
            // The same window as the settings, so the two look alike.
            _window = new StandardWindow(AsyncTexture2D.FromAssetId(155997), new Rectangle(25, 26, 560, 640), new Rectangle(40, 50, 540, 590))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Quick Item",
                Subtitle = "Choose Item",
                Emblem = emblem,
                Id = "bytex.quickitem.picker",
                SavesPosition = true,
            };

            int width = _window.ContentRegion.Width;
            _query = new TextBox
            {
                Parent = _window,
                Location = new Point(0, 0),
                Width = width,
                PlaceholderText = "Search your items, or paste a chat link or item ID",
            };
            _query.TextChanged += (_, __) => Refresh();
            _status = new Label
            {
                Parent = _window,
                Location = new Point(2, _query.Bottom + 4),
                Width = width - 4,
                AutoSizeHeight = true,
                WrapText = true,
                TextColor = Color.LightGray,
            };
            _results = new FlowPanel
            {
                Parent = _window,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0, 2),
                CanScroll = true,
            };
            _status.Resized += (_, __) => PlaceResults();
            PlaceResults();
            _api.SubtokenUpdated += Api_SubtokenUpdated;
        }

        /// <summary>
        /// Blish HUD gave the module new API permissions (a key added or changed): the items are loaded again, at once if
        /// the window is showing, as Blish HUD's API guide suggests.
        /// </summary>
        private void Api_SubtokenUpdated(object sender, ValueEventArgs<IEnumerable<TokenPermission>> e) => OnUiThread(() =>
        {
            if (_unloading.IsCancellationRequested) return;
            _owned = null;
            if (!_window.Visible || !_loading.IsCompleted) return;
            _loading = LoadOwnedAsync();
            Refresh();
        });

        private void PlaceResults()
        {
            int top = _status.Bottom + 6;
            _results.Location = new Point(0, top);
            _results.Size = new Point(_window.ContentRegion.Width, _window.ContentRegion.Height - top);
        }

        /// <summary>Opens the window; <paramref name="chosen"/> is called with the item picked.</summary>
        public void Open(string title, Action<PickerItem> chosen)
        {
            _chosen = chosen;
            _window.Subtitle = title;
            _query.Text = "";
            _window.Show();
            _window.BringWindowToFront();
            _query.Focused = true;
            if (_loading.IsCompleted && (_owned == null || DateTime.UtcNow - _ownedAtUtc > OwnedFreshFor))
                _loading = LoadOwnedAsync();
            Refresh();
        }

        public void Hide() => _window.Hide();

        private async Task LoadOwnedAsync()
        {
            _ownedProblem = null;
            try
            {
                if (!_api.HasPermissions(Needed))
                {
                    _ownedProblem = "To list your items, add an API key with the account, characters and inventories permissions in Blish HUD's settings (API Keys), and allow Quick Item to use them. Meanwhile, paste an item's chat link: shift-click the item into chat, then select and copy it.";
                    return;
                }

                OnUiThread(() => SetStatus("Loading your items…"));
                var client = _api.Gw2ApiClient.V2;
                string current = GameService.Gw2Mumble.PlayerCharacter.Name;
                var where = new Dictionary<int, (int count, HashSet<string> places)>();
                void Count(int id, int count, string place)
                {
                    if (!where.TryGetValue(id, out var entry)) entry = (0, new HashSet<string>());
                    entry.places.Add(place);
                    where[id] = (entry.count + count, entry.places);
                }

                var names = await client.Characters.IdsAsync(_unloading);
                var inventories = await Task.WhenAll(names.Select(async name =>
                {
                    try { return (name, bags: await client.Characters[name].Inventory.GetAsync(_unloading)); }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        Logger.Warn(ex, $"Couldn't read {name}'s inventory.");
                        return (name, bags: (CharactersInventory)null);
                    }
                }));
                foreach (var (name, bags) in inventories)
                    foreach (var bag in bags?.Bags ?? Enumerable.Empty<CharacterInventoryBag>())
                        foreach (var item in bag?.Inventory ?? Enumerable.Empty<AccountItem>())
                            if (item != null) Count(item.Id, item.Count, name);

                foreach (var item in await client.Account.Inventory.GetAsync(_unloading))
                    if (item != null) Count(item.Id, item.Count, "Shared slots");

                var details = new List<Item>();
                var ids = where.Keys.ToList();
                for (int start = 0; start < ids.Count; start += 200)
                    details.AddRange(await client.Items.ManyAsync(ids.Skip(start).Take(200), _unloading));

                _owned = details.Where(d => !string.IsNullOrEmpty(d.Name)).Select(d =>
                {
                    var (count, places) = where[d.Id];
                    bool here = current != null && places.Contains(current);
                    string place = here ? "This character"
                        : places.Contains("Shared slots") ? "Shared slots"
                        : places.Count == 1 ? places.First() : $"{places.Count} characters";
                    return new PickerItem
                    {
                        Id = d.Id, Name = d.Name, Icon = d.Icon.Url?.ToString(), Rarity = d.Rarity.ToString(),
                        Count = count, Where = place, OnThisCharacter = here || places.Contains("Shared slots"),
                    };
                }).ToList();
                _ownedAtUtc = DateTime.UtcNow;
            }
            catch (OperationCanceledException) when (_unloading.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Couldn't load the items in your inventories.");
                _ownedProblem = "Couldn't load your items from the API. Try again in a moment, or paste an item's chat link.";
            }
            OnUiThread(Refresh);
        }

        /// <summary>Controls are changed on Blish HUD's update thread, not mid-draw from a download.</summary>
        private static void OnUiThread(Action action) => GameService.Overlay.QueueMainThreadUpdate(_ => action());

        private void SetStatus(string text) => _status.Text = text ?? "";

        /// <summary>Shows what matches the query. Called on typing and when the owned items arrive.</summary>
        private void Refresh()
        {
            string query = _query.Text.Trim();
            int version = Interlocked.Increment(ref _lookupVersion);

            if (TryParseItemId(query, out int itemId))
            {
                ShowRows(new List<PickerItem>());
                SetStatus("Looking up the item…");
                _ = LookUpAsync(itemId, version);
                return;
            }

            // The ready-made actions need no API key, so they're listed whatever happens with the items.
            var actions = _actions.Where(a => query.Length == 0 || a.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            if (!_loading.IsCompleted)
            {
                SetStatus("Loading your items…");
                ShowRows(actions);
                return;
            }
            if (_owned == null)
            {
                SetStatus(_ownedProblem);
                ShowRows(actions);
                return;
            }

            var matches = _owned
                .Where(i => query.Length == 0 || i.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .OrderByDescending(i => i.OnThisCharacter)
                .ThenByDescending(i => query.Length > 0 && i.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
                .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            SetStatus(matches.Count == 0 ? "None of your items match. Paste a chat link to add any item."
                : matches.Count > MaxRows ? $"{matches.Count} items; showing the first {MaxRows}. Type to narrow them down."
                : "Your items on this character and in shared slots come first. You can only use items in this character's bags.");
            ShowRows(actions.Concat(matches.Take(MaxRows)).ToList());
        }

        /// <summary>An item chat link ("[&amp;AgH1WQAA]") or a plain item id.</summary>
        private static bool TryParseItemId(string text, out int id)
        {
            id = 0;
            if (int.TryParse(text, out id)) return id > 0;
            return Gw2ChatLink.TryParse(text, out IGw2ChatLink link) && link is ItemChatLink item && (id = item.ItemId) > 0;
        }

        private async Task LookUpAsync(int id, int version)
        {
            try
            {
                var item = await _api.Gw2ApiClient.V2.Items.GetAsync(id, _unloading);
                OnUiThread(() =>
                {
                    if (version != _lookupVersion) return;
                    var owned = _owned?.FirstOrDefault(o => o.Id == id);
                    SetStatus(owned == null ? "Not in your inventories, as far as the API knows. You can still add it." : null);
                    ShowRows(new List<PickerItem>
                    {
                        owned ?? new PickerItem { Id = item.Id, Name = item.Name, Icon = item.Icon.Url?.ToString(), Rarity = item.Rarity.ToString(), Where = "Chat link" },
                    });
                });
            }
            catch (OperationCanceledException) when (_unloading.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Item {id} couldn't be looked up.");
                OnUiThread(() =>
                {
                    if (version == _lookupVersion) SetStatus("No item with that chat link or ID was found.");
                });
            }
        }

        private void ShowRows(List<PickerItem> items)
        {
            foreach (var row in _results.Children.ToList()) row.Dispose();
            foreach (var item in items)
            {
                var row = new PickerRow(item) { Parent = _results, Width = _results.ContentRegion.Width - 12 };
                row.Click += (_, __) => Choose(item);
            }
        }

        private void Choose(PickerItem item)
        {
            if (item.Action == null && string.IsNullOrEmpty(item.Icon))
            {
                SetStatus($"{item.Name} has no icon, so it can't be found in the inventory.");
                return;
            }
            _window.Hide();
            var chosen = _chosen;
            _chosen = null;
            chosen?.Invoke(item);
        }

        public void Dispose()
        {
            _api.SubtokenUpdated -= Api_SubtokenUpdated;
            _window.Dispose();
        }

        /// <summary>A row in the list: icon, name (in its rarity's colour) and where you have it.</summary>
        private sealed class PickerRow : Control
        {
            private readonly PickerItem _item;
            private readonly AsyncTexture2D _icon;

            public PickerRow(PickerItem item)
            {
                _item = item;
                Height = 38;
                int asset = AssetIdOf(item.Icon);
                _icon = item.Texture ?? (asset != 0 ? AsyncTexture2D.FromAssetId(asset) : null);
                BasicTooltipText = item.Action == BarItem.DepositMaterials
                    ? "Click to choose Deposit All Materials: opens the inventory, presses its Deposit All Materials button, and closes it again."
                    : $"Click to choose {item.Name}.";
            }

            private static int AssetIdOf(string url)
            {
                if (string.IsNullOrEmpty(url)) return 0;
                string file = System.IO.Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
                return int.TryParse(file, out int id) ? id : 0;
            }

            protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
            {
                var pixel = ContentService.Textures.Pixel;
                spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(0, 0, Width, Height), Color.Black * (MouseOver ? 0.5f : 0.25f));
                if (_icon != null) spriteBatch.DrawOnCtrl(this, _icon, new Rectangle(3, 3, 32, 32));

                var color = BarControl.RarityColor(_item.Rarity, Color.White);
                spriteBatch.DrawStringOnCtrl(this, _item.Name, GameService.Content.DefaultFont14,
                    new Rectangle(44, 0, Width - 200, Height), color, false, true, 1, HorizontalAlignment.Left, VerticalAlignment.Middle);
                string where = _item.Count > 0 ? $"{_item.Count} · {_item.Where}" : _item.Where;
                spriteBatch.DrawStringOnCtrl(this, where, GameService.Content.DefaultFont12,
                    new Rectangle(Width - 160, 0, 152, Height), Color.LightGray, false, HorizontalAlignment.Right, VerticalAlignment.Middle);
            }
        }
    }
}
