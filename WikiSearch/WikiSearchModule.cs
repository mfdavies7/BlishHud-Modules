using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Controls.Extern;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ModuleShared;
using Keyboard = Blish_HUD.Controls.Intern.Keyboard;

namespace WikiSearch
{
    public enum WikiLanguage { GameLanguage, English, German, French, Spanish }

    [Export(typeof(Module))]
    public class WikiSearchModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<WikiSearchModule>();

        private const int HintCursorOffset = 25;

        private SettingEntry<WikiLanguage> _language;
        private SettingEntry<KeyBinding> _toggleKey;
        private SettingEntry<bool> _enabled;
        private SettingEntry<bool> _workWithChatOpen;
        private SettingEntry<bool> _workWithMapOpen;
        private SettingEntry<bool> _returnFocusToGame;
        private SettingEntry<bool> _restoreClipboard;
        private SettingEntry<int> _keyDelayMs;
        private SettingEntry<bool> _waitForShiftRelease;
        private SettingEntry<bool> _showHint;

        private HintBox _hint;
        private Texture2D _hintIcon;
        private bool _busy;
        [ImportingConstructor]
        public WikiSearchModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { }

        protected override void DefineSettings(SettingCollection settings)
        {
            _language = settings.DefineSetting("Language", WikiLanguage.GameLanguage,
                () => "Wiki language",
                () => "Which wiki to search. Game Language uses the wiki for your game client's language.");
            _toggleKey = settings.DefineSetting("ToggleKey", new KeyBinding(),
                () => "Toggle hotkey",
                () => "Turns Wiki Search on or off.");
            _enabled = settings.DefineSetting("Enabled", true,
                () => "Enabled",
                () => "Shift + left-click an item to open its wiki page. Turn off to link items in chat normally.");
            _workWithChatOpen = settings.DefineSetting("WorkWithChatOpen", false,
                () => "Work when chat is already open",
                () => "Also search when chat was open before the click. Everything typed in chat is searched, so only use this with an empty chat box. Off: shift-click links items into your message as usual.");
            _workWithMapOpen = settings.DefineSetting("WorkWithMapOpen", true,
                () => "Work when the map is open",
                () => "Also search when shift-clicking on the world map (for example waypoints). Off: shift-click links map points as usual.");
            _returnFocusToGame = settings.DefineSetting("ReturnFocusToGame", false,
                () => "Return to the game after searching",
                () => "Switch back to Guild Wars 2 after the wiki opens in your browser.");
            _restoreClipboard = settings.DefineSetting("RestoreClipboard", true,
                () => "Restore clipboard",
                () => "Put back what you had copied before. The module uses the clipboard to type the /wiki command.");
            _keyDelayMs = settings.DefineSetting("KeyDelayMs", 40,
                () => "Key press delay (ms)",
                () => "Time between simulated key presses. Raise it if the /wiki command is sometimes typed wrong or not sent. Below one game frame (about 16 ms at 60 FPS) keys may be missed.");
            _keyDelayMs.SetRange(1, 250);
            _waitForShiftRelease = settings.DefineSetting("WaitForShiftRelease", false,
                () => "Wait for Shift release before sending",
                () => "Only type the /wiki command after you let go of Shift. Turn on if the command is sometimes missing or the item link gets replaced.");
            _showHint = settings.DefineSetting("ShowHint", true,
                () => "Show hint while holding Shift",
                () => "Show a hint next to the cursor while Shift is held and a click would open the wiki.");
        }

        public override IView GetSettingsView() => new SettingsWithValuesView(ModuleParameters.SettingsManager.ModuleSettings);

        protected override void Initialize()
        {
            _toggleKey.Value.Enabled = true;
            _toggleKey.Value.Activated += ToggleKey_Activated;
            GameService.Input.Mouse.LeftMouseButtonPressed += Mouse_LeftMouseButtonPressed;

            _hintIcon = ModuleParameters.ContentsManager.GetTexture(@"textures\WikiSearch.png");
            _hint = new HintBox(_hintIcon, "Wiki Search", GetHintText())
            {
                Parent = GameService.Graphics.SpriteScreen,
                ZIndex = int.MaxValue,
                Visible = false,
            };
            _waitForShiftRelease.SettingChanged += WaitForShiftRelease_SettingChanged;
        }

        protected override void Update(GameTime gameTime)
        {
            bool show = _showHint.Value
                        && !_busy
                        && IsShiftDown()
                        && CanSearchNow();

            if (show)
            {
                var mouse = GameService.Input.Mouse.Position;
                var screen = GameService.Graphics.SpriteScreen;
                // Flip to the other side of the cursor near the screen's right or bottom edge.
                int x = mouse.X + HintCursorOffset + _hint.Width > screen.Width ? mouse.X - HintCursorOffset - _hint.Width : mouse.X + HintCursorOffset;
                int y = mouse.Y + HintCursorOffset + _hint.Height > screen.Height ? mouse.Y - HintCursorOffset - _hint.Height : mouse.Y + HintCursorOffset;
                _hint.Location = new Point(x, y);
            }

            _hint.Visible = show;
        }

        protected override void Unload()
        {
            _toggleKey.Value.Activated -= ToggleKey_Activated;
            GameService.Input.Mouse.LeftMouseButtonPressed -= Mouse_LeftMouseButtonPressed;
            _waitForShiftRelease.SettingChanged -= WaitForShiftRelease_SettingChanged;
            _hint?.Dispose();
            _hintIcon?.Dispose();
        }

        private void WaitForShiftRelease_SettingChanged(object sender, ValueChangedEventArgs<bool> e)
        {
            _hint.Description = GetHintText();
        }

        private string GetHintText() => _waitForShiftRelease.Value
            ? "Click an item to open its wiki page.\nRelease Shift after clicking."
            : "Click an item to open its wiki page.";

        private void ToggleKey_Activated(object sender, EventArgs e)
        {
            _enabled.Value = !_enabled.Value;
            ScreenNotification.ShowNotification($"Wiki Search {(_enabled.Value ? "enabled" : "disabled")}");
        }

        private void Mouse_LeftMouseButtonPressed(object sender, MouseEventArgs e)
        {
            if (_busy || !IsShiftDown() || !CanSearchNow()) return;

            _ = RunWikiSearchAsync();
        }

        /// <summary>Shared by the click trigger and the hint, so the hint only shows when a click would search.</summary>
        private bool CanSearchNow()
        {
            var ui = GameService.Gw2Mumble.UI;
            var gw2 = GameService.GameIntegration.Gw2Instance;
            return _enabled.Value
                   && gw2.IsInGame // not on character select or a loading screen
                   && gw2.Gw2HasFocus
                   && (_workWithMapOpen.Value || !ui.IsMapOpen)
                   && (_workWithChatOpen.Value || !ui.IsTextInputFocused);
        }

        private async Task RunWikiSearchAsync()
        {
            _busy = true;
            bool ctrlHeld = false;
            try
            {
                int delay = _keyDelayMs.Value;

                // Let GW2 open chat and put the item link in it.
                await Task.Delay(50);
                if (!await WaitForChatAsync()) return;
                if (_waitForShiftRelease.Value && !await WaitForShiftReleaseAsync()) return;
                await Task.Delay(delay);

                string previousClipboard = _restoreClipboard.Value
                    ? await ClipboardUtil.WindowsClipboardService.GetTextAsync()
                    : null;
                if (!await ClipboardUtil.WindowsClipboardService.SetTextAsync(GetWikiCommand()))
                {
                    // Another program may be holding the clipboard open.
                    Logger.Warn("Could not write the /wiki command to the clipboard.");
                    ScreenNotification.ShowNotification("Wiki Search: couldn't use the clipboard. Try again.", ScreenNotification.NotificationType.Warning);
                    return;
                }

                // The player may still be holding Shift, which would turn Home into "select to start".
                // Shift is lifted right before each key so key repeat has no time to press it again.
                // GW2 reads keys once per frame, so Ctrl must be held for a moment before V counts as Ctrl+V.
                ReleaseShift();
                Keyboard.Stroke(VirtualKeyShort.HOME, true);
                await Task.Delay(delay);

                Keyboard.Press(VirtualKeyShort.LCONTROL, true);
                ctrlHeld = true;
                await Task.Delay(delay);
                ReleaseShift();
                Keyboard.Stroke(VirtualKeyShort.KEY_V, true);
                await Task.Delay(delay);
                Keyboard.Release(VirtualKeyShort.LCONTROL, true);
                ctrlHeld = false;
                await Task.Delay(delay);

                ReleaseShift();
                Keyboard.Stroke(VirtualKeyShort.RETURN, true);

                // Give GW2 time to read the paste and open the browser.
                await Task.Delay(300);
                if (previousClipboard != null)
                    await ClipboardUtil.WindowsClipboardService.SetTextAsync(previousClipboard);

                if (_returnFocusToGame.Value)
                {
                    await Task.Delay(500);
                    GameService.GameIntegration.Gw2Instance.FocusGw2();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Wiki search failed.");
                ScreenNotification.ShowNotification("Wiki Search failed. See the Blish HUD log for details.", ScreenNotification.NotificationType.Warning);
            }
            finally
            {
                // Never leave a simulated Ctrl held down system-wide.
                if (ctrlHeld) Keyboard.Release(VirtualKeyShort.LCONTROL, true);
                _busy = false;
            }
        }

        private static async Task<bool> WaitForChatAsync()
        {
            for (int waited = 0; waited < 1000; waited += 10)
            {
                if (GameService.Gw2Mumble.UI.IsTextInputFocused) return true;
                await Task.Delay(10);
            }
            return false;
        }

        private static async Task<bool> WaitForShiftReleaseAsync()
        {
            for (int waited = 0; waited < 5000; waited += 10)
            {
                if (!IsShiftDown()) return true;
                await Task.Delay(10);
            }
            return false;
        }

        private static void ReleaseShift()
        {
            Keyboard.Release(VirtualKeyShort.LSHIFT, true);
            Keyboard.Release(VirtualKeyShort.RSHIFT, true);
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
