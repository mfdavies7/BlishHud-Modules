using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Bytex.QuickItem
{
    /// <summary>
    /// Screenshots of the game's client area: preferably the game window's own picture, without anything drawn over it,
    /// or else a copy of the screen there, which includes Blish HUD's overlay.
    /// </summary>
    internal static class ScreenGrab
    {
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }

        /// <summary>The game's client area in screen pixels.</summary>
        public static bool TryGetClientArea(IntPtr window, out Rectangle area)
        {
            area = Rectangle.Empty;
            if (window == IntPtr.Zero || !GetClientRect(window, out var rect)) return false;
            var origin = new NativePoint();
            if (!ClientToScreen(window, ref origin)) return false;
            area = new Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return area.Width > 0 && area.Height > 0;
        }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);

        // Also renders DirectX content (Windows 8.1 and later), which a plain PrintWindow leaves black.
        private const uint PwRenderFullContent = 2;

        /// <summary>
        /// The game window's own picture of its client area: only the game, without Blish HUD or anything else on top.
        /// Null if Windows couldn't provide it, or gave back a blank (all one colour) picture, as it can for some
        /// DirectX windows.
        /// </summary>
        public static BgraImage CaptureWindow(IntPtr window, Rectangle clientArea)
        {
            if (!GetWindowRect(window, out var rect)) return null;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return null;

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

                // The picture covers the whole window, frame included; keep the client area.
                var client = new Rectangle(clientArea.X - rect.Left, clientArea.Y - rect.Top, clientArea.Width, clientArea.Height);
                client.Intersect(new Rectangle(0, 0, width, height));
                if (client.Width <= 0 || client.Height <= 0) return null;
                using (var cropped = whole.Clone(client, PixelFormat.Format32bppArgb))
                {
                    var image = FromBitmap(cropped, opaque: true);
                    return IsBlank(image) ? null : image;
                }
            }
        }

        /// <summary>All (nearly) one colour, as a failed window capture is. A game frame never is.</summary>
        private static bool IsBlank(BgraImage image)
        {
            var p = image.Pixels;
            int min = 255 * 3, max = 0;
            // Every 97th pixel: a prime step, so it doesn't line up with rows.
            for (int i = 0; i < p.Length; i += 97 * 4)
            {
                int brightness = p[i] + p[i + 1] + p[i + 2];
                if (brightness < min) min = brightness;
                if (brightness > max) max = brightness;
            }
            return max - min < 24;
        }

        /// <summary>A copy of the screen over the area: everything that's drawn there, Blish HUD included.</summary>
        public static BgraImage Capture(Rectangle area)
        {
            using (var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(area.X, area.Y, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
                return FromBitmap(bitmap, opaque: true);
            }
        }

        /// <summary>Decodes an image file (the item icons are PNGs).</summary>
        public static BgraImage Decode(byte[] file)
        {
            using (var stream = new MemoryStream(file))
            using (var decoded = new Bitmap(stream))
            using (var bitmap = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.DrawImage(decoded, 0, 0, decoded.Width, decoded.Height);
                }
                return FromBitmap(bitmap, opaque: false);
            }
        }

        private static BgraImage FromBitmap(Bitmap bitmap, bool opaque)
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = bitmap.Width * 4;
                var pixels = new byte[rowBytes * bitmap.Height];
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
                // A screen copy's alpha isn't meaningful.
                if (opaque) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                return new BgraImage(bitmap.Width, bitmap.Height, pixels);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>Saves a screenshot as PNG with a box around where the icon was best matched, for Save Search Screenshots.</summary>
        public static void SaveWithMatch(BgraImage image, IconMatch? match, string path)
        {
            using (var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < image.Height; y++)
                    Marshal.Copy(image.Pixels, y * image.Width * 4, data.Scan0 + y * data.Stride, image.Width * 4);
                bitmap.UnlockBits(data);

                if (match is IconMatch m)
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    using (var pen = new Pen(Color.Red, 3))
                    using (var font = new Font("Arial", 14))
                    {
                        graphics.DrawRectangle(pen, m.X - m.Size / 2, m.Y - m.Size / 2, m.Size, m.Size);
                        graphics.DrawString($"score {m.Score:F3}", font, Brushes.Red, m.X - m.Size / 2, m.Y + m.Size / 2 + 2);
                    }
                }
                bitmap.Save(path, ImageFormat.Png);
            }
        }
    }
}
