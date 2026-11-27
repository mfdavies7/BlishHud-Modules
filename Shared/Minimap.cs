using System;
using System.Runtime.InteropServices;
using Blish_HUD;
using Microsoft.Xna.Framework;

namespace Bytex.ModuleShared
{
    /// <summary>Where the game's minimap (compass) is, in Blish HUD's UI units.</summary>
    internal static class Minimap
    {
        // Between the bottom of the minimap and the bottom of the screen, in screen pixels.
        private const int BottomGap = 36;

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        private static float _dpiScale = 1;
        private static DateTime _dpiCheckedUtc;

        /// <summary>
        /// With the game's DPI Scaling option on, it draws the map and compass bigger by the monitor's scale (Windows
        /// display scaling), but Mumble's map scale and compass size don't include it. 1 with the option off.
        /// Checked once a second, since the game window can move to another monitor.
        /// </summary>
        public static float GameDpiScale()
        {
            if (DateTime.UtcNow - _dpiCheckedUtc < TimeSpan.FromSeconds(1)) return _dpiScale;
            _dpiCheckedUtc = DateTime.UtcNow;
            float scale = 1;
            try
            {
                var window = GameService.GameIntegration.Gw2Instance.Gw2WindowHandle;
                if (GameService.GameIntegration.GfxSettings.DpiScaling == true && window != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(window);
                    if (dpi > 0) scale = dpi / 96f;
                }
            }
            catch (Exception) { } // GetDpiForWindow needs Windows 10 1607 or later
            return _dpiScale = scale;
        }

        /// <summary>Screen pixels (as Mumble reports them) per UI unit.</summary>
        public static float PixelsPerUnit() => GameService.Graphics.UIScaleMultiplier / GameDpiScale();

        /// <summary>
        /// The compass in UI units. Mumble gives its size in screen pixels before DPI scaling; it sits at the right edge,
        /// either at the top or just above the bottom.
        /// </summary>
        public static Rectangle Bounds()
        {
            var ui = GameService.Gw2Mumble.UI;
            var screen = GameService.Graphics.SpriteScreen;
            float scale = PixelsPerUnit();
            int width = (int)(ui.CompassSize.Width / scale);
            int height = (int)(ui.CompassSize.Height / scale);
            int y = ui.IsCompassTopRight ? 0 : screen.Height - height - (int)(BottomGap / scale);
            return new Rectangle(screen.Width - width, y, width, height);
        }
    }
}
