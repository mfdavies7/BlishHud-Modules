using System;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Gw2Sharp.ChatLinks;
using ModuleShared;
using static ModuleShared.ChatInput;
using Mouse = Blish_HUD.Controls.Intern.Mouse;
using MouseButton = Blish_HUD.Controls.Intern.MouseButton;

namespace WaypointPaste
{
    [Export(typeof(Module))]
    public class WaypointPasteModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<WaypointPasteModule>();

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
                () => "Paste Waypoint Hotkey",
                () => "Whispers the waypoint to your own character, so its link shows up in chat, ready to click or share.");
            _waypoint = settings.DefineSetting("Waypoint", "[&BCAJAAA=]",
                () => "Waypoint Chat Code",
                () => "The waypoint to paste, for example [&BCAJAAA=]. Shift-click a waypoint on the map to get its code.");
            _useClipboardFirst = settings.DefineSetting("UseClipboardFirst", false,
                () => "Use Copied Waypoint First",
                () => "If your clipboard holds a waypoint, point of interest or vista chat code, paste that instead of the saved one.");
            _restoreClipboard = settings.DefineSetting("RestoreClipboard", true,
                () => "Restore Clipboard",
                () => "Put back what you had copied before. The module uses the clipboard to type in chat.");
            _overwriteUnsentText = settings.DefineSetting("OverwriteUnsentText", false,
                () => "Overwrite Unsent Chat Text",
                () => "If the chat box still holds text you typed but didn't send, delete it and paste the waypoint. The deleted text can't be recovered. Off: stop and leave your text in chat.");
            _autoClickLink = settings.DefineSetting("AutoClickChatLink", false,
                () => "Auto-Click Chat Link",
                () => "After the whisper is sent, click its link in chat for you. Save the spot to click with Set Click Position Hotkey first.");
            _setClickPositionKey = settings.DefineSetting("SetClickPositionKey", new KeyBinding(),
                () => "Set Click Position Hotkey",
                () => "Send a waypoint whisper, hover over its link in chat, then press this to save that spot. Set it again if you move or resize chat.");
            // 0, 0 means not set: the top-left corner of the main screen is never where a chat link lands.
            int left = GetSystemMetrics(SmXVirtualScreen), top = GetSystemMetrics(SmYVirtualScreen);
            _clickX = settings.DefineSetting("ClickX", 0,
                () => "Click Position X",
                () => "Screen X position of the chat link, in pixels. Set by Set Click Position Hotkey, or type it in. 0, 0 means not set.");
            _clickX.SetRange(left, left + GetSystemMetrics(SmCxVirtualScreen) - 1);
            _clickY = settings.DefineSetting("ClickY", 0,
                () => "Click Position Y",
                () => "Screen Y position of the chat link, in pixels. Set by Set Click Position Hotkey, or type it in. 0, 0 means not set.");
            _clickY.SetRange(top, top + GetSystemMetrics(SmCyVirtualScreen) - 1);
            _clickWaitMs = settings.DefineSetting("ClickWaitMs", 500,
                () => "Wait Before Clicking Link (ms)",
                () => "Time for the whisper to show up in chat before the link is clicked. Raise it if the click sometimes misses the link.");
            _clickWaitMs.SetRange(20, 3000);
            _keyDelayMs = settings.DefineSetting("KeyDelayMs", 60,
                () => "Key Press Delay (ms)",
                () => "Time between simulated key presses. Raise it if the whisper is sometimes typed wrong or not sent. Below one game frame (about 16 ms at 60 FPS) keys may be missed.");
            _keyDelayMs.SetRange(20, 250);
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
            _pasteKey.Value.Enabled = false;
            _pasteKey.Value.Activated -= PasteKey_Activated;
            _setClickPositionKey.Value.Enabled = false;
            _setClickPositionKey.Value.Activated -= SetClickPositionKey_Activated;
        }

        private void SetClickPositionKey_Activated(object sender, EventArgs e)
        {
            var cursor = Mouse.GetPosition();
            _clickX.Value = cursor.X;
            _clickY.Value = cursor.Y;
            ScreenNotification.ShowNotification("Waypoint Paste: Chat link click position saved.");
        }

        private void PasteKey_Activated(object sender, EventArgs e)
        {
            var gw2 = GameService.GameIntegration.Gw2Instance;
            if (_busy || !gw2.IsInGame || !gw2.Gw2HasFocus) return;

            // Chat must start closed: typing into an open message would mix with the whisper.
            if (GameService.Gw2Mumble.UI.IsTextInputFocused)
            {
                ScreenNotification.ShowNotification("Waypoint Paste: Close chat first.", ScreenNotification.NotificationType.Warning);
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
                if (!IsPointOfInterest(waypoint)) throw new AbortException("The saved waypoint isn't a valid chat code. Check Waypoint Chat Code in the settings.");

                await ChatActions.WhisperToSelfAsync(waypoint, delay, _overwriteUnsentText.Value);

                if (_autoClickLink.Value) await ClickChatLinkAsync(delay);
            }
            catch (AbortException ex)
            {
                ScreenNotification.ShowNotification($"Waypoint Paste: {ex.Message}", ScreenNotification.NotificationType.Warning);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Waypoint Paste failed.");
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
                throw new AbortException("Whisper sent, but no click position is saved. Hover over the link in chat and press Set Click Position Hotkey.");

            // Time for the whisper to come back from the server and show up in chat.
            await Task.Delay(_clickWaitMs.Value);
            EnsureSafe(chatOpen: false);

            var previous = Mouse.GetPosition();
            bool buttonDown = false;
            try
            {
                // sendToSystem: screen coordinates, and real input rather than window messages, as GW2 needs the cursor over the link.
                Mouse.SetPosition(_clickX.Value, _clickY.Value, true);
                await Task.Delay(delay); // let GW2 see the cursor over the link before clicking
                EnsureSafe(chatOpen: false);
                Mouse.Press(MouseButton.LEFT, sendToSystem: true);
                buttonDown = true;
                await Task.Delay(delay); // GW2 reads input once per frame
                Mouse.Release(MouseButton.LEFT, sendToSystem: true);
                buttonDown = false;
                await Task.Delay(delay);
            }
            finally
            {
                // Never leave the left button held down, and give the player their cursor back.
                if (buttonDown) Mouse.Release(MouseButton.LEFT, sendToSystem: true);
                Mouse.SetPosition(previous.X, previous.Y, true);
            }
        }

        /// <summary>Waypoints, points of interest and vistas all share the PointOfInterest link type.</summary>
        private static bool IsPointOfInterest(string text) =>
            !string.IsNullOrWhiteSpace(text)
            && Gw2ChatLink.TryParse(text.Trim(), out IGw2ChatLink link)
            && link?.Type == ChatLinkType.PointOfInterest;

    }
}
