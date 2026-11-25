using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// Blish HUD's screen notifications: one line, in a fixed-width banner, so a long message is cut off. This splits
    /// one that's too wide over several banners, at word breaks.
    /// </summary>
    internal static class Notify
    {
        // The banner is 1024 px wide and draws the message in Menomonia 36; this leaves room at the sides.
        private const float MaxLineWidth = 820;

        /// <summary>Shows "&lt;module name&gt;: &lt;message&gt;", over as many banners as it needs, first line on top.</summary>
        public static void Show(string moduleName, string message,
            ScreenNotification.NotificationType type = ScreenNotification.NotificationType.Info)
        {
            var lines = Wrap($"{moduleName}: {message}");
            // A new banner appears above the ones already showing, so the last line goes first.
            for (int i = lines.Count - 1; i >= 0; i--) ScreenNotification.ShowNotification(lines[i], type);
        }

        public static void Warning(string moduleName, string message) =>
            Show(moduleName, message, ScreenNotification.NotificationType.Warning);

        private static List<string> Wrap(string text)
        {
            var font = GameService.Content.GetFont(ContentService.FontFace.Menomonia, ContentService.FontSize.Size36,
                ContentService.FontStyle.Regular);
            var lines = new List<string>();
            string line = "";
            foreach (string word in text.Split(' '))
            {
                string longer = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && font.MeasureString(longer).Width > MaxLineWidth)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = longer;
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }
    }
}
