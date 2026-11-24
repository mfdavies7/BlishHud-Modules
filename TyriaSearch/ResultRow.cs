using System;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bytex.TyriaSearch
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
        /// <summary>
        /// Makes the row's tooltip the first time the mouse enters it, for tooltips that fetch what they show: making
        /// one for every row would fetch for every result.
        /// </summary>
        public Func<Tooltip> TooltipFactory { get; set; }

        public ResultRow(SearchItem item)
        {
            Item = item;
            Height = RowHeight;
        }

        protected override void OnMouseEntered(MouseEventArgs e)
        {
            // Blish HUD reads the hovered control's tooltip on each mouse move, so this one shows from the next.
            if (TooltipFactory != null)
            {
                Tooltip = TooltipFactory();
                TooltipFactory = null;
            }
            base.OnMouseEntered(e);
        }

        protected override void DisposeControl()
        {
            // Rows are replaced on every search; their tooltips (and what they loaded) go with them.
            var tooltip = Tooltip;
            Tooltip = null;
            tooltip?.Dispose();
            base.DisposeControl();
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (MouseOver) spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, bounds, HoverColor);

            var iconRect = new Rectangle(8, (Height - IconSize) / 2, IconSize, IconSize);
            // The placeholder shows while the icon downloads, and instead of Blish HUD's error texture if that failed.
            if (TyriaSearchModule.IsLoaded(Icon)) spriteBatch.DrawOnCtrl(this, Icon, iconRect);
            else if (PlaceholderIcon != null) spriteBatch.DrawOnCtrl(this, PlaceholderIcon, iconRect);

            int textWidth = Width - TextLeft - 4;
            spriteBatch.DrawStringOnCtrl(this, Title ?? "", GameService.Content.DefaultFont16,
                new Rectangle(TextLeft, 2, textWidth, 20), TitleColor, false, HorizontalAlignment.Left, VerticalAlignment.Middle);
            spriteBatch.DrawStringOnCtrl(this, Subtitle ?? "", GameService.Content.DefaultFont12,
                new Rectangle(TextLeft, 21, textWidth, 16), SubtitleColor, false, HorizontalAlignment.Left, VerticalAlignment.Middle);
        }
    }
}
