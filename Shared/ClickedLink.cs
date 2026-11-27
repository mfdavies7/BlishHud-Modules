using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls.Extern;
using static Bytex.ModuleShared.ChatInput;
using Keyboard = Blish_HUD.Controls.Intern.Keyboard;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// The link GW2 put in chat when an item (or skill, trait…) was shift-clicked: reads it out of the chat box, and can
    /// take it out again, leaving whatever else the player had typed.
    /// </summary>
    internal static class ClickedLink
    {
        private static readonly Logger Logger = Logger.GetLogger(typeof(ClickedLink));

        // GW2 copies a link in the chat box as its chat code; the [Name] form is matched too in case it doesn't.
        private static readonly Regex LinkAtEnd = new Regex(@"\[[^\[\]]+\]$");

        // A link as its name: "[Mithril Ore]", or "[250 Mithril Ore]" for a stack. A number before "Slot" is part of
        // the name (20 Slot Invisible Bag), not an amount.
        private static readonly Regex NameLink = new Regex(@"^\[(?:(\d+) (?!Slot ))?([^\[\]&][^\[\]]*)\]$");

        /// <summary>
        /// The name and amount in a link copied as its name, which is in the game's language. Null for a chat code
        /// ("[&amp;AgH0TAAA]") or anything that isn't a link.
        /// </summary>
        public static string NameOf(string link, out int amount)
        {
            amount = 1;
            var match = NameLink.Match(link?.Trim() ?? "");
            if (!match.Success) return null;
            if (match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out int count) && count > 0) amount = count;
            string name = match.Groups[2].Value.Trim();
            return name.Length == 0 ? null : name;
        }

        /// <summary>
        /// After a shift-click, waits for the chat box holding the link to be focused. Clicking a window while chat is open
        /// can take focus from chat (GW2 still puts the link in it); then Enter, which brings chat back with its text, focuses
        /// it again, and chat counts as closed before the click, so taking the link out closes it again. False if chat
        /// doesn't come up: the click wasn't on something linkable.
        /// </summary>
        public static async Task<(bool ready, bool chatWasOpen)> WaitForChatAsync(bool chatWasOpen, int delay)
        {
            if (chatWasOpen)
            {
                // Mumble reports focus a frame or two late.
                await Task.Delay(delay);
                if (GameService.Gw2Mumble.UI.IsTextInputFocused) return (true, true);

                Logger.Info("Chat lost focus with the shift-click; pressing Enter to focus it again.");
                Stroke(VirtualKeyShort.RETURN, chatOpen: false);
            }
            bool ready = await WaitAsync(() => GameService.Gw2Mumble.UI.IsTextInputFocused, 1000);
            if (!ready && chatWasOpen) Logger.Info("Chat didn't come back after Enter.");
            return (ready, false);
        }

        // Put on the clipboard before a copy; still there afterwards means nothing was selected.
        private const string CopyCheckMarker = "Bytex.ModuleShared:empty-copy-check";

        /// <summary>
        /// GW2 put the link at the cursor, so the text up to the cursor ends with it. Copies that, then the whole chat box,
        /// to split the link from the rest of the text. Returns a null link (and deselects) if the text before the cursor
        /// doesn't end with a link; otherwise all the text is left selected.
        /// </summary>
        public static async Task<(string link, string otherText)> TakeAsync(int delay)
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
                Logger.Info(beforeCursor == null
                    ? "Nothing was copied from chat (Shift+Home selected nothing)."
                    : "The chat text before the cursor doesn't end in a link.");
                Stroke(VirtualKeyShort.END); // deselect, leave the text as it was
                return (null, null);
            }

            await CtrlStrokeAsync(VirtualKeyShort.KEY_A, delay);
            string all = await CopySelectionAsync(delay);
            if (all == null || !all.StartsWith(beforeCursor, StringComparison.Ordinal))
            {
                Stroke(VirtualKeyShort.END);
                throw new AbortException("Couldn't read the chat box. Try again.");
            }

            return (match.Value, beforeCursor.Substring(0, match.Index) + all.Substring(beforeCursor.Length));
        }

        /// <summary>
        /// Takes the shift-clicked link out of chat: reads it, puts back only the rest of the text, and closes chat again
        /// if it was closed before the click. Null if the chat box doesn't end in a link at the cursor (the click wasn't
        /// on one); chat is then left as it is.
        /// </summary>
        public static async Task<string> TakeOutAsync(bool chatWasOpen, int delay)
        {
            var (link, otherText) = await TakeAsync(delay);
            if (link == null) return null;

            // All the text is selected, so Backspace or a paste replaces all of it.
            if (otherText.Length == 0 || !chatWasOpen)
            {
                Stroke(VirtualKeyShort.BACK);
            }
            else
            {
                await SetClipboardAsync(otherText);
                await CtrlStrokeAsync(VirtualKeyShort.KEY_V, delay);
            }
            await Task.Delay(delay);

            if (!chatWasOpen)
            {
                // Enter on an empty chat box closes it without sending anything, as TyrianQoL does. Escape can't be used:
                // it closes the topmost window first (the inventory the item was clicked in), not chat. So text the
                // player had left unsent in the closed chat box is cleared with the link; Enter would send it.
                if (otherText.Length > 0) Logger.Info("Cleared the unsent text in chat to close it.");
                Stroke(VirtualKeyShort.RETURN);
                if (!await WaitAsync(() => !GameService.Gw2Mumble.UI.IsTextInputFocused, 1000))
                    throw new AbortException("Chat didn't close. Close it yourself.");
            }
            return link;
        }

        /// <summary>Copies the selected chat text (copy, not cut, so nothing is lost); null if nothing is selected.</summary>
        public static async Task<string> CopySelectionAsync(int delay)
        {
            await SetClipboardAsync(CopyCheckMarker);
            await CtrlStrokeAsync(VirtualKeyShort.KEY_C, delay);
            await Task.Delay(delay);
            string copied = await ClipboardUtil.WindowsClipboardService.GetTextAsync();
            return string.IsNullOrEmpty(copied) || copied == CopyCheckMarker ? null : copied;
        }
    }
}
