using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Gw2Sharp.ChatLinks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bytex.ModuleShared;
using static Bytex.ModuleShared.ChatInput;
using Mouse = Blish_HUD.Controls.Intern.Mouse;
using MouseButton = Blish_HUD.Controls.Intern.MouseButton;

namespace Bytex.WaypointPaste
{
    /// <summary>What clicking the corner icon does.</summary>
    public enum IconAction
    {
        [Description("Open Settings")] OpenSettings,
        [Description("Paste Waypoint")] PasteWaypoint,
        [Description("Paste and Click Waypoint")] PasteAndClickWaypoint,
        [Description("Nothing")] Nothing,
    }

    [Export(typeof(Module))]
    public class WaypointPasteModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<WaypointPasteModule>();

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }

        // The desktop across all monitors; a monitor left of or above the main one has negative coordinates.
        private const int SmXVirtualScreen = 76, SmYVirtualScreen = 77, SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;

        private SettingEntry<KeyBinding> _pasteKey;
        private SettingEntry<string> _waypoint;
        private SettingEntry<bool> _useClipboardFirst;
        private SettingEntry<bool> _overwriteUnsentText;
        private SettingEntry<bool> _switchBackToSay;
        private SettingEntry<bool> _autoClickLink;
        private SettingEntry<KeyBinding> _setClickPositionKey;
        private SettingEntry<int> _clickWaitMs;
        private SettingEntry<int> _clickX;
        private SettingEntry<int> _clickY;
        private SettingEntry<bool> _showClickPosition;
        private SettingEntry<int> _keyDelayMs;
        private SettingEntry<bool> _registerCornerIcon;
        private SettingEntry<IconAction> _iconLeftClick;
        private SettingEntry<IconAction> _iconRightClick;

        private bool _busy;

        private Texture2D _icon;
        private RegisteredCornerIcon _cornerIcon;
        private StandardWindow _settingsWindow;

        private ClickMarker _clickMarker;
        // The marker is also shown until then, after the position is set or changed.
        private DateTime _showMarkerUntilUtc;
        private static readonly TimeSpan MarkerShowTime = TimeSpan.FromSeconds(3);

        [ImportingConstructor]
        public WaypointPasteModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { }

        protected override void DefineSettings(SettingCollection settings)
        {
            // Saved under its old key, so the choice made with Show Corner Icon carries over.
            _registerCornerIcon = settings.DefineSetting("ShowCornerIcon", true,
                () => "Register Corner Icon",
                () => "Whether the module should register a corner icon. Off: paste with Paste Waypoint Hotkey, and open the settings with Settings on the module's page in Blish HUD's settings.");
            _iconLeftClick = settings.DefineSetting("IconLeftClick", IconAction.OpenSettings,
                () => "Corner Icon Left Click Action",
                () => "Defines the action of the corner icon when left clicked. Paste and Click Waypoint clicks the link only when Auto-Click Chat Link is on; otherwise it's the same as Paste Waypoint.");
            _iconRightClick = settings.DefineSetting("IconRightClick", IconAction.PasteAndClickWaypoint,
                () => "Corner Icon Right Click Action",
                () => "Defines the action of the corner icon when right clicked. Paste and Click Waypoint clicks the link only when Auto-Click Chat Link is on; otherwise it's the same as Paste Waypoint.");
            _pasteKey = settings.DefineSetting("PasteKey", new KeyBinding(),
                () => "Paste Waypoint Hotkey",
                () => "Whispers the waypoint to your own character, so its link shows up in chat, ready to click or share.");
            _waypoint = settings.DefineSetting("Waypoint", "[&BCAJAAA=]",
                () => "Waypoint Chat Code",
                () => "The waypoint to paste, for example [&BCAJAAA=]. Shift-click a waypoint on the map to get its code.");
            _useClipboardFirst = settings.DefineSetting("UseClipboardFirst", true,
                () => "Use Copied Waypoint First",
                () => "If your clipboard holds a waypoint, point of interest or vista chat code, paste that instead of the saved one.");
            _overwriteUnsentText = settings.DefineSetting("OverwriteUnsentText", false,
                () => "Overwrite Unsent Chat Text",
                () => "If the chat box still holds text you typed but didn't send, delete it and send the whisper. The deleted text can't be recovered. Off: stop and leave your text in chat.");
            _switchBackToSay = settings.DefineSetting("SwitchBackToSay", false,
                () => "Switch to /s After Whisper",
                () => "After the whisper is sent, switch chat back to /s, so the next message you type isn't also whispered to yourself. With Auto-Click Chat Link, this happens before the link is clicked. Off: chat stays on the whisper channel.");
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
            _showClickPosition = settings.DefineSetting("ShowClickPosition", false,
                () => "Show Click Position",
                () => "Mark the saved click position on screen, to check it's over the chat link. Off: it's still shown for a few seconds after Set Click Position Hotkey is pressed or the position is changed.");
            _clickWaitMs = settings.DefineSetting("ClickWaitMs", 500,
                () => "Wait Before Clicking Link (ms)",
                () => "Time for the whisper to show up in chat before the link is clicked. Raise it if the click sometimes misses the link.");
            _clickWaitMs.SetRange(20, 3000);
            _keyDelayMs = settings.DefineSetting("KeyDelayMs", 60,
                () => "Key Press Delay (ms)",
                () => "Time between simulated key presses. Raise it if the whisper is sometimes typed wrong or not sent. Below one game frame (about 16 ms at 60 FPS) keys may be missed.");
            _keyDelayMs.SetRange(20, 250);
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

        // Textures are loaded here rather than in Initialize, as Blish HUD's module guide asks.
        protected override Task LoadAsync()
        {
            _icon = ModuleParameters.ContentsManager.GetTexture(@"textures\WaypointPaste.png");
            // Right-click on release, not press: the button mustn't still be held while keys are typed and the link is clicked.
            _cornerIcon = new RegisteredCornerIcon(_registerCornerIcon, _icon,
                () => RunIconAction(_iconLeftClick.Value), () => RunIconAction(_iconRightClick.Value),
                rightClickOnRelease: true, _iconLeftClick, _iconRightClick);
            // The same window as the other modules' settings, so they look alike.
            _settingsWindow = new StandardWindow(AsyncTexture2D.FromAssetId(155997), new Rectangle(25, 26, 560, 640), new Rectangle(40, 50, 540, 590))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Waypoint Paste",
                Subtitle = "Settings",
                Emblem = _icon,
                Id = "bytex.waypointpaste.settings",
                SavesPosition = true,
            };
            return Task.CompletedTask;
        }

        private void RunIconAction(IconAction action)
        {
            switch (action)
            {
                case IconAction.OpenSettings:
                    if (_settingsWindow.Visible) _settingsWindow.Hide();
                    else OpenSettingsWindow();
                    break;
                case IconAction.PasteWaypoint:
                    StartPaste(click: false, fromIcon: true);
                    break;
                case IconAction.PasteAndClickWaypoint:
                    StartPaste(click: _autoClickLink.Value, fromIcon: true);
                    break;
            }
        }

        protected override void Initialize()
        {
            _pasteKey.Value.Enabled = true;
            _pasteKey.Value.Activated += PasteKey_Activated;
            _setClickPositionKey.Value.Enabled = true;
            _setClickPositionKey.Value.Activated += SetClickPositionKey_Activated;
            _clickX.SettingChanged += ClickPosition_SettingChanged;
            _clickY.SettingChanged += ClickPosition_SettingChanged;

            _clickMarker = new ClickMarker { Parent = GameService.Graphics.SpriteScreen, Visible = false };
        }

        protected override void Update(GameTime gameTime)
        {
            Point position = default;
            bool show = (_showClickPosition.Value || DateTime.UtcNow < _showMarkerUntilUtc)
                && !(_clickX.Value == 0 && _clickY.Value == 0)
                && GameService.GameIntegration.Gw2Instance.IsInGame
                && TryScreenToUi(_clickX.Value, _clickY.Value, out position);
            if (show) _clickMarker.CenterAt(position);
            _clickMarker.Visible = show;
        }

        /// <summary>A screen position in Blish HUD's UI units, which start at GW2's client area and scale with UI Size.</summary>
        private static bool TryScreenToUi(int x, int y, out Point position)
        {
            position = default;
            var window = GameService.GameIntegration.Gw2Instance.Gw2WindowHandle;
            var origin = new NativePoint();
            if (window == IntPtr.Zero || !ClientToScreen(window, ref origin)) return false;
            float scale = GameService.Graphics.UIScaleMultiplier;
            position = new Point((int)Math.Round((x - origin.X) / scale), (int)Math.Round((y - origin.Y) / scale));
            return true;
        }

        private void ClickPosition_SettingChanged(object sender, ValueChangedEventArgs<int> e) =>
            _showMarkerUntilUtc = DateTime.UtcNow + MarkerShowTime;

        protected override void Unload()
        {
            _pasteKey.Value.Enabled = false;
            _pasteKey.Value.Activated -= PasteKey_Activated;
            _setClickPositionKey.Value.Enabled = false;
            _setClickPositionKey.Value.Activated -= SetClickPositionKey_Activated;
            _clickX.SettingChanged -= ClickPosition_SettingChanged;
            _clickY.SettingChanged -= ClickPosition_SettingChanged;
            _clickMarker?.Dispose();
            _clickMarker = null;
            _cornerIcon?.Dispose();
            _cornerIcon = null;
            _settingsWindow?.Dispose();
            _settingsWindow = null;
            _icon?.Dispose();
            _icon = null;
        }

        private void SetClickPositionKey_Activated(object sender, EventArgs e)
        {
            var cursor = Mouse.GetPosition();
            _clickX.Value = cursor.X;
            _clickY.Value = cursor.Y;
            _showMarkerUntilUtc = DateTime.UtcNow + MarkerShowTime; // even if the position didn't change
            ScreenNotification.ShowNotification("Waypoint Paste: Chat link click position saved.");
        }

        private void PasteKey_Activated(object sender, EventArgs e) => StartPaste(_autoClickLink.Value, fromIcon: false);

        /// <summary>
        /// Whispers the waypoint, then clicks its link if <paramref name="click"/>. A hotkey only works while the game
        /// has focus; after clicking the icon Blish HUD may have it, so the game is focused first.
        /// </summary>
        private void StartPaste(bool click, bool fromIcon)
        {
            var gw2 = GameService.GameIntegration.Gw2Instance;
            if (_busy) return;
            if (!fromIcon && (!gw2.IsInGame || !gw2.Gw2HasFocus)) return;
            if (fromIcon && !gw2.IsInGame)
            {
                ScreenNotification.ShowNotification("Waypoint Paste: You need to be in game first.", ScreenNotification.NotificationType.Warning);
                return;
            }

            // Chat must start closed: typing into an open message would mix with the whisper.
            if (GameService.Gw2Mumble.UI.IsTextInputFocused)
            {
                ScreenNotification.ShowNotification("Waypoint Paste: Close chat first.", ScreenNotification.NotificationType.Warning);
                return;
            }

            // Set here, not in the task, so a second press can't start another paste before it runs.
            _busy = true;
            // Off Blish HUD's input thread: awaits would resume there, and if Blish HUD restarts that thread
            // the paste never finishes and _busy stays set until reload.
            _ = Task.Run(() => PasteWaypointAsync(click, focusGame: fromIcon));
        }

        private async Task PasteWaypointAsync(bool click, bool focusGame)
        {
            // The whisper is typed through the clipboard, so it's always put back afterwards. Even an empty
            // clipboard is restored, so the message typed last isn't left behind.
            string previousClipboard = null;
            try
            {
                int delay = _keyDelayMs.Value;
                string clipboard = await ClipboardUtil.WindowsClipboardService.GetTextAsync();
                previousClipboard = clipboard ?? "";

                string waypoint = _useClipboardFirst.Value && IsPointOfInterest(clipboard) ? clipboard.Trim() : _waypoint.Value.Trim();
                if (!IsPointOfInterest(waypoint)) throw new AbortException("The saved waypoint isn't a valid chat code. Check Waypoint Chat Code in the settings.");

                if (focusGame) await ChatActions.FocusGameAsync();

                // Switching back to /s is part of the whisper, so it always happens before the link is clicked.
                await ChatActions.WhisperToSelfAsync(waypoint, delay, _overwriteUnsentText.Value, _switchBackToSay.Value);

                if (click) await ClickChatLinkAsync(delay);
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
                try
                {
                    if (previousClipboard != null)
                        await ClipboardUtil.WindowsClipboardService.SetTextAsync(previousClipboard);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Couldn't restore the clipboard.");
                }
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
