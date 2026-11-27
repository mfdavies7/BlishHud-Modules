using System;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// Tooltip-style hint that follows the cursor: icon, gold title, description.
    /// Drawn as one control that never captures input, so it can't block clicks to the game.
    /// </summary>
    internal class HintBox : Control
    {
        // The same sizes as Tyria Search's and the game's own tooltips: a 32 px icon beside the title in the large
        // font, and the description below in the body font. The box shrinks to fit its text, wrapping at MaxContentWidth.
        private const int MaxContentWidth = 320;
        private const int Spacing = 8;
        private const int BorderWidth = 2;
        private const int IconSize = 32;
        private const int IconGap = 6;
        private const int DescriptionGap = 6;

        // GW2 game asset: dark tooltip background.
        private static readonly AsyncTexture2D BackgroundTexture = AsyncTexture2D.FromAssetId(156003);

        private static BitmapFont TitleFont => GameService.Content.DefaultFont18;
        private static BitmapFont TextFont => GameService.Content.DefaultFont14;

        // Space between the cursor and the box.
        private const int CursorOffset = 25;

        private AsyncTexture2D _icon;
        private string _title;
        private string _description;
        private string _wrapped;
        private int _headerHeight;

        public HintBox(AsyncTexture2D icon, string title, string description)
        {
            _icon = icon;
            _title = title;
            Description = description;
        }

        /// <summary>The picture beside the title, e.g. an item's icon while one is chosen.</summary>
        public AsyncTexture2D Icon
        {
            get => _icon;
            set => _icon = value;
        }

        /// <summary>The title's colour: gold, or an item's rarity colour.</summary>
        public Color TitleColor { get; set; } = ContentService.Colors.Chardonnay;

        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                Description = _description; // the width depends on the title too
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                _wrapped = DrawUtil.WrapText(TextFont, _description, MaxContentWidth);
                Layout();
            }
        }

        private void Layout()
        {
            _headerHeight = Math.Max(IconSize, TitleFont.LineHeight);
            var textSize = TextFont.MeasureString(_wrapped ?? "");
            int textHeight = (int)Math.Ceiling(textSize.Height);
            // Only as wide as the header or the longest description line needs, up to MaxContentWidth.
            int headerWidth = IconSize + IconGap + (int)Math.Ceiling(TitleFont.MeasureString(_title ?? "").Width);
            int contentWidth = Math.Min(MaxContentWidth, Math.Max(headerWidth, (int)Math.Ceiling(textSize.Width)));
            int inset = BorderWidth + Spacing;
            Size = new Point(inset * 2 + contentWidth, inset * 2 + _headerHeight + DescriptionGap + textHeight);
        }

        protected override CaptureType CapturesInput() => CaptureType.None;

        // Blish Icon Manager draws every corner icon (and its folders) on this one control, which takes the mouse
        // only over what it draws. Matched by name, as it's another module's class.
        private const string IconManagerLayer = "Bytex.BlishIconManager.IconLayer";

        /// <summary>
        /// Whether a hint should hide because the cursor is over Blish HUD: always over a corner icon (Blish HUD's own
        /// or Blish Icon Manager's), and over any other of its controls (windows, menus, other modules' panels) when
        /// <paramref name="overWindows"/>. The hint never captures input, so it's never the active control itself.
        /// </summary>
        public static bool HideForCursor(bool overWindows)
        {
            var active = GameService.Input.Mouse.ActiveControl;
            if (active == null) return false;
            return overWindows || active is CornerIcon || active.GetType().FullName == IconManagerLayer;
        }

        /// <summary>Puts the box beside the cursor, flipped to its other side near the screen's right or bottom edge.</summary>
        public void PlaceNearCursor()
        {
            var mouse = GameService.Input.Mouse.Position;
            var screen = GameService.Graphics.SpriteScreen;
            int x = mouse.X + CursorOffset + Width > screen.Width ? mouse.X - CursorOffset - Width : mouse.X + CursorOffset;
            int y = mouse.Y + CursorOffset + Height > screen.Height ? mouse.Y - CursorOffset - Height : mouse.Y + CursorOffset;
            Location = new Point(x, y);
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            int inset = BorderWidth + Spacing;

            // Crop a piece of the 1024x1024 texture instead of squashing it into the box.
            spriteBatch.DrawOnCtrl(this, BackgroundTexture, bounds, new Rectangle(50, 50, bounds.Width, bounds.Height), Color.White);

            var iconRect = new Rectangle(inset, inset + (_headerHeight - IconSize) / 2, IconSize, IconSize);
            if (_icon != null) spriteBatch.DrawOnCtrl(this, _icon, iconRect);

            int titleX = iconRect.Right + IconGap;
            int titleY = inset + (_headerHeight - TitleFont.LineHeight) / 2;
            var titleRect = new Rectangle(titleX, titleY, bounds.Width - titleX, TitleFont.LineHeight);
            int textY = inset + _headerHeight + DescriptionGap;
            var textRect = new Rectangle(inset, textY, bounds.Width - inset, bounds.Height - textY);
            DrawShadowedString(spriteBatch, _title, TitleFont, titleRect, TitleColor);
            DrawShadowedString(spriteBatch, _wrapped, TextFont, textRect, Color.White);

            DrawBorder(spriteBatch, bounds.Size, Point.Zero, BorderWidth, Color.Black);
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
