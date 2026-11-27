using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
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
using Bytex.ModuleShared;
using Gw2Sharp.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Bytex.QuickItem
{
    /// <summary>What clicking the corner icon does.</summary>
    public enum IconAction
    {
        [Description("Open Settings")] OpenSettings,
        [Description("Edit Bars")] EditBars,
        [Description("Show or Hide Bars")] ShowOrHideBars,
        [Description("Choose Layout")] ChooseLayout,
        [Description("Nothing")] Nothing,
    }

    /// <summary>How an item on a bar is used.</summary>
    public enum UseClick
    {
        [Description("Click")] Click,
        [Description("Double-Click")] DoubleClick,
    }

    [Export(typeof(Module))]
    public class QuickItemModule : Module, IBarHost, ILayoutHost
    {
        private static readonly Logger Logger = Logger.GetLogger<QuickItemModule>();

        // Structured PvP (the Heart of the Mists, arenas, tournaments) and World vs. World (the borderlands, Eternal
        // Battlegrounds, Edge of the Mists, Armistice Bastion, Obsidian Sanctum). Quick Item does nothing on these maps.
        private static readonly HashSet<MapType> PvpMaps = new HashSet<MapType>
        {
            MapType.Pvp, MapType.Gvg, MapType.Tournament, MapType.UserTournament,
            MapType.Center, MapType.EternalBattlegrounds, MapType.BlueHome, MapType.BlueBorderlands,
            MapType.GreenHome, MapType.GreenBorderlands, MapType.RedHome, MapType.RedBorderlands,
            MapType.EdgeOfTheMists, MapType.WvwLounge, MapType.ObsidianSanctum,
        };

        private SettingEntry<bool> _registerCornerIcon;
        private SettingEntry<IconAction> _iconLeftClick;
        private SettingEntry<IconAction> _iconRightClick;
        private SettingEntry<bool> _showBars;
        private SettingEntry<KeyBinding> _showBarsKey;
        private SettingEntry<bool> _editBars;
        private SettingEntry<bool> _showInCombat;
        private SettingEntry<bool> _showWhileMapOpen;
        private SettingEntry<UseClick> _useWith;
        private SettingEntry<bool> _showHotkeys;
        private SettingEntry<KeyBinding> _nextLayoutKey;
        private SettingEntry<KeyBinding> _depositKey;
        private SettingEntry<KeyBinding> _inventoryKey;
        private SettingEntry<bool> _closeInventory;
        private SettingEntry<int> _waitForInventoryMs;
        private SettingEntry<int> _matchTolerance;
        private SettingEntry<int> _keyDelayMs;
        private SettingEntry<bool> _saveScreenshots;
        private SettingEntry<bool> _scrollInventory;
        private SettingEntry<int> _scrollNotches;

        private readonly CancellationTokenSource _unloading = new CancellationTokenSource();
        private string _screenshotDirectory;
        private BarStore _store;
        private IconCache _iconCache;
        private ItemUser _user;
        private ItemPicker _picker;
        private readonly Dictionary<Guid, BarControl> _barControls = new Dictionary<Guid, BarControl>();
        private readonly Dictionary<int, AsyncTexture2D> _iconTextures = new Dictionary<int, AsyncTexture2D>();
        // Item and layout hotkeys, and what each does.
        private readonly List<(KeyBinding binding, Action action)> _hotkeys = new List<(KeyBinding, Action)>();
        private volatile bool _hiddenForUse;

        // The layout shown, the character played and whether it's PvP, as of the last frame: a change to any of them
        // changes which bars and hotkeys are in use.
        private Layout _layout;
        private string _character;
        private volatile bool _inPvp;
        // A layout chosen by hand while a character's or profession's layout applied; it lasts until the character changes.
        private Guid? _chosenOverRule;
        // Set while renaming a layout, so the layout list (where the name is being typed) isn't rebuilt.
        private bool _renaming;

        private Texture2D _icon;
        private Texture2D _icon32;
        private Texture2D _iconHover32;
        private Texture2D _iconDisabled32;
        private Texture2D _iconDisabledHover32;
        private Texture2D _depositTexture;
        private AsyncTexture2D _depositIcon;
        private RegisteredCornerIcon _cornerIcon;
        private ContextMenuStrip _layoutMenu;
        private StandardWindow _settingsWindow;
        private SettingsUsedWhen _barsShownSettings;
        private SettingsUsedWhen _scrollSettings;

        public event Action LayoutsChanged;

        [ImportingConstructor]
        public QuickItemModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters)
        {
            Blocked.ModuleName = "Quick Item";
        }

        protected override void DefineSettings(SettingCollection settings)
        {
            _registerCornerIcon = settings.DefineSetting("RegisterCornerIcon", true,
                () => "Register Corner Icon",
                () => "Whether the module should register a corner icon. Off: edit the bars with Edit Bars, and open the settings with Settings on the module's page in Blish HUD's settings.");
            _iconLeftClick = settings.DefineSetting("IconLeftClick", IconAction.OpenSettings,
                () => "Corner Icon Left Click Action",
                () => "Defines the action of the corner icon when left clicked.");
            _iconRightClick = settings.DefineSetting("IconRightClick", IconAction.EditBars,
                () => "Corner Icon Right Click Action",
                () => "Defines the action of the corner icon when right clicked. Edit Bars turns editing on or off; Choose Layout lists the layouts to switch to.");

            _showBars = settings.DefineSetting("ShowBars", true,
                () => "Show Bars",
                () => "Show the bars of items on screen. Item hotkeys work either way.");
            _showBarsKey = settings.DefineSetting("ShowBarsKey", new KeyBinding(),
                () => "Show or Hide Bars Hotkey",
                () => "Turns Show Bars on or off.");
            _editBars = settings.DefineSetting("EditBars", false,
                () => "Edit Bars",
                () => "Drag a bar's header to move it, drag items to reorder them, click + to add an item, and right-click a bar or item for more. Clicking items doesn't use them while this is on.");
            _showInCombat = settings.DefineSetting("ShowInCombat", true,
                () => "Show in Combat",
                () => "Keep the bars on screen while you're in combat.");
            _showWhileMapOpen = settings.DefineSetting("ShowWhileMapOpen", false,
                () => "Show While the Map Is Open",
                () => "Keep the bars on screen while the world map is open.");
            _useWith = settings.DefineSetting("UseWith", UseClick.DoubleClick,
                () => "Use Items With",
                () => "Whether a click or a double-click on a bar uses the item. Double-Click guards against using something by accident.");
            _showHotkeys = settings.DefineSetting("ShowHotkeys", true,
                () => "Show Hotkeys on Icons",
                () => "Write each item's hotkey in the corner of its icon.");

            _nextLayoutKey = settings.DefineSetting("NextLayoutKey", new KeyBinding(),
                () => "Next Layout Hotkey",
                () => "Switches to the next layout, in the order they're listed below. Kept from the game.");

            _depositKey = settings.DefineSetting("DepositMaterialsKey", new KeyBinding(),
                () => "Deposit All Materials Hotkey",
                () => "Opens the inventory, presses its Deposit All Materials button, and closes it again. Kept from the game. Deposit All Materials can also go on a bar: click + and choose it at the top of the list.");
            _inventoryKey = settings.DefineSetting("InventoryKey", new KeyBinding(Keys.I),
                () => "Inventory Key",
                () => "Your game's Inventory key (Options, Control Options, Inventory), pressed to open and close the inventory.");
            _closeInventory = settings.DefineSetting("CloseInventory", true,
                () => "Close Inventory Afterwards",
                () => "Close the inventory again after using an item. An inventory that was already open stays open.");
            _waitForInventoryMs = settings.DefineSlider("WaitForInventoryMs", 1000,
                () => "Wait for Inventory (ms)",
                () => "The longest to wait for the inventory to open. Quick Item carries on as soon as it's fully drawn; raise this if your inventory sometimes takes longer to appear.",
                200, 3000);
            _matchTolerance = settings.DefineSlider("MatchTolerance", 7,
                () => "Match Tolerance",
                () => "How different from its icon an item on screen may look and still be used. Raise it if items in your inventory aren't found; lower it if the wrong item is used. Save Search Screenshots shows each search's score.",
                1, 30);
            _keyDelayMs = settings.DefineSlider("KeyDelayMs", 60,
                () => "Key Press Delay (ms)",
                () => "Time between simulated key presses and clicks. Raise it if the inventory doesn't open or the item isn't used. Below one game frame (about 16 ms at 60 FPS) input may be missed.",
                20, 250);
            _scrollInventory = settings.DefineSetting("ScrollInventory", true,
                () => "Scroll the Inventory",
                () => "If the item isn't in view, scroll the inventory to look for it, then scroll it back. Quick Item finds the inventory window by the bag at its top left, so keep that on screen.");
            _scrollNotches = settings.DefineSlider("ScrollNotches", 2,
                () => "Scroll Step (Notches)",
                () => "Mouse wheel notches per scroll step. Lower it if items further down are skipped; raise it to scroll through large bags faster.",
                1, 10);
            _saveScreenshots = settings.DefineSetting("SaveScreenshots", false,
                () => "Save Search Screenshots",
                () => "Save a screenshot of each search, with the best match boxed and scored, to see why an item wasn't found. The last 10 are kept. Each use's steps and timings also go in the Blish HUD log.");
        }

        // Blish HUD's settings page only opens the module's own settings window.
        public override IView GetSettingsView() => new OpenSettingsView(OpenSettingsWindow);

        /// <summary>The settings, in sections, for the settings window.</summary>
        private IView SettingsView() => new SettingsWithValuesView(
            new SettingsSection("Corner Icon", new SettingEntry[] { _registerCornerIcon, _iconLeftClick, _iconRightClick }),
            new SettingsSection("Bars", new SettingEntry[] { _showBars, _showBarsKey, _editBars, _showInCombat, _showWhileMapOpen, _useWith, _showHotkeys },
                ("New Bar", "Add an empty bar to this layout, in the middle of the screen, and start editing.", NewBar)),
            new SettingsSection("Layouts", new SettingEntry[] { _nextLayoutKey },
                ("New Layout", "Add an empty layout, switch to it, and start editing its first bar.", NewLayout))
            {
                BuildExtra = panel => LayoutsList.Build(panel, this),
            },
            new SettingsSection("Using Items", new SettingEntry[] { _depositKey, _inventoryKey, _closeInventory, _waitForInventoryMs, _scrollInventory, _scrollNotches, _matchTolerance, _keyDelayMs, _saveScreenshots },
                ("Open Screenshot Folder", "Open the folder Save Search Screenshots saves to.", OpenScreenshotFolder)));

        private void OpenSettingsWindow()
        {
            if (_settingsWindow == null) return; // not loaded yet
            if (!_settingsWindow.Visible) _settingsWindow.Show(SettingsView());
            _settingsWindow.BringWindowToFront();
        }

        /// <summary>A picture from the module's ref folder as pixels, to find on screen.</summary>
        private BgraImage LoadImage(string path)
        {
            using (var stream = ModuleParameters.ContentsManager.GetFileStream(path))
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return ScreenGrab.Decode(copy.ToArray());
            }
        }

        // Textures and saved files are loaded here rather than in Initialize, as Blish HUD's module guide asks.
        protected override Task LoadAsync()
        {
            string directory = ModuleParameters.DirectoriesManager.GetFullDirectoryPath("quickitem");
            _screenshotDirectory = Path.Combine(directory, "screenshots");
            _store = new BarStore(directory);
            _store.Load();
            _store.Changed += Store_Changed;
            _iconCache = new IconCache(directory);
            _user = new ItemUser(_iconCache, new UseOptions
            {
                InventoryKey = () => _inventoryKey.Value,
                CloseInventory = () => _closeInventory.Value,
                WaitForInventoryMs = () => _waitForInventoryMs.Value,
                Tolerance = () => _matchTolerance.Value / 100.0,
                KeyDelayMs = () => _keyDelayMs.Value,
                SaveScreenshots = () => _saveScreenshots.Value,
                ScreenshotDirectory = _screenshotDirectory,
                HideBars = hide => _hiddenForUse = hide,
                BarAt = BarAt,
                ScrollInventory = () => _scrollInventory.Value,
                ScrollNotches = () => _scrollNotches.Value,
                InventoryEmblem = LoadImage(@"textures\InventoryEmblem.png"),
                DepositButton = LoadImage(@"textures\DepositMaterials.png"),
                // Read off the update thread, so straight from Mumble rather than the last frame's value.
                InPvp = InPvp,
            }, _unloading.Token);

            _icon = ModuleParameters.ContentsManager.GetTexture(@"textures\QuickItem.png");
            _icon32 = ModuleParameters.ContentsManager.GetTexture(@"textures\QuickItem-32.png");
            _iconHover32 = ModuleParameters.ContentsManager.GetTexture(@"textures\QuickItemHover-32.png");
            // Greyed out in PvP and WvW, where Quick Item is off, as Wiki Search's is while it's off.
            _iconDisabled32 = ModuleParameters.ContentsManager.GetTexture(@"textures\QuickItemDisabled-32.png");
            _iconDisabledHover32 = ModuleParameters.ContentsManager.GetTexture(@"textures\QuickItemDisabledHover-32.png");
            _depositTexture = ModuleParameters.ContentsManager.GetTexture(@"textures\DepositMaterialsIcon.png");
            _depositIcon = new AsyncTexture2D(_depositTexture);
            _picker = new ItemPicker(ModuleParameters.Gw2ApiManager, _icon, _depositIcon, _unloading.Token);
            _layoutMenu = new ContextMenuStrip(LayoutMenuItems);
            _inPvp = InPvp();
            _cornerIcon = new RegisteredCornerIcon(ModuleParameters.Manifest, _registerCornerIcon, CornerIconTexture(), CornerIconHoverTexture(),
                () => RunIconAction(_iconLeftClick.Value), () => RunIconAction(_iconRightClick.Value),
                rightClickOnRelease: false, _iconLeftClick, _iconRightClick);
            _cornerIcon.ShowKeybind(_showBarsKey);
            // The same window as the other modules' settings, so they look alike.
            _settingsWindow = new StandardWindow(AsyncTexture2D.FromAssetId(155997), new Rectangle(25, 26, 560, 640), new Rectangle(40, 50, 540, 590))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Quick Item",
                Subtitle = "Settings",
                Emblem = _icon,
                Id = "bytex.quickitem.settings",
                SavesPosition = true,
            };
            _barsShownSettings = new SettingsUsedWhen(() => _showBars.Value, _showInCombat, _showWhileMapOpen).On(_showBars);
            _scrollSettings = new SettingsUsedWhen(() => _scrollInventory.Value, _scrollNotches).On(_scrollInventory);

            _showBarsKey.Value.Activated += ShowBarsKey_Activated;
            // Kept from the game, like the item hotkeys, so a key the game also uses does only this.
            _nextLayoutKey.Value.BlockSequenceFromGw2 = true;
            _nextLayoutKey.Value.Activated += NextLayoutKey_Activated;
            _depositKey.Value.BlockSequenceFromGw2 = true;
            _depositKey.Value.Activated += DepositKey_Activated;
            _editBars.SettingChanged += EditBars_SettingChanged;
            GameService.Graphics.SpriteScreen.Resized += SpriteScreen_Resized;

            _character = CurrentCharacter;
            _layout = EffectiveLayout();

            // First run: an empty bar to fill, already in edit mode, so it's clear where to start.
            if (_store.IsNew)
            {
                AddBar(_layout);
                _editBars.Value = true;
                Notify.Show("Quick Item", "Click + on the new bar to add an item.");
            }
            else
            {
                // Editing is for now, not for the next session.
                _editBars.Value = false;
            }
            SyncBars();
            return Task.CompletedTask;
        }

        private void RunIconAction(IconAction action)
        {
            if (action == IconAction.Nothing) return;
            if (action == IconAction.OpenSettings)
            {
                if (_settingsWindow.Visible) _settingsWindow.Hide();
                else OpenSettingsWindow();
                return;
            }
            if (_inPvp)
            {
                Notify.Show("Quick Item", "Off in PvP and WvW.");
                return;
            }
            switch (action)
            {
                case IconAction.EditBars:
                    SetEditing(!_editBars.Value);
                    break;
                case IconAction.ShowOrHideBars:
                    _showBars.Value = !_showBars.Value;
                    break;
                case IconAction.ChooseLayout:
                    _layoutMenu.Show(GameService.Input.Mouse.Position);
                    break;
            }
        }

        /// <summary>
        /// Whether the player is in structured PvP or World vs. World, where Quick Item hides its bars, lets every key
        /// through to the game, and uses nothing. Mumble's competitive flag covers the matches; the map type covers the
        /// lobbies (the Heart of the Mists, Armistice Bastion) as well.
        /// </summary>
        private static bool InPvp()
        {
            var map = GameService.Gw2Mumble.CurrentMap;
            return map.IsCompetitiveMode || PvpMaps.Contains(map.Type);
        }

        protected override void Update(GameTime gameTime)
        {
            var gw2 = GameService.GameIntegration.Gw2Instance;
            var mumble = GameService.Gw2Mumble;

            // Which bars and hotkeys are in use depends on the character, the layout, and PvP.
            bool changed = false;
            string character = CurrentCharacter;
            if (character != _character)
            {
                _character = character;
                _chosenOverRule = null; // a layout chosen by hand was for that character
                changed = true;
            }
            bool pvp = InPvp();
            if (pvp != _inPvp)
            {
                _inPvp = pvp;
                _cornerIcon.SetTextures(CornerIconTexture(), CornerIconHoverTexture());
                if (pvp)
                {
                    _picker?.Hide();
                    _layoutMenu?.Hide();
                    Logger.Info("In PvP or WvW: bars hidden and hotkeys off.");
                }
                changed = true;
            }
            var layout = EffectiveLayout();
            if (layout != _layout)
            {
                _layout = layout;
                changed = true;
            }
            if (changed)
            {
                RebuildHotkeys();
                foreach (var control in _barControls.Values) control.Layout();
                LayoutsChanged?.Invoke();
            }

            bool editing = _editBars.Value;
            bool show = !_hiddenForUse && !pvp && gw2.IsInGame
                        && (editing || _showBars.Value
                            && (_showInCombat.Value || !mumble.PlayerCharacter.IsInCombat)
                            && (_showWhileMapOpen.Value || !mumble.UI.IsMapOpen));

            foreach (var control in _barControls.Values)
            {
                var bar = control.Bar;
                control.Visible = show && Applies(bar) && (bar.Items.Count > 0 || editing);
                if (editing) control.UpdateDrag();
            }
        }

        private Texture2D CornerIconTexture() => _inPvp ? _iconDisabled32 : _icon32;
        private Texture2D CornerIconHoverTexture() => _inPvp ? _iconDisabledHover32 : _iconHover32;

        /// <summary>Whether a bar is in use now: in the layout shown, and on this character.</summary>
        private bool Applies(Bar bar) =>
            BarStore.InLayout(bar, _layout) && (bar.Character == null || bar.Character == _character);

        protected override void Unload()
        {
            _unloading.Cancel();
            foreach (var key in new[] { _showBarsKey, _nextLayoutKey, _depositKey }) key.Value.Enabled = false;
            _showBarsKey.Value.Activated -= ShowBarsKey_Activated;
            _nextLayoutKey.Value.Activated -= NextLayoutKey_Activated;
            _depositKey.Value.Activated -= DepositKey_Activated;
            _editBars.SettingChanged -= EditBars_SettingChanged;
            GameService.Graphics.SpriteScreen.Resized -= SpriteScreen_Resized;
            if (_store != null) _store.Changed -= Store_Changed;
            ClearHotkeys();
            foreach (var control in _barControls.Values) control.Dispose();
            _barControls.Clear();
            // Blish HUD's asset cache owns these, and may share them with other modules.
            _iconTextures.Clear();
            _barsShownSettings?.Dispose();
            _barsShownSettings = null;
            _scrollSettings?.Dispose();
            _scrollSettings = null;
            _picker?.Dispose();
            _picker = null;
            _iconCache?.Dispose();
            _iconCache = null;
            _layoutMenu?.Dispose();
            _layoutMenu = null;
            _cornerIcon?.Dispose();
            _cornerIcon = null;
            _settingsWindow?.Dispose();
            _settingsWindow = null;
            _icon?.Dispose();
            _icon = null;
            _icon32?.Dispose();
            _iconHover32?.Dispose();
            _icon32 = null;
            _iconHover32 = null;
            _iconDisabled32?.Dispose();
            _iconDisabledHover32?.Dispose();
            _iconDisabled32 = null;
            _iconDisabledHover32 = null;
            _depositTexture?.Dispose();
            _depositTexture = null;
            _depositIcon = null;
        }

        /// <summary>
        /// Whether a visible bar covers a screen position (with a few pixels to spare). Blish HUD's UI units start at
        /// the game's client area and scale with the interface size.
        /// </summary>
        private bool BarAt(int x, int y)
        {
            var window = GameService.GameIntegration.Gw2Instance.Gw2WindowHandle;
            if (!ScreenGrab.TryGetClientArea(window, out var area)) return true; // can't tell; hide them to be safe
            float scale = GameService.Graphics.UIScaleMultiplier;
            var point = new Point((int)Math.Round((x - area.X) / scale), (int)Math.Round((y - area.Y) / scale));
            return _barControls.Values.ToList().Any(c =>
            {
                var bounds = c.AbsoluteBounds;
                bounds.Inflate(4, 4);
                return c.Visible && bounds.Contains(point);
            });
        }

        /// <summary>Whether a hotkey may act now: in game, the game focused, not typing, and not in PvP.</summary>
        private bool HotkeysAllowed()
        {
            var gw2 = GameService.GameIntegration.Gw2Instance;
            return !_inPvp && gw2.IsInGame && gw2.Gw2HasFocus && !GameService.Gw2Mumble.UI.IsTextInputFocused;
        }

        private void ShowBarsKey_Activated(object sender, EventArgs e)
        {
            if (!_inPvp) _showBars.Value = !_showBars.Value;
        }

        private void NextLayoutKey_Activated(object sender, EventArgs e)
        {
            if (!HotkeysAllowed() || _store.Layouts.Count < 2) return;
            int index = _store.Layouts.IndexOf(_layout);
            SwitchLayout(_store.Layouts[(index + 1) % _store.Layouts.Count], announce: true);
        }

        private void DepositKey_Activated(object sender, EventArgs e)
        {
            if (HotkeysAllowed()) _user.Use(new BarItem { Name = BarItem.DepositMaterialsName, Action = BarItem.DepositMaterials }, fromHotkey: true);
        }

        private void EditBars_SettingChanged(object sender, ValueChangedEventArgs<bool> e)
        {
            // The + slot and the header come and go.
            foreach (var control in _barControls.Values) control.Layout();
            if (!e.NewValue) _picker?.Hide();
        }

        private void SpriteScreen_Resized(object sender, ResizedEventArgs e)
        {
            foreach (var control in _barControls.Values) control.Layout();
        }

        // On the next frame: a bar's own menu can delete it, and the control mustn't be disposed mid-click.
        private void Store_Changed()
        {
            bool renaming = _renaming;
            _renaming = false;
            GameService.Overlay.QueueMainThreadUpdate(_ =>
            {
                if (_unloading.IsCancellationRequested) return;
                SyncBars();
                if (!renaming) LayoutsChanged?.Invoke();
            });
        }

        /// <summary>Makes the controls and hotkeys match the saved bars.</summary>
        private void SyncBars()
        {
            var ids = new HashSet<Guid>(_store.Bars.Select(b => b.Id));
            foreach (var gone in _barControls.Where(c => !ids.Contains(c.Key)).ToList())
            {
                gone.Value.Dispose();
                _barControls.Remove(gone.Key);
            }
            foreach (var bar in _store.Bars)
            {
                if (!_barControls.TryGetValue(bar.Id, out var control))
                    _barControls[bar.Id] = control = new BarControl(bar, this) { Parent = GameService.Graphics.SpriteScreen, Visible = false };
                control.Layout();
            }
            // A layout may have been deleted or switched to.
            _layout = EffectiveLayout();
            RebuildHotkeys();
        }

        /// <summary>
        /// The hotkeys in use now: those of the items on the bars in use (so the same key can use different items in
        /// different layouts), and the layouts'. In PvP and WvW none are, and every key goes to the game.
        /// </summary>
        private void RebuildHotkeys()
        {
            ClearHotkeys();
            bool on = !_inPvp;
            _showBarsKey.Value.Enabled = on;
            _nextLayoutKey.Value.Enabled = on;
            _depositKey.Value.Enabled = on;
            if (!on) return;

            foreach (var item in _store.Bars.Where(Applies).SelectMany(b => b.Items).Where(i => i.Key != 0))
                AddHotkey(item.Modifiers, item.Key, () => _user.Use(item, fromHotkey: true));
            foreach (var layout in _store.Layouts.Where(l => l.Key != 0))
                AddHotkey(layout.Modifiers, layout.Key, () => SwitchLayout(layout, announce: true));
        }

        private void AddHotkey(int modifiers, int key, Action action)
        {
            // Blocked from the game, so a key the game also uses (a skill key, say) does only this.
            var binding = new KeyBinding((ModifierKeys)modifiers, (Keys)key) { Enabled = true, BlockSequenceFromGw2 = true };
            binding.Activated += Hotkey_Activated;
            _hotkeys.Add((binding, action));
        }

        private void ClearHotkeys()
        {
            foreach (var (binding, _) in _hotkeys)
            {
                binding.Enabled = false;
                binding.Activated -= Hotkey_Activated;
            }
            _hotkeys.Clear();
        }

        private void Hotkey_Activated(object sender, EventArgs e)
        {
            if (!HotkeysAllowed()) return;
            _hotkeys.FirstOrDefault(h => h.binding == sender).action?.Invoke();
        }

        private void NewBar()
        {
            AddBar(_layout);
            SetEditing(true);
        }

        private void AddBar(Layout layout)
        {
            var screen = GameService.Graphics.SpriteScreen.Size;
            // Each new bar a little lower than the last, so they don't stack exactly.
            int offset = _store.Bars.Count(b => BarStore.InLayout(b, layout)) * 30 % 300;
            _store.Bars.Add(new Bar { Layout = layout?.Id, X = Math.Max(0, screen.X / 2 - 100), Y = Math.Max(30, screen.Y / 2 + offset) });
            _store.Save();
        }

        private void OpenScreenshotFolder()
        {
            try
            {
                Directory.CreateDirectory(_screenshotDirectory);
                Process.Start(new ProcessStartInfo(_screenshotDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Couldn't open the screenshot folder.");
                Notify.Warning("Quick Item", "Couldn't open the screenshot folder.");
            }
        }

        // ---- Layouts ----

        /// <summary>The layout set for this character, else for its profession; null if neither has one.</summary>
        private Layout RuleLayout()
        {
            string character = CurrentCharacter;
            if (string.IsNullOrEmpty(character)) return null;
            string profession = CurrentProfession;
            return _store.Layouts.FirstOrDefault(l => l.Characters.Contains(character))
                   ?? (profession == null ? null : _store.Layouts.FirstOrDefault(l => l.Professions.Contains(profession)));
        }

        /// <summary>The layout to show: one chosen by hand over a rule, else the rule's, else the one chosen last.</summary>
        private Layout EffectiveLayout() =>
            (_chosenOverRule is Guid chosen ? _store.LayoutById(chosen) : null)
            ?? RuleLayout()
            ?? _store.LayoutById(_store.ChosenLayout)
            ?? _store.Layouts[0];

        private IEnumerable<ContextMenuStripItem> LayoutMenuItems()
        {
            foreach (var layout in _store.Layouts)
            {
                var entry = new ContextMenuStripItem(layout.Name)
                {
                    CanCheck = true,
                    Checked = layout == _layout,
                    BasicTooltipText = layout == _layout ? "The layout in use." : $"Switch to {layout.Name}.",
                };
                entry.Click += (_, __) => SwitchLayout(layout, announce: false);
                yield return entry;
            }
            var manage = new ContextMenuStripItem("Manage Layouts") { BasicTooltipText = "Open the settings to add, rename and set up layouts." };
            manage.Click += (_, __) => OpenSettingsWindow();
            yield return manage;
        }

        /// <param name="announce">Say so on screen; for hotkeys, where nothing else shows the switch happened.</param>
        private void SwitchLayout(Layout layout, bool announce)
        {
            if (!_store.Layouts.Contains(layout)) return;
            _store.ChosenLayout = layout.Id;
            // Over a character's or profession's layout until the character changes.
            _chosenOverRule = layout.Id;
            _store.Save();
            if (announce) Notify.Show("Quick Item", $"Switched to the {layout.Name} layout.");
        }

        private string UniqueLayoutName(string wanted)
        {
            string name = wanted;
            for (int n = 2; _store.Layouts.Any(l => string.Equals(l.Name, name, StringComparison.CurrentCultureIgnoreCase)); n++)
                name = $"{wanted} {n}";
            return name;
        }

        private void NewLayout()
        {
            var layout = new Layout { Name = UniqueLayoutName($"Layout {_store.Layouts.Count + 1}") };
            _store.Layouts.Add(layout);
            _store.ChosenLayout = layout.Id;
            _chosenOverRule = layout.Id;
            _layout = layout;
            AddBar(layout); // saves
            SetEditing(true);
        }

        // ---- ILayoutHost ----

        IReadOnlyList<Layout> ILayoutHost.Layouts => _store.Layouts;

        public string CurrentProfession
        {
            get
            {
                var profession = GameService.Gw2Mumble.PlayerCharacter.Profession;
                return Enum.IsDefined(typeof(ProfessionType), profession) ? profession.ToString() : null;
            }
        }

        public int BarCount(Layout layout) => _store.Bars.Count(b => b.Layout == layout.Id);

        void ILayoutHost.SwitchLayout(Layout layout) => SwitchLayout(layout, announce: false);

        public void RenameLayout(Layout layout, string name)
        {
            layout.Name = name;
            _renaming = true;
            _store.Save();
        }

        public void SetLayoutHotkey(Layout layout)
        {
            var window = new KeybindingAssignmentWindow(layout.Name, (ModifierKeys)layout.Modifiers, (Keys)layout.Key)
            {
                Parent = GameService.Graphics.SpriteScreen,
            };
            window.AssignmentAccepted += (_, __) =>
            {
                layout.Key = (int)window.PrimaryKey;
                layout.Modifiers = (int)window.ModifierKeys;
                _store.Save();
            };
            window.Show();
        }

        public void ClearLayoutHotkey(Layout layout)
        {
            layout.Key = 0;
            layout.Modifiers = 0;
            _store.Save();
        }

        public void SetCharacterRule(Layout layout, bool on)
        {
            string character = CurrentCharacter;
            if (string.IsNullOrEmpty(character)) return;
            // A character has one layout.
            foreach (var other in _store.Layouts) other.Characters.Remove(character);
            if (on) layout.Characters.Add(character);
            _chosenOverRule = null; // the new rule takes effect now
            _store.Save();
        }

        public void SetProfessionRule(Layout layout, bool on)
        {
            string profession = CurrentProfession;
            if (profession == null) return;
            foreach (var other in _store.Layouts) other.Professions.Remove(profession);
            if (on) layout.Professions.Add(profession);
            _chosenOverRule = null;
            _store.Save();
        }

        public void DuplicateLayout(Layout layout)
        {
            var copy = new Layout { Name = UniqueLayoutName($"{layout.Name} Copy"), Key = 0 };
            _store.Layouts.Insert(_store.Layouts.IndexOf(layout) + 1, copy);
            foreach (var bar in _store.Bars.Where(b => b.Layout == layout.Id).ToList())
            {
                var barCopy = bar.Copy();
                barCopy.Layout = copy.Id;
                _store.Bars.Add(barCopy);
            }
            SwitchLayout(copy, announce: false); // saves
        }

        public void DeleteLayout(Layout layout)
        {
            if (_store.Layouts.Count <= 1 || !_store.Layouts.Remove(layout)) return;
            _store.Bars.RemoveAll(b => b.Layout == layout.Id);
            if (_store.ChosenLayout == layout.Id) _store.ChosenLayout = _store.Layouts[0].Id;
            if (_chosenOverRule == layout.Id) _chosenOverRule = null;
            _store.Save();
        }

        // ---- IBarHost ----

        public bool Editing => _editBars.Value;
        public bool DoubleClickToUse => _useWith.Value == UseClick.DoubleClick;
        public bool ShowHotkeys => _showHotkeys.Value;
        public BarItem Busy => _user.Current;
        public string CurrentCharacter => GameService.Gw2Mumble.PlayerCharacter.Name;
        public Layout CurrentLayout => _layout ?? EffectiveLayout();
        IReadOnlyList<Layout> IBarHost.Layouts => _store.Layouts;

        /// <summary>The bar's number among the bars in the layout shown.</summary>
        public int NumberOf(Bar bar) => _store.Bars.Where(b => BarStore.InLayout(b, _layout)).ToList().IndexOf(bar) + 1;

        public AsyncTexture2D IconOf(BarItem item)
        {
            if (item.Action == BarItem.DepositMaterials) return _depositIcon;
            int asset = item.AssetId;
            if (asset == 0) return null;
            if (!_iconTextures.TryGetValue(asset, out var texture))
                _iconTextures[asset] = texture = AsyncTexture2D.FromAssetId(asset);
            return texture;
        }

        public void Use(BarItem item) => _user.Use(item, fromHotkey: false);

        public void ChooseItem(Bar bar, BarItem replacing)
        {
            _picker.Open(replacing == null ? "Add Item" : $"Change {replacing.Name}", picked =>
            {
                var item = replacing ?? new BarItem();
                item.ItemId = picked.Id;
                item.Name = picked.Name;
                item.Icon = picked.Action == null ? picked.Icon : null;
                item.Rarity = picked.Rarity;
                item.Action = picked.Action;
                if (replacing == null)
                {
                    if (!_store.Bars.Contains(bar)) return; // deleted meanwhile
                    bar.Items.Add(item);
                }
                _store.Save();
            });
        }

        public void SetHotkey(BarItem item)
        {
            var window = new KeybindingAssignmentWindow(item.Name, (ModifierKeys)item.Modifiers, (Keys)item.Key)
            {
                Parent = GameService.Graphics.SpriteScreen,
            };
            window.AssignmentAccepted += (_, __) =>
            {
                item.Key = (int)window.PrimaryKey;
                item.Modifiers = (int)window.ModifierKeys;
                _store.Save();
            };
            window.Show();
        }

        public void ClearHotkey(BarItem item)
        {
            item.Key = 0;
            item.Modifiers = 0;
            _store.Save();
        }

        public void Save() => _store.Save();

        public void DeleteBar(Bar bar)
        {
            _store.Bars.Remove(bar);
            _store.Save();
        }

        public void SetEditing(bool editing) => _editBars.Value = editing;

        public void SetBarLayout(Bar bar, Layout layout)
        {
            bar.Layout = layout?.Id;
            _store.Save();
        }

        public void CopyBarTo(Bar bar, Layout layout)
        {
            var copy = bar.Copy();
            copy.Layout = layout.Id;
            _store.Bars.Add(copy);
            _store.Save();
            Notify.Show("Quick Item", $"Copied the bar to the {layout.Name} layout.");
        }

        IEnumerable<ContextMenuStripItem> IBarHost.LayoutMenuItems() => LayoutMenuItems();
    }
}
