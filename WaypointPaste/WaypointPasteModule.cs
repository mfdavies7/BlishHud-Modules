using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Controls.Extern;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Gw2Sharp.ChatLinks;
using Microsoft.Xna.Framework.Input;
using ModuleShared;
using static ModuleShared.ChatInput;
using Keyboard = Blish_HUD.Controls.Intern.Keyboard;

namespace WaypointPaste
{
    [Export(typeof(Module))]
    public class WaypointPasteModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<WaypointPasteModule>();

        private static readonly Keys[] Modifiers =
            { Keys.LeftShift, Keys.RightShift, Keys.LeftControl, Keys.RightControl, Keys.LeftAlt, Keys.RightAlt };

        // Put on the clipboard before copying the chat box; still there afterwards means the box was empty.
        private const string DraftCheckMarker = "WaypointPaste:empty-chat-check";

        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

        // The desktop across all monitors; a monitor left of or above the main one has negative coordinates.
        private const int SmXVirtualScreen = 76, SmYVirtualScreen = 77, SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;

        private SettingEntry<KeyBinding> _pasteKey;
        private SettingEntry<string> _waypoint;
        private SettingEntry<bool> _useClipboardFirst;
        private SettingEntry<bool> _restoreClipboard;
        private SettingEntry<bool> _overwriteUnsentText;
        private SettingEntry<bool> _autoClickLink;
        private SettingEntry<KeyBinding> _setClickPositionKey;
        private SettingEntry<int> _clickWaitMs;
        private SettingEntry<int> _clickX;
        private SettingEntry<int> _clickY;
        private SettingEntry<int> _keyDelayMs;

        private bool _busy;

        [ImportingConstructor]
        public WaypointPasteModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { }

        protected override void DefineSettings(SettingCollection settings)
        {
            _pasteKey = settings.DefineSetting("PasteKey", new KeyBinding(),
                () => "Paste waypoint hotkey",
                () => "Whispers the waypoint to your own character, so the link shows in chat.");
            _waypoint = settings.DefineSetting("Waypoint", "[&BCAJAAA=]",
                () => "Waypoint chat code",
                () => "The waypoint to paste, for example [&BCAJAAA=]. Shift-click a waypoint on the map to get its code.");
            _useClipboardFirst = settings.DefineSetting("UseClipboardFirst", false,
                () => "Use copied waypoint first",
                () => "If your clipboard holds a waypoint, point of interest or vista chat code, paste that instead of the saved one.");
            _restoreClipboard = settings.DefineSetting("RestoreClipboard", true,
                () => "Restore clipboard",
                () => "Put back what you had copied before. The module uses the clipboard to type in chat.");
            _overwriteUnsentText = settings.DefineSetting("OverwriteUnsentText", false,
                () => "Overwrite unsent chat text",
                () => "If the chat box still holds text you typed but didn't send, delete it and paste the waypoint. The deleted text can't be recovered. Off: stop and leave your text in chat.");
            _autoClickLink = settings.DefineSetting("AutoClickChatLink", false,
                () => "Auto Click Chat Link",
                () => "After the whisper is sent, click its link in chat for you. Set the spot to click with the Set click position hotkey first.");
            _setClickPositionKey = settings.DefineSetting("SetClickPositionKey", new KeyBinding(),
                () => "Set click position hotkey",
                () => "Send a waypoint whisper, hover over its link in chat, then press this to save that spot. Set it again if you move or resize chat.");
            // 0, 0 means not set: the top-left corner of the main screen is never where a chat link lands.
            int left = GetSystemMetrics(SmXVirtualScreen), top = GetSystemMetrics(SmYVirtualScreen);
            _clickX = settings.DefineSetting("ClickX", 0,
                () => "Click position X",
                () => "Screen X position of the chat link, in pixels. Set by the Set click position hotkey, or type it in. 0, 0 means not set.");
            _clickX.SetRange(left, left + GetSystemMetrics(SmCxVirtualScreen) - 1);
            _clickY = settings.DefineSetting("ClickY", 0,
                () => "Click position Y",
                () => "Screen Y position of the chat link, in pixels. Set by the Set click position hotkey, or type it in. 0, 0 means not set.");
            _clickY.SetRange(top, top + GetSystemMetrics(SmCyVirtualScreen) - 1);
            _clickWaitMs = settings.DefineSetting("ClickWaitMs", 500,
                () => "Wait before clicking link (ms)",
                () => "Time for the whisper to show up in chat before the link is clicked. Raise it if the click sometimes misses the link.");
            _clickWaitMs.SetRange(0, 3000);
            _keyDelayMs = settings.DefineSetting("KeyDelayMs", 40,
                () => "Key press delay (ms)",
                () => "Time between simulated key presses. Raise it if the whisper is sometimes typed wrong or not sent. Below one game frame (about 16 ms at 60 FPS) keys may be missed.");
            _keyDelayMs.SetRange(1, 250);
        }

        public override IView GetSettingsView() => new SettingsWithValuesView(ModuleParameters.SettingsManager.ModuleSettings);

        protected override void Initialize()
        {
            _pasteKey.Value.Enabled = true;
            _pasteKey.Value.Activated += PasteKey_Activated;
            _setClickPositionKey.Value.Enabled = true;
            _setClickPositionKey.Value.Activated += SetClickPositionKey_Activated;
        }

        protected override void Unload()
        {
            _pasteKey.Value.Activated -= PasteKey_Activated;
            _setClickPositionKey.Value.Activated -= SetClickPositionKey_Activated;
        }

        private void SetClickPositionKey_Activated(object sender, EventArgs e)
        {
            if (!GetCursorPos(out var cursor)) return;
            _clickX.Value = cursor.X;
            _clickY.Value = cursor.Y;
            ScreenNotification.ShowNotification("Waypoint Paste: chat link click position saved.");
        }

        private void PasteKey_Activated(object sender, EventArgs e)
        {
            var gw2 = GameService.GameIntegration.Gw2Instance;
            if (_busy || !gw2.IsInGame || !gw2.Gw2HasFocus) return;

            // Chat must start closed: typing into an open message would mix with the whisper.
            if (GameService.Gw2Mumble.UI.IsTextInputFocused)
            {
                ScreenNotification.ShowNotification("Waypoint Paste: close chat first.", ScreenNotification.NotificationType.Warning);
                return;
            }

            _ = PasteWaypointAsync();
        }

        private async Task PasteWaypointAsync()
        {
            _busy = true;
            // Restore even an empty clipboard, so the "/s " typed last isn't left behind.
            bool restoreClipboard = false;
            string previousClipboard = null;
            try
            {
                int delay = _keyDelayMs.Value;
                string clipboard = await ClipboardUtil.WindowsClipboardService.GetTextAsync();
                restoreClipboard = _restoreClipboard.Value;
                previousClipboard = clipboard ?? "";

                string waypoint = _useClipboardFirst.Value && IsPointOfInterest(clipboard) ? clipboard.Trim() : _waypoint.Value.Trim();
                if (!IsPointOfInterest(waypoint)) throw new AbortException("the saved waypoint chat code isn't valid.");

                string name = GameService.Gw2Mumble.PlayerCharacter.Name;
                if (string.IsNullOrEmpty(name)) throw new AbortException("couldn't read your character name.");

                // The hotkey's own modifiers would turn Enter or V into other shortcuts.
                foreach (var key in Modifiers) Keyboard.Release((VirtualKeyShort)key, true);
                if (!await WaitAsync(() => !GameService.Input.Keyboard.KeysDown.Any(Modifiers.Contains), 2000))
                    throw new AbortException("let go of Shift, Ctrl and Alt and try again.");

                Stroke(VirtualKeyShort.RETURN, chatOpen: false);
                if (!await WaitAsync(() => GameService.Gw2Mumble.UI.IsTextInputFocused, 1000))
                    throw new AbortException("chat didn't open.");
                await Task.Delay(delay);

                // GW2 keeps unsent text when chat loses focus; Enter brings it back and the whisper would be added to it.
                if (await ChatHasTextAsync(delay))
                {
                    if (!_overwriteUnsentText.Value)
                    {
                        if (IsSafe(chatOpen: true)) Keyboard.Stroke(VirtualKeyShort.END, true); // deselect, leave the text as it was
                        throw new AbortException("chat has unsent text. Send or delete it, then try again.");
                    }

                    // The text is still selected from the check, so Backspace deletes all of it.
                    Stroke(VirtualKeyShort.BACK, chatOpen: true);
                    await Task.Delay(delay);

                    // A missed key press would send the old text along with the whisper.
                    if (await ChatHasTextAsync(delay))
                    {
                        if (IsSafe(chatOpen: true)) Keyboard.Stroke(VirtualKeyShort.END, true);
                        throw new AbortException("couldn't clear the chat box. Try again or raise the key press delay.");
                    }
                }

                // "/s " first switches out of any whisper channel left over from earlier, so "/w " starts fresh.
                // Say is used because every player has it, unlike a guild channel.
                // Tab moves from the whisper name to the message box.
                foreach (string text in new[] { "/s ", "/w ", name, null, waypoint })
                {
                    if (text == null)
                    {
                        Stroke(VirtualKeyShort.TAB, chatOpen: true);
                        await Task.Delay(delay);
                        continue;
                    }

                    await SetClipboardAsync(text);
                    await CtrlStrokeAsync(VirtualKeyShort.KEY_V, delay);
                }

                Stroke(VirtualKeyShort.RETURN, chatOpen: true);
                if (!await WaitAsync(() => !GameService.Gw2Mumble.UI.IsTextInputFocused, 1000))
                    throw new AbortException("whisper may not have been sent; chat is still open.");
                await Task.Delay(delay);

                // Otherwise the next message the player types would also be whispered to themselves.
                // Enter on the now-empty box closes chat without sending anything (Escape could open the game menu).
                Stroke(VirtualKeyShort.RETURN, chatOpen: false);
                if (!await WaitAsync(() => GameService.Gw2Mumble.UI.IsTextInputFocused, 1000))
                    throw new AbortException("whisper sent, but chat didn't reopen to switch back to /s.");
                await Task.Delay(delay);
                await SetClipboardAsync("/s ");
                await CtrlStrokeAsync(VirtualKeyShort.KEY_V, delay);
                Stroke(VirtualKeyShort.RETURN, chatOpen: true);
                if (!await WaitAsync(() => !GameService.Gw2Mumble.UI.IsTextInputFocused, 1000))
                    throw new AbortException("whisper sent, but chat didn't close after switching back to /s.");

                if (_autoClickLink.Value) await ClickChatLinkAsync(delay);
            }
            catch (AbortException ex)
            {
                ScreenNotification.ShowNotification($"Waypoint Paste: {ex.Message}", ScreenNotification.NotificationType.Warning);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Waypoint paste failed.");
                ScreenNotification.ShowNotification("Waypoint Paste failed. See the Blish HUD log for details.", ScreenNotification.NotificationType.Warning);
            }
            finally
            {
                if (restoreClipboard)
                    await ClipboardUtil.WindowsClipboardService.SetTextAsync(previousClipboard);
                _busy = false;
            }
        }

        private async Task ClickChatLinkAsync(int delay)
        {
            if (_clickX.Value == 0 && _clickY.Value == 0)
                throw new AbortException("whisper sent, but no click position is saved. Hover over the link in chat and press the Set click position hotkey.");

            // Time for the whisper to come back from the server and show up in chat.
            await Task.Delay(_clickWaitMs.Value);
            EnsureSafe(chatOpen: false);

            if (!GetCursorPos(out var previous)) throw new AbortException("couldn't read the mouse position.");
            bool buttonDown = false;
            try
            {
                SetCursorPos(_clickX.Value, _clickY.Value);
                await Task.Delay(delay); // let GW2 see the cursor over the link before clicking
                EnsureSafe(chatOpen: false);
                mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                buttonDown = true;
                await Task.Delay(delay); // GW2 reads input once per frame
                mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                buttonDown = false;
                await Task.Delay(delay);
            }
            finally
            {
                // Never leave the left button held down, and give the player their cursor back.
                if (buttonDown) mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                SetCursorPos(previous.X, previous.Y);
            }
        }

        /// <summary>
        /// Selects everything in the open chat box and copies it (copy, not cut, so nothing is lost).
        /// The text stays selected afterwards.
        /// </summary>
        private static async Task<bool> ChatHasTextAsync(int delay)
        {
            await SetClipboardAsync(DraftCheckMarker);
            await CtrlStrokeAsync(VirtualKeyShort.KEY_A, delay);
            await CtrlStrokeAsync(VirtualKeyShort.KEY_C, delay);
            await Task.Delay(delay);
            string copied = await ClipboardUtil.WindowsClipboardService.GetTextAsync();
            return !string.IsNullOrEmpty(copied) && copied != DraftCheckMarker;
        }

        /// <summary>Waypoints, points of interest and vistas all share the PointOfInterest link type.</summary>
        private static bool IsPointOfInterest(string text) =>
            !string.IsNullOrWhiteSpace(text)
            && Gw2ChatLink.TryParse(text.Trim(), out IGw2ChatLink link)
            && link?.Type == ChatLinkType.PointOfInterest;

    }
}
