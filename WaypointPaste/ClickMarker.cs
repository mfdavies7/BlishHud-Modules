using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bytex.WaypointPaste
{
    /// <summary>
    /// Crosshair centred on where Auto-Click Chat Link clicks. Never takes input, so it can't block the click
    /// or the chat link underneath. Position it with <see cref="CenterAt"/>, in UI units.
    /// </summary>
    internal sealed class ClickMarker : Control
    {
        private const int Arm = 10;      // length of each arm, from the gap outward
        private const int Gap = 3;       // empty space around the centre, so the link under it stays readable
        private const int Thickness = 2;
        private const int Outline = 1;

        private static readonly Color LineColor = new Color(255, 70, 70);

        private const int Half = Outline + Arm + Gap + Thickness / 2; // from the top left corner to the centre

        public ClickMarker()
        {
            int size = (Outline + Arm + Gap) * 2 + Thickness;
            Size = new Point(size, size);
        }

        public void CenterAt(Point center) => Location = new Point(center.X - Half, center.Y - Half);

        protected override CaptureType CapturesInput() => CaptureType.None;

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            int c = Outline + Arm + Gap; // left/top of the centre lines
            int after = c + Thickness + Gap; // where the bottom and right arms start
            var arms = new[]
            {
                new Rectangle(c, Outline, Thickness, Arm), // top
                new Rectangle(c, after, Thickness, Arm),   // bottom
                new Rectangle(Outline, c, Arm, Thickness), // left
                new Rectangle(after, c, Arm, Thickness),   // right
                new Rectangle(c, c, Thickness, Thickness), // centre dot
            };
            foreach (var arm in arms)
            {
                var outline = arm;
                outline.Inflate(Outline, Outline);
                spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, outline, Color.Black * 0.8f);
            }
            foreach (var arm in arms)
                spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, arm, LineColor);
        }
    }
}
