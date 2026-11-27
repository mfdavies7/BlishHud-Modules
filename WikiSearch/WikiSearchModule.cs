using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Controls.Extern;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Gw2Sharp.WebApi;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Bytex.ModuleShared;
using static Bytex.ModuleShared.ChatInput;

namespace Bytex.WikiSearch
{
    public enum WikiLanguage { GameLanguage, English, German, French, Spanish }

    /// <summary>What clicking the corner icon does.</summary>
    public enum IconAction
    {
        [Description("Open Settings")] OpenSettings,
        [Description("Toggle Wiki Search")] ToggleWikiSearch,
        [Description("Open Wiki")] OpenWiki,
        [Description("Nothing")] Nothing,
    }

    [Export(typeof(Module))]
    public class WikiSearchModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<WikiSearchModule>();
        private const string ModuleName = "Wiki Search";

        private SettingEntry<WikiLanguage> _language;
        private SettingEntry<KeyBinding> _toggleKey;
        private SettingEntry<bool> _enabled;
        private SettingEntry<bool> _turnOffOnRightClick;
        private SettingEntry<bool> _overwriteUnsentText;
        private SettingEntry<bool> _workWithMapOpen;
        private SettingEntry<bool> _returnFocusToGame;
        private SettingEntry<int> _keyDelayMs;
        private SettingEntry<bool> _waitForShiftRelease;
        private SettingEntry<bool> _showHint;
        private SettingEntry<bool> _hideHintOverWindows;
        private SettingsUsedWhen _hintSettings;
        private SettingEntry<bool> _registerCornerIcon;
        private SettingEntry<IconAction> _iconLeftClick;
        private SettingEntry<IconAction> _iconRightClick;

        private HintBox _hint;
        private Texture2D _icon;
        // Drawn at 32 px (the corner icon and the Shift hint), so they get their own 32 px images rather than a scaled 64.
        private Texture2D _icon32;
        private Texture2D _iconHover32;
        private Texture2D _iconDisabled32;
        private Texture2D _iconDisabledHover32;
        private RegisteredCornerIcon _cornerIcon;
        private StandardWindow _settingsWindow;
        private bool _busy;

        [ImportingConstructor]
        public WikiSearchModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { }

        protected override void DefineSettings(SettingCollection settings)
        {
            // Saved under its old key, so the choice made with Show Corner Icon carries over.
            _registerCornerIcon = settings.DefineSetting("ShowCornerIcon", true,
                () => "Register Corner Icon",
                () => "Whether the module should register a corner icon. Off: turn Wiki Search on or off with Toggle Hotkey, and open the settings with Settings on the module's page in Blish HUD's settings.");
            _iconLeftClick = settings.DefineSetting("IconLeftClick", IconAction.OpenSettings,
                () => "Corner Icon Left Click Action",
                () => "Defines the action of the corner icon when left clicked. Open Wiki opens the wiki's main page in Wiki Language.");
            _iconRightClick = settings.DefineSetting("IconRightClick", IconAction.ToggleWikiSearch,
                () => "Corner Icon Right Click Action",
                () => "Defines the action of the corner icon when right clicked. Open Wiki opens the wiki's main page in Wiki Language.");
            _toggleKey = settings.DefineSetting("ToggleKey", new KeyBinding(),
                () => "Toggle Hotkey",
                () => "Turns Wiki Search on or off.");
            _enabled = settings.DefineSetting("Enabled", true,
                () => "Enabled",
                () => "Shift + left-click an item to open its wiki page. Turn off to link items in chat normally. Kept between sessions.");
            _turnOffOnRightClick = settings.DefineSetting("TurnOffOnRightClick", false,
                () => "Turn Off on Right-Click",
                () => "Right-click anywhere in the game to turn Wiki Search off, as TyrianQoL does. Turn it back on with Toggle Hotkey or the corner icon.");
            _language = settings.DefineSetting("Language", WikiLanguage.GameLanguage,
                () => "Wiki Language",
                () => "Which wiki to search. Game Language uses the wiki for your game client's language.");
            _overwriteUnsentText = settings.DefineSetting("OverwriteUnsentText", false,
                () => "Overwrite Unsent Chat Text",
                () => "Also search when chat was already open before the click: if chat still holds text you typed but didn't send, delete it and search the item. The deleted text can't be recovered. Off: with chat already open, shift-click links items into your message as usual.");
            _workWithMapOpen = settings.DefineSetting("WorkWithMapOpen", true,
                () => "Work When the Map Is Open",
                () => "Also search when shift-clicking on the world map (for example waypoints). Off: shift-click links map points as usual.");
            _returnFocusToGame = settings.DefineSetting("ReturnFocusToGame", false,
                () => "Return to the Game After Searching",
                () => "Switch back to Guild Wars 2 after the wiki opens in your browser.");
            _keyDelayMs = settings.DefineSlider("KeyDelayMs", 60,
                () => "Key Press Delay (ms)",
                () => "Time between simulated key presses. Raise it if the /wiki command is sometimes typed wrong or not sent. Below one game frame (about 16 ms at 60 FPS) keys may be missed.",
                20, 250);
            _waitForShiftRelease = settings.DefineSetting("WaitForShiftRelease", false,
                () => "Wait for Shift Release Before Sending",
                () => "Only type the /wiki command after you let go of Shift. Turn on if the command is sometimes missing or the item link gets replaced.");
            _showHint = settings.DefineSetting("ShowHint", true,
                () => "Show Hint While Holding Shift",
                () => "Show a hint next to the cursor while Shift is held and a click would open the wiki.");
            _hideHintOverWindows = settings.DefineSetting("HideHintOverWindows", true,
                () => "Hide Hint Over Blish HUD Windows",
                () => "Hide the hint while the cursor is over a Blish HUD window, menu or other module's panel. It always hides over corner icons.");
        }

        // Blish HUD's settings page only opens the module's own settings window.
        public override IView GetSettingsView() => new OpenSettingsView(OpenSettingsWindow);

        private IView SettingsView() => new SettingsWithValuesView(ModuleParameters.SettingsManager.ModuleSettings);

        private void OpenSettingsWindow()
        {
            if (_settingsWindow == null) return; // not loaded yet
            if (!_settingsWindow.Visible) _settingsWindow.Show(SettingsView());
            _settingsWindow.BringWindowToFront();
        }

        // Loads the icon's texture, so it's done here rather than in Initialize, as Blish HUD's module guide asks.
        protected override Task LoadAsync()
        {
            ModuleLifetime.Begin();
            _toggleKey.Value.Enabled = true;
            _toggleKey.Value.Activated += ToggleKey_Activated;
            GameService.Input.Mouse.LeftMouseButtonPressed += Mouse_LeftMouseButtonPressed;
            GameService.Input.Mouse.RightMouseButtonReleased += Mouse_RightMouseButtonReleased;

            _icon = ModuleParameters.ContentsManager.GetTexture(@"textures\WikiSearch.png");
            _icon32 = ModuleParameters.ContentsManager.GetTexture(@"textures\WikiSearch-32.png");
            _iconHover32 = ModuleParameters.ContentsManager.GetTexture(@"textures\WikiSearchHover-32.png");
            _hint = new HintBox(_icon32, "Wiki Search", GetHintText())
            {
                Parent = GameService.Graphics.SpriteScreen,
                ZIndex = int.MaxValue,
                Visible = false,
            };
            _hintSettings = new SettingsUsedWhen(() => _showHint.Value, _hideHintOverWindows).On(_showHint);
            _waitForShiftRelease.SettingChanged += WaitForShiftRelease_SettingChanged;

            // Greyed out while Wiki Search is off, so the corner icon shows whether it's on.
            _iconDisabled32 = ModuleParameters.ContentsManager.GetTexture(@"textures\WikiSearchDisabled-32.png");
            _iconDisabledHover32 = ModuleParameters.ContentsManager.GetTexture(@"textures\WikiSearchDisabledHover-32.png");
            _cornerIcon = new RegisteredCornerIcon(ModuleParameters.Manifest, _registerCornerIcon, CornerIconTexture(), CornerIconHoverTexture(),
                () => RunIconAction(_iconLeftClick.Value), () => RunIconAction(_iconRightClick.Value),
                rightClickOnRelease: false, _iconLeftClick, _iconRightClick);
            _cornerIcon.ShowKeybind(_toggleKey);
            _enabled.SettingChanged += Enabled_SettingChanged;
            // The same window as the other modules' settings, so they look alike.
            _settingsWindow = new StandardWindow(AsyncTexture2D.FromAssetId(155997), new Rectangle(25, 26, 560, 640), new Rectangle(40, 50, 540, 590))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Wiki Search",
                Subtitle = "Settings",
                Emblem = _icon,
                Id = "bytex.wikisearch.settings",
                SavesPosition = true,
            };
            return Task.CompletedTask;
        }

        protected override void Update(GameTime gameTime)
        {
            bool show = _showHint.Value
                        && !_busy
                        && IsShiftDown()
                        && CanSearchNow()
                        && !HintBox.HideForCursor(_hideHintOverWindows.Value);

            if (show) _hint.PlaceNearCursor();
            _hint.Visible = show;
        }

        protected override void Unload()
        {
            ModuleLifetime.End(); // stops any key sequence or download still running
            _toggleKey.Value.Enabled = false;
            _toggleKey.Value.Activated -= ToggleKey_Activated;
            GameService.Input.Mouse.LeftMouseButtonPressed -= Mouse_LeftMouseButtonPressed;
            GameService.Input.Mouse.RightMouseButtonReleased -= Mouse_RightMouseButtonReleased;
            _waitForShiftRelease.SettingChanged -= WaitForShiftRelease_SettingChanged;
            _enabled.SettingChanged -= Enabled_SettingChanged;
            _hintSettings?.Dispose();
            _hintSettings = null;
            _hint?.Dispose();
            _hint = null;
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
        }

        private Texture2D CornerIconTexture() => _enabled.Value ? _icon32 : _iconDisabled32;
        private Texture2D CornerIconHoverTexture() => _enabled.Value ? _iconHover32 : _iconDisabledHover32;

        private void Enabled_SettingChanged(object sender, ValueChangedEventArgs<bool> e)
        {
            _cornerIcon.SetTextures(CornerIconTexture(), CornerIconHoverTexture());
        }

        private void RunIconAction(IconAction action)
        {
            switch (action)
            {
                case IconAction.OpenSettings:
                    if (_settingsWindow.Visible) _settingsWindow.Hide();
                    else OpenSettingsWindow();
                    break;
                case IconAction.ToggleWikiSearch:
                    ToggleEnabled();
                    break;
                case IconAction.OpenWiki:
                    OpenWikiMainPage();
                    break;
            }
        }

        private void OpenWikiMainPage()
        {
            try
            {
                Process.Start($"https://{GetWikiHost()}/");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Couldn't open the wiki.");
                Notify.Warning(ModuleName, "Couldn't open the wiki in your browser.");
            }
        }

        /// <summary>The wiki for Wiki Language; Game Language uses Blish HUD's language, the closest to the game's it knows.</summary>
        private string GetWikiHost()
        {
            var language = _language.Value;
            if (language == WikiLanguage.GameLanguage)
            {
                language = GameService.Overlay.UserLocale.Value switch
                {
                    Locale.German => WikiLanguage.German,
                    Locale.French => WikiLanguage.French,
                    Locale.Spanish => WikiLanguage.Spanish,
                    _ => WikiLanguage.English,
                };
            }
            return language switch
            {
                WikiLanguage.German => "wiki-de.guildwars2.com",
                WikiLanguage.French => "wiki-fr.guildwars2.com",
                WikiLanguage.Spanish => "wiki-es.guildwars2.com",
                _ => "wiki.guildwars2.com",
            };
        }

        private void WaitForShiftRelease_SettingChanged(object sender, ValueChangedEventArgs<bool> e)
        {
            _hint.Description = GetHintText();
        }

        private string GetHintText() => _waitForShiftRelease.Value
            ? "Click an item to open its wiki page.\nRelease Shift after clicking."
            : "Click an item to open its wiki page.";

        private void ToggleKey_Activated(object sender, EventArgs e) => ToggleEnabled();

        private void ToggleEnabled()
        {
            _enabled.Value = !_enabled.Value;
            Notify.Show(ModuleName, _enabled.Value ? "On" : "Off");
        }

        // On release: Blish HUD takes a right press over the game for camera dragging and only raises the release.
        // Not over Blish HUD's own controls (ActiveControl), so right-clicking the corner icon runs its action instead.
        private void Mouse_RightMouseButtonReleased(object sender, MouseEventArgs e)
        {
            if (!_enabled.Value || !_turnOffOnRightClick.Value || _busy || GameService.Input.Mouse.ActiveControl != null
                || !GameService.GameIntegration.Gw2Instance.Gw2HasFocus) return;
            _enabled.Value = false;
            Notify.Show(ModuleName, "Off");
        }

        private void Mouse_LeftMouseButtonPressed(object sender, MouseEventArgs e)
        {
            if (_busy || !IsShiftDown() || !CanSearchNow()) return;

            // Set here, not in the task, so a second click can't start another search before it runs.
            _busy = true;
            bool chatWasOpen = GameService.Gw2Mumble.UI.IsTextInputFocused;
            // Off Blish HUD's input thread: awaits would resume there, and if Blish HUD restarts that thread
            // (e.g. when the browser takes focus) the search never finishes and _busy stays set until reload.
            _ = Task.Run(() => RunWikiSearchAsync(chatWasOpen));
        }

        /// <summary>Shared by the click trigger and the hint, so the hint only shows when a click would search.</summary>
        private bool CanSearchNow()
        {
            var ui = GameService.Gw2Mumble.UI;
            var gw2 = GameService.GameIntegration.Gw2Instance;
            return _enabled.Value
                   && !ShiftClickClaim.TakenByOther("Wiki Search") // Copy Item Name or Item Destruction is waiting for the click
                   && gw2.IsInGame // not on character select or a loading screen
                   && gw2.Gw2HasFocus
                   && (_workWithMapOpen.Value || !ui.IsMapOpen)
                   && (_overwriteUnsentText.Value || !ui.IsTextInputFocused);
        }

        private async Task RunWikiSearchAsync(bool chatWasOpen)
        {
            // The /wiki command is typed through the clipboard, so it's always put back afterwards.
            string previousClipboard = null;
            try
            {
                int delay = _keyDelayMs.Value;

                // Let GW2 open chat and put the item link in it. No chat means the click wasn't on an item.
                await Task.Delay(50);
                if (!await WaitAsync(() => GameService.Gw2Mumble.UI.IsTextInputFocused, 1000)) return;
                if (_waitForShiftRelease.Value && !await WaitAsync(() => !IsShiftDown(), 5000)) return;
                await Task.Delay(delay);

                previousClipboard = await ClipboardUtil.WindowsClipboardService.GetTextAsync() ?? "";

                if (chatWasOpen)
                {
                    var (link, _) = await ClickedLink.TakeAsync(delay);
                    if (link == null) return; // the click didn't add a link, so it wasn't on an item

                    // All the text is selected, so the paste replaces it, including any unsent text.
                    await SetClipboardAsync(GetWikiCommand() + link);
                    await CtrlStrokeAsync(VirtualKeyShort.KEY_V, delay);
                }
                else
                {
                    await SetClipboardAsync(GetWikiCommand());
                    Stroke(VirtualKeyShort.HOME);
                    await Task.Delay(delay);
                    await CtrlStrokeAsync(VirtualKeyShort.KEY_V, delay);
                }
                Stroke(VirtualKeyShort.RETURN);

                // Give GW2 time to read the command and open the browser before the clipboard changes back.
                await Task.Delay(300);
                await RestoreClipboardAsync(previousClipboard);
                previousClipboard = null;

                if (_returnFocusToGame.Value)
                {
                    await Task.Delay(500);
                    if (!ModuleLifetime.Ended) GameService.GameIntegration.Gw2Instance.FocusGw2();
                }
            }
            catch (AbortException ex)
            {
                Notify.Warning(ModuleName, ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Wiki Search failed.");
                Notify.Warning(ModuleName, "Failed. See the Blish HUD log for details.");
            }
            finally
            {
                if (previousClipboard != null) await RestoreClipboardAsync(previousClipboard);
                _busy = false;
            }
        }

        private static async Task RestoreClipboardAsync(string text)
        {
            try
            {
                await ClipboardUtil.WindowsClipboardService.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Couldn't restore the clipboard.");
            }
        }

        private static bool IsShiftDown()
        {
            var keys = GameService.Input.Keyboard.KeysDown;
            return keys.Contains(Keys.LeftShift) || keys.Contains(Keys.RightShift);
        }

        private string GetWikiCommand() => _language.Value switch
        {
            WikiLanguage.English => "/wiki en: ",
            WikiLanguage.German => "/wiki de: ",
            WikiLanguage.French => "/wiki fr: ",
            WikiLanguage.Spanish => "/wiki es: ",
            _ => "/wiki ",
        };
    }
}
