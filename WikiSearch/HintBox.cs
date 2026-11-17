using System;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace WikiSearch
{
    /// <summary>
    /// Tooltip-style hint that follows the cursor: icon, gold title, description.
    /// Drawn as one control that never captures input, so it can't block clicks to the game.
    /// </summary>
    internal class HintBox : Control
    {
        private const int Spacing = 8;
        private const int BorderWidth = 2;
        private const int IconSize = 48;
        private const int TitleGap = 4;

        // GW2 game asset: dark tooltip background.
        private static readonly AsyncTexture2D BackgroundTexture = AsyncTexture2D.FromAssetId(156003);

        private static BitmapFont TitleFont => GameService.Content.DefaultFont18;
        private static BitmapFont TextFont => GameService.Content.DefaultFont16;

        private readonly Texture2D _icon;
        private readonly string _title;
        private string _description;
        private int _titleHeight;

        public HintBox(Texture2D icon, string title, string description)
        {
            _icon = icon;
            _title = title;
            Description = description;
        }

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                var titleSize = TitleFont.MeasureString(_title);
                var textSize = TextFont.MeasureString(_description);
                _titleHeight = (int)Math.Ceiling(titleSize.Height);
                int textWidth = (int)Math.Ceiling(Math.Max(titleSize.Width, textSize.Width));
                int textHeight = _titleHeight + TitleGap + (int)Math.Ceiling(textSize.Height);
                int inset = BorderWidth + Spacing;
                Size = new Point(inset * 2 + IconSize + Spacing + textWidth, inset * 2 + Math.Max(IconSize, textHeight));
            }
        }

        protected override CaptureType CapturesInput() => CaptureType.None;

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            int inset = BorderWidth + Spacing;

            // Crop a piece of the 1024x1024 texture instead of squashing it into the box.
            spriteBatch.DrawOnCtrl(this, BackgroundTexture, bounds, new Rectangle(50, 50, bounds.Width, bounds.Height), Color.White);

            var iconRect = new Rectangle(inset, inset, IconSize, IconSize);
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, iconRect, Color.Black * 0.5f);
            spriteBatch.DrawOnCtrl(this, _icon, iconRect);

            int textX = iconRect.Right + Spacing;
            var titleRect = new Rectangle(textX, inset, bounds.Width - textX, _titleHeight);
            var textRect = new Rectangle(textX, titleRect.Bottom + TitleGap, bounds.Width - textX, bounds.Height - titleRect.Bottom - TitleGap);
            DrawShadowedString(spriteBatch, _title, TitleFont, titleRect, ContentService.Colors.Chardonnay);
            DrawShadowedString(spriteBatch, _description, TextFont, textRect, Color.White);

            DrawBorder(spriteBatch, bounds.Size, Point.Zero, BorderWidth, Color.Black);
            DrawBorder(spriteBatch, iconRect.Size, iconRect.Location, 1, Color.Black * 0.8f);
        }

        private void DrawBorder(SpriteBatch spriteBatch, Point size, Point at, int w, Color color)
        {
            var pixel = ContentService.Textures.Pixel;
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(at.X, at.Y, size.X, w), color);
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(at.X, at.Y + size.Y - w, size.X, w), color);
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(at.X, at.Y, w, size.Y), color);
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(at.X + size.X - w, at.Y, w, size.Y), color);
        }

        private void DrawShadowedString(SpriteBatch spriteBatch, string text, BitmapFont font, Rectangle rect, Color color)
        {
            var shadow = rect;
            shadow.Offset(1, 1);
            spriteBatch.DrawStringOnCtrl(this, text, font, shadow, Color.Black, false, HorizontalAlignment.Left, VerticalAlignment.Top);
            spriteBatch.DrawStringOnCtrl(this, text, font, rect, color, false, HorizontalAlignment.Left, VerticalAlignment.Top);
        }
    }
}
