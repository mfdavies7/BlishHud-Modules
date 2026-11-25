using System;
using System.Runtime.InteropServices;
using Blish_HUD;

namespace Bytex.QuickItem
{
    internal static class Dpi
    {
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);

        /// <summary>
        /// With the game's DPI Scaling option on, it draws its interface bigger by the monitor's scale (Windows display
        /// scaling). 1 with the option off.
        /// </summary>
        public static float GameScale(IntPtr window)
        {
            try
            {
                if (GameService.GameIntegration.GfxSettings.DpiScaling == true && window != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(window);
                    if (dpi > 0) return dpi / 96f;
                }
            }
            catch (Exception) { } // GetDpiForWindow needs Windows 10 1607 or later
            return 1;
        }
    }
}
