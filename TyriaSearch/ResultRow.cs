using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TyriaSearch
{
    /// <summary>
    /// One search result. Drawn in a single control so clicks anywhere on the row reach it,
    /// rather than landing on a child label or image.
    /// </summary>
    internal sealed class ResultRow : Control
    {
        public const int RowHeight = 40;
        private const int IconSize = 32;
        private const int TextLeft = 8 + IconSize + 8;

        private static readonly Color HoverColor = new Color(255, 255, 255, 30);
        private static readonly Color SubtitleColor = new Color(190, 190, 190);

        public SearchItem Item { get; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public AsyncTexture2D Icon { get; set; }
        /// <summary>Drawn while <see cref="Icon"/> is still downloading.</summary>
        public Texture2D PlaceholderIcon { get; set; }
        public Color TitleColor { get; set; } = Color.White;

        public ResultRow(SearchItem item)
        {
            Item = item;
            Height = RowHeight;
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (MouseOver) spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, bounds, HoverColor);

            var iconRect = new Rectangle(8, (Height - IconSize) / 2, IconSize, IconSize);
            if (Icon != null && Icon.HasTexture) spriteBatch.DrawOnCtrl(this, Icon, iconRect);
            else if (PlaceholderIcon != null) spriteBatch.DrawOnCtrl(this, PlaceholderIcon, iconRect);

            int textWidth = Width - TextLeft - 4;
            spriteBatch.DrawStringOnCtrl(this, Title ?? "", GameService.Content.DefaultFont16,
                new Rectangle(TextLeft, 2, textWidth, 20), TitleColor, false, HorizontalAlignment.Left, VerticalAlignment.Middle);
            spriteBatch.DrawStringOnCtrl(this, Subtitle ?? "", GameService.Content.DefaultFont12,
                new Rectangle(TextLeft, 21, textWidth, 16), SubtitleColor, false, HorizontalAlignment.Left, VerticalAlignment.Middle);
        }
    }
}
