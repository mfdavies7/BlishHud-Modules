using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
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
using mfdavies7.ModuleShared;
using static mfdavies7.ModuleShared.ChatInput;
using Keyboard = Blish_HUD.Controls.Intern.Keyboard;

namespace mfdavies7.WikiSearch
{
    public enum WikiLanguage { GameLanguage, English, German, French, Spanish }

    [Export(typeof(Module))]
    public class WikiSearchModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<WikiSearchModule>();

        private const int HintCursorOffset = 25;

        // GW2 copies a link in the chat box as its chat code; the [Name] form is matched too in case it doesn't.
        private static readonly Regex LinkAtEnd = new Regex(@"\[[^\[\]]+\]$");

        // Put on the clipboard before a copy; still there afterwards means nothing was selected.
        private const string CopyCheckMarker = "mfdavies7.WikiSearch:empty-copy-check";

        private SettingEntry<WikiLanguage> _language;
        private SettingEntry<KeyBinding> _toggleKey;
        private SettingEntry<bool> _enabled;
        private SettingEntry<bool> _overwriteUnsentText;
        private SettingEntry<bool> _workWithMapOpen;
        private SettingEntry<bool> _returnFocusToGame;
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
                () => "Wiki Language",
                () => "Which wiki to search. Game Language uses the wiki for your game client's language.");
            _toggleKey = settings.DefineSetting("ToggleKey", new KeyBinding(),
                () => "Toggle Hotkey",
                () => "Turns Wiki Search on or off.");
            _enabled = settings.DefineSetting("Enabled", true,
                () => "Enabled",
                () => "Shift + left-click an item to open its wiki page. Turn off to link items in chat normally.");
            _overwriteUnsentText = settings.DefineSetting("OverwriteUnsentText", false,
                () => "Overwrite Unsent Chat Text",
                () => "Also search when chat was already open before the click: if chat still holds text you typed but didn't send, delete it and search the item. The deleted text can't be recovered. Off: with chat already open, shift-click links items into your message as usual.");
            _workWithMapOpen = settings.DefineSetting("WorkWithMapOpen", true,
                () => "Work When the Map Is Open",
                () => "Also search when shift-clicking on the world map (for example waypoints). Off: shift-click links map points as usual.");
            _returnFocusToGame = settings.DefineSetting("ReturnFocusToGame", false,
                () => "Return to the Game After Searching",
                () => "Switch back to Guild Wars 2 after the wiki opens in your browser.");
            _keyDelayMs = settings.DefineSetting("KeyDelayMs", 60,
                () => "Key Press Delay (ms)",
                () => "Time between simulated key presses. Raise it if the /wiki command is sometimes typed wrong or not sent. Below one game frame (about 16 ms at 60 FPS) keys may be missed.");
            _keyDelayMs.SetRange(20, 250);
            _waitForShiftRelease = settings.DefineSetting("WaitForShiftRelease", false,
                () => "Wait for Shift Release Before Sending",
                () => "Only type the /wiki command after you let go of Shift. Turn on if the command is sometimes missing or the item link gets replaced.");
            _showHint = settings.DefineSetting("ShowHint", true,
                () => "Show Hint While Holding Shift",
                () => "Show a hint next to the cursor while Shift is held and a click would open the wiki.");
        }

        public override IView GetSettingsView() => new SettingsWithValuesView(ModuleParameters.SettingsManager.ModuleSettings);

        // Loads the hint's texture, so it's done here rather than in Initialize, as Blish HUD's module guide asks.
        protected override Task LoadAsync()
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
            return Task.CompletedTask;
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
            _toggleKey.Value.Enabled = false;
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
            ScreenNotification.ShowNotification($"Wiki Search {(_enabled.Value ? "enabled" : "disabled")}.");
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
                   && gw2.IsInGame // not on character select or a loading screen
                   && gw2.Gw2HasFocus
                   && (_workWithMapOpen.Value || !ui.IsMapOpen)
                   && (_overwriteUnsentText.Value ||!ui.IsTextInputFocused);
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
                    var (link, _) = await TakeClickedLinkAsync(delay);
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
                    GameService.GameIntegration.Gw2Instance.FocusGw2();
                }
            }
            catch (AbortException ex)
            {
                ScreenNotification.ShowNotification($"Wiki Search: {ex.Message}", ScreenNotification.NotificationType.Warning);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Wiki Search failed.");
                ScreenNotification.ShowNotification("Wiki Search failed. See the Blish HUD log for details.", ScreenNotification.NotificationType.Warning);
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

        /// <summary>
        /// GW2 put the link at the cursor, so the text up to the cursor ends with it. Copies that, then the whole chat box,
        /// to split the link from the rest of the text. Returns a null link (and deselects) if the text before the cursor
        /// doesn't end with a link; otherwise all the text is left selected.
        /// </summary>
        private static async Task<(string link, string otherText)> TakeClickedLinkAsync(int delay)
        {
            // Shift+Home selects from the cursor to the start. Stroke would lift Shift, so the keys are sent directly.
            EnsureSafe(chatOpen: true);
            Keyboard.Press(VirtualKeyShort.LSHIFT, true);
            try
            {
                await Task.Delay(delay);
                EnsureSafe(chatOpen: true);
                Keyboard.Stroke(VirtualKeyShort.HOME, true);
                await Task.Delay(delay);
            }
            finally
            {
                Keyboard.Release(VirtualKeyShort.LSHIFT, true);
            }
            await Task.Delay(delay);

            string beforeCursor = await CopySelectionAsync(delay);
            var match = LinkAtEnd.Match(beforeCursor ?? "");
            if (!match.Success)
            {
                Stroke(VirtualKeyShort.END); // deselect, leave the text as it was
                return (null, null);
            }

            await CtrlStrokeAsync(VirtualKeyShort.KEY_A, delay);
            string all = await CopySelectionAsync(delay);
            if (all == null || !all.StartsWith(beforeCursor, StringComparison.Ordinal))
            {
                Stroke(VirtualKeyShort.END);
                throw new AbortException("Couldn't read the chat box, so nothing was searched. Try again, or raise Key Press Delay in the settings.");
            }

            return (match.Value, beforeCursor.Substring(0, match.Index) + all.Substring(beforeCursor.Length));
        }

        /// <summary>Copies the selected chat text (copy, not cut, so nothing is lost); null if nothing is selected.</summary>
        private static async Task<string> CopySelectionAsync(int delay)
        {
            await SetClipboardAsync(CopyCheckMarker);
            await CtrlStrokeAsync(VirtualKeyShort.KEY_C, delay);
            await Task.Delay(delay);
            string copied = await ClipboardUtil.WindowsClipboardService.GetTextAsync();
            return string.IsNullOrEmpty(copied) || copied == CopyCheckMarker ? null : copied;
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
