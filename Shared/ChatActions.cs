using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls.Extern;
using Microsoft.Xna.Framework.Input;
using static ModuleShared.ChatInput;
using Keyboard = Blish_HUD.Controls.Intern.Keyboard;

namespace ModuleShared
{
    /// <summary>
    /// Whole chat actions built from <see cref="ChatInput"/>. They use the clipboard and leave whatever they put there;
    /// callers save and restore it.
    /// </summary>
    internal static class ChatActions
    {
        private static readonly Keys[] Modifiers =
            { Keys.LeftShift, Keys.RightShift, Keys.LeftControl, Keys.RightControl, Keys.LeftAlt, Keys.RightAlt };

        // Put on the clipboard before copying the chat box; still there afterwards means the box was empty.
        private const string DraftCheckMarker = "ModuleShared:empty-chat-check";

        /// <summary>A Blish HUD text box takes keyboard focus from GW2; simulated keys must go to the game.</summary>
        public static async Task FocusGameAsync()
        {
            var gw2 = GameService.GameIntegration.Gw2Instance;
            if (!gw2.IsInGame) throw new AbortException("Guild Wars 2 isn't in game (loading screen?).");
            // Windows can refuse or undo a focus switch, so ask again until GW2 has kept focus for a few frames.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!gw2.Gw2HasFocus) gw2.FocusGw2();
                if (await WaitAsync(() => gw2.Gw2HasFocus, 1000))
                {
                    await Task.Delay(100);
                    if (gw2.Gw2HasFocus) return;
                }
            }
            throw new AbortException("couldn't switch back to Guild Wars 2. Click the game and try again.");
        }

        /// <summary>
        /// Pastes text at the cursor in chat, opening chat first if needed, and leaves chat open for the player to send.
        /// Unsent text GW2 kept from earlier stays, like shift-clicking a link into a message.
        /// </summary>
        public static async Task InsertIntoChatAsync(string text, int delay)
        {
            await ReleaseModifiersAsync();

            if (!GameService.Gw2Mumble.UI.IsTextInputFocused)
            {
                Stroke(VirtualKeyShort.RETURN, chatOpen: false);
                if (!await WaitAsync(() => GameService.Gw2Mumble.UI.IsTextInputFocused, 1000))
                    throw new AbortException("chat didn't open.");
                await Task.Delay(delay);
            }

            await SetClipboardAsync(text);
            await CtrlStrokeAsync(VirtualKeyShort.KEY_V, delay);
        }

        /// <summary>
        /// Whispers the message to the player's own character so it shows in chat, then switches chat back to /s.
        /// Chat that is already open is closed first; text typed into it stays and is handled as unsent text.
        /// </summary>
        public static async Task WhisperToSelfAsync(string message, int delay, bool overwriteUnsentText)
        {
            string name = GameService.Gw2Mumble.PlayerCharacter.Name;
            if (string.IsNullOrEmpty(name)) throw new AbortException("couldn't read your character name.");

            await ReleaseModifiersAsync();

            // Escape only goes to chat while it's open (Stroke checks); on its own it would open the game menu.
            if (GameService.Gw2Mumble.UI.IsTextInputFocused)
            {
                Stroke(VirtualKeyShort.ESCAPE, chatOpen: true);
                if (!await WaitAsync(() => !GameService.Gw2Mumble.UI.IsTextInputFocused, 1000))
                    throw new AbortException("chat is open and couldn't be closed. Close it and try again.");
                await Task.Delay(delay);
            }

            Stroke(VirtualKeyShort.RETURN, chatOpen: false);
            if (!await WaitAsync(() => GameService.Gw2Mumble.UI.IsTextInputFocused, 1000))
                throw new AbortException("chat didn't open.");
            await Task.Delay(delay);

            // GW2 keeps unsent text when chat loses focus; Enter brings it back and the whisper would be added to it.
            if (await ChatHasTextAsync(delay))
            {
                if (!overwriteUnsentText)
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
            foreach (string text in new[] { "/s ", "/w ", name, null, message })
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
        }

        /// <summary>A held hotkey modifier would turn Enter or V into other shortcuts.</summary>
        private static async Task ReleaseModifiersAsync()
        {
            foreach (var key in Modifiers) Keyboard.Release((VirtualKeyShort)key, true);
            if (!await WaitAsync(() => !GameService.Input.Keyboard.KeysDown.Any(Modifiers.Contains), 2000))
                throw new AbortException("let go of Shift, Ctrl and Alt and try again.");
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
    }
}
