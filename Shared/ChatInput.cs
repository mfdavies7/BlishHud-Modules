using System;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls.Extern;
using Keyboard = Blish_HUD.Controls.Intern.Keyboard;

namespace Bytex.ModuleShared
{
    /// <summary>Stops a simulated input sequence; the message is shown to the player.</summary>
    internal sealed class AbortException : Exception
    {
        public AbortException(string message) : base(message) { }
    }

    /// <summary>
    /// Simulated typing into GW2 chat. Keys are sent system-wide, so every key first checks that GW2 still has focus
    /// (they'd go to another app) and that chat is open or closed as expected (otherwise V, Tab and Enter act as game keys).
    /// </summary>
    internal static class ChatInput
    {
        public static bool IsSafe(bool chatOpen)
        {
            var gw2 = GameService.GameIntegration.Gw2Instance;
            return !ModuleLifetime.Ended && gw2.IsInGame && gw2.Gw2HasFocus && GameService.Gw2Mumble.UI.IsTextInputFocused == chatOpen;
        }

        public static void EnsureSafe(bool chatOpen)
        {
            ModuleLifetime.ThrowIfEnded(); // nothing more is sent once the module is turned off
            var gw2 = GameService.GameIntegration.Gw2Instance;
            if (!gw2.Gw2HasFocus) throw new AbortException("Stopped because Guild Wars 2 lost focus.");
            if (!gw2.IsInGame) throw new AbortException("Stopped because you left the game (loading screen?).");
            if (GameService.Gw2Mumble.UI.IsTextInputFocused != chatOpen)
                throw new AbortException(chatOpen ? "Stopped because chat closed." : "Close chat first.");
        }

        /// <summary>
        /// Shift is lifted right before the key: the player may still be holding it (Home would become "select to start"),
        /// and key repeat would press it again if it were lifted any earlier.
        /// </summary>
        public static void Stroke(VirtualKeyShort key, bool chatOpen = true)
        {
            EnsureSafe(chatOpen);
            Keyboard.Release(VirtualKeyShort.LSHIFT, true);
            Keyboard.Release(VirtualKeyShort.RSHIFT, true);
            Keyboard.Stroke(key, true);
        }

        /// <summary>GW2 reads keys once per frame, so Ctrl must be held for a moment before the key counts as a shortcut.</summary>
        public static async Task CtrlStrokeAsync(VirtualKeyShort key, int delay)
        {
            EnsureSafe(chatOpen: true);
            Keyboard.Press(VirtualKeyShort.LCONTROL, true);
            try
            {
                await Task.Delay(delay);
                Stroke(key);
                await Task.Delay(delay);
            }
            finally
            {
                // Never leave a simulated Ctrl held down system-wide.
                Keyboard.Release(VirtualKeyShort.LCONTROL, true);
            }
            await Task.Delay(delay);
        }

        public static async Task SetClipboardAsync(string text)
        {
            if (await ClipboardUtil.WindowsClipboardService.SetTextAsync(text)) return;

            // Another program may be holding the clipboard open.
            throw new AbortException("Couldn't use the clipboard. Another program may be using it; try again.");
        }

        public static async Task<bool> WaitAsync(Func<bool> condition, int timeoutMs)
        {
            for (int waited = 0; waited < timeoutMs; waited += 10)
            {
                if (condition()) return true;
                await Task.Delay(10);
            }
            return false;
        }
    }
}
