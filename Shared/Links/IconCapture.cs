using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Blish_HUD;
using Microsoft.Xna.Framework.Graphics;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// A picture of the inventory icon that was clicked, copied off the screen and cut to the icon itself rather than
    /// centred on the cursor: the slot's frame is found, and the picture is what's inside it.
    /// </summary>
    internal static class IconCapture
    {
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);

        // An inventory icon's size in pixels at each interface size (Small, Normal, Large, Larger), as Quick Item
        // measured them, before the game's DPI scaling.
        private static readonly int[] IconSizes = { 45, 50, 56, 62 };
        // Sizes either side of the expected one that are tried too.
        private const int SizeSlack = 3;
        // A slot is framed by two 1 px lines round the icon at Normal size (a grey or rarity-coloured line outside, a
        // dimmer one inside; gold for both while hovered), so the frame is the icon plus this each side, and the
        // frame lines' thickness grows with it.
        private const int FrameInset = 2;
        private const float FrameInsetPerPixel = FrameInset / 54f; // 54 = the frame at Normal size (50 + 2 × 2)
        // A square's sides must stand out as lines by this much (0–255) to count as the frame; otherwise the picture
        // is just centred on the cursor. Frames score 35–55; a frame partly covered or a stray square, under 20.
        private const float MinEdge = 12;

        /// <summary>The icon's expected size in screen pixels.</summary>
        public static int ExpectedSize(IntPtr window)
        {
            int index = Math.Max(0, Math.Min(IconSizes.Length - 1, (int)GameService.Gw2Mumble.UI.UISize));
            float scale = 1;
            try
            {
                // With the game's DPI Scaling option on, its interface is drawn bigger by the monitor's scale.
                if (GameService.GameIntegration.GfxSettings.DpiScaling == true && window != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(window);
                    if (dpi > 0) scale = dpi / 96f;
                }
            }
            catch (Exception) { } // GetDpiForWindow needs Windows 10 1607 or later
            return (int)Math.Round(IconSizes[index] * scale);
        }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);

        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

        // Also renders DirectX content (Windows 8.1 and later), which a plain PrintWindow leaves black.
        private const uint PwRenderFullContent = 2;

        private static readonly Logger Log = Logger.GetLogger(typeof(IconCapture));

        // After this many blank or failed window pictures in a row, the screen is copied instead for the rest of the
        // session, as Quick Item does.
        private const int MaxWindowCaptureFailures = 3;
        private static int _windowCaptureFailures;

        /// <summary>
        /// The icon under the cursor (screen pixels), as BGRA pixels with its width; null if it couldn't be copied.
        /// It's cut from the game window's own picture, which has nothing of Blish HUD in it. If Windows can't give that
        /// picture, the screen is copied instead, with the hint hidden for it (it's by the cursor, so it would be in the
        /// picture): <paramref name="hideHint"/> is called with true, then false once the copy is taken.
        /// </summary>
        public static async Task<(byte[] bgra, int size)?> CaptureAsync(IntPtr window, int cursorX, int cursorY, int iconSize,
            Action<bool> hideHint)
        {
            // Room for a frame of the largest size tried, wherever in it the cursor is.
            int big = iconSize + FrameInset * 2 + SizeSlack;
            int half = big + 4;
            int w = half * 2, h = half * 2;
            int x = cursorX - half, y = cursorY - half;

            byte[] pixels = null;
            if (_windowCaptureFailures < MaxWindowCaptureFailures)
            {
                pixels = CaptureWindowAround(window, x, y, w, h);
                if (pixels != null) _windowCaptureFailures = 0;
                else if (++_windowCaptureFailures == MaxWindowCaptureFailures)
                    Log.Info("The game window's own picture keeps coming back blank; copying the screen instead from now on.");
            }
            if (pixels == null)
            {
                hideHint(true);
                try
                {
                    await Task.Delay(50); // Blish HUD draws a frame or two without the hint
                    pixels = CaptureScreen(x, y, w, h);
                }
                finally
                {
                    hideHint(false);
                }
                if (pixels == null) return null;
            }

            return FindIcon(pixels, w, h, half, iconSize);
        }

        /// <summary>A copy of the screen over the area: everything that's drawn there, Blish HUD included.</summary>
        private static byte[] CaptureScreen(int x, int y, int w, int h)
        {
            try
            {
                using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                    var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var pixels = new byte[w * h * 4];
                    for (int row = 0; row < h; row++) Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * w * 4, w * 4);
                    bitmap.UnlockBits(data);
                    return pixels;
                }
            }
            catch (Exception)
            {
                return null; // e.g. the screen is locked
            }
        }

        /// <summary>The icon inside the slot frame around the middle of the copied area, where the cursor is.</summary>
        private static (byte[] bgra, int size) FindIcon(byte[] pixels, int w, int h, int half, int iconSize)
        {

            var grey = new float[w * h];
            for (int i = 0; i < grey.Length; i++) grey[i] = (pixels[i * 4] + pixels[i * 4 + 1] + pixels[i * 4 + 2]) / 3f;

            // The cursor is at the middle of the area. Try every square of about the frame's size that contains it, and
            // keep the one whose four sides are the clearest lines.
            int cx = half, cy = half;
            float bestScore = 0;
            int bestX = 0, bestY = 0, bestSize = 0;
            int frameSize = iconSize + FrameInset * 2;
            for (int size = frameSize - SizeSlack; size <= frameSize + SizeSlack; size++)
            {
                for (int top = cy - size + 1; top <= cy; top++)
                {
                    if (top < 1 || top + size >= h) continue;
                    for (int left = cx - size + 1; left <= cx; left++)
                    {
                        if (left < 1 || left + size >= w) continue;
                        float score = FrameScore(grey, w, left, top, size);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestX = left;
                            bestY = top;
                            bestSize = size;
                        }
                    }
                }
            }
            if (bestScore < MinEdge)
            {
                // No clear frame: centre on the cursor, as TyrianQoL does.
                bestSize = iconSize;
                bestX = cx - iconSize / 2;
                bestY = cy - iconSize / 2;
            }
            else
            {
                // Just the icon, without the frame's lines.
                int inset = Math.Max(FrameInset, (int)Math.Round(bestSize * FrameInsetPerPixel));
                bestX += inset;
                bestY += inset;
                bestSize -= inset * 2;
            }

            var icon = new byte[bestSize * bestSize * 4];
            for (int y = 0; y < bestSize; y++)
                Buffer.BlockCopy(pixels, ((bestY + y) * w + bestX) * 4, icon, y * bestSize * 4, bestSize * 4);
            return (icon, bestSize);
        }

        /// <summary>
        /// The area (screen pixels) of the game window's own picture, as QuickItem's ScreenGrab takes it: only the game,
        /// without Blish HUD's overlay (the hint by the cursor) or anything else on top, so nothing needs hiding first.
        /// Parts outside the window are black. Null if Windows couldn't give the picture, or gave a blank (all one
        /// colour) one, as it can for some DirectX windows.
        /// </summary>
        private static byte[] CaptureWindowAround(IntPtr window, int x, int y, int w, int h)
        {
            if (window == IntPtr.Zero || !GetWindowRect(window, out var rect)) return null;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return null;
            try
            {
                using (var whole = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(whole))
                    {
                        IntPtr hdc = graphics.GetHdc();
                        try
                        {
                            if (!PrintWindow(window, hdc, PwRenderFullContent)) return null;
                        }
                        finally
                        {
                            graphics.ReleaseHdc(hdc);
                        }
                    }

                    var data = whole.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        if (IsBlank(data, width, height)) return null;

                        // The area in the window's picture, and the part of it that's inside the window.
                        var area = new Rectangle(x - rect.Left, y - rect.Top, w, h);
                        var inside = Rectangle.Intersect(area, new Rectangle(0, 0, width, height));
                        var pixels = new byte[w * h * 4];
                        for (int row = inside.Top; row < inside.Bottom; row++)
                            Marshal.Copy(data.Scan0 + row * data.Stride + inside.Left * 4, pixels,
                                ((row - area.Top) * w + inside.Left - area.Left) * 4, inside.Width * 4);
                        // A window picture's alpha isn't meaningful.
                        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                        return pixels;
                    }
                    finally
                    {
                        whole.UnlockBits(data);
                    }
                }
            }
            catch (Exception)
            {
                return null; // e.g. the window closed
            }
        }

        /// <summary>All (nearly) one colour, as a failed window capture is. A game frame never is.</summary>
        private static bool IsBlank(BitmapData data, int width, int height)
        {
            int min = 255 * 3, max = 0;
            // Every 97th pixel: a prime step, so it doesn't line up with rows.
            for (long i = 0; i < (long)width * height; i += 97)
            {
                IntPtr pixel = data.Scan0 + (int)(i / width) * data.Stride + (int)(i % width) * 4;
                int brightness = Marshal.ReadByte(pixel) + Marshal.ReadByte(pixel, 1) + Marshal.ReadByte(pixel, 2);
                if (brightness < min) min = brightness;
                if (brightness > max) max = brightness;
            }
            return max - min < 24;
        }

        /// <summary>
        /// How clearly the square's sides are 1 px lines, as a slot frame's are: for each pixel of a side, the smaller of
        /// its brightness steps to the pixels just outside and just inside, averaged along the side. An icon's own edge
        /// is a step, not a line, so it scores low. The weakest side counts double, so a square needs all four.
        /// </summary>
        private static float FrameScore(float[] grey, int w, int left, int top, int size)
        {
            int right = left + size - 1, bottom = top + size - 1;
            float l = 0, r = 0, t = 0, b = 0;
            for (int i = 0; i < size; i++)
            {
                int y = top + i, x = left + i;
                l += Line(grey[y * w + left], grey[y * w + left - 1], grey[y * w + left + 1]);
                r += Line(grey[y * w + right], grey[y * w + right + 1], grey[y * w + right - 1]);
                t += Line(grey[top * w + x], grey[(top - 1) * w + x], grey[(top + 1) * w + x]);
                b += Line(grey[bottom * w + x], grey[(bottom + 1) * w + x], grey[(bottom - 1) * w + x]);
            }
            l /= size; r /= size; t /= size; b /= size;
            float weakest = Math.Min(Math.Min(l, r), Math.Min(t, b));
            return (l + r + t + b + weakest * 2) / 6;
        }

        private static float Line(float pixel, float outside, float inside) =>
            Math.Min(Math.Abs(pixel - outside), Math.Abs(pixel - inside));

        /// <summary>A texture of the captured pixels, for the hint. Can be called off the update thread.</summary>
        public static Texture2D ToTexture(byte[] bgra, int size)
        {
            // MonoGame's Color format is RGBA.
            var rgba = new byte[bgra.Length];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                rgba[i] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i];
                rgba[i + 3] = 255;
            }
            using (var context = GameService.Graphics.LendGraphicsDeviceContext())
            {
                var texture = new Texture2D(context.GraphicsDevice, size, size, false, SurfaceFormat.Color);
                texture.SetData(rgba);
                return texture;
            }
        }
    }
}
