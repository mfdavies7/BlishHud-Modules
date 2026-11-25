using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Bytex.ModuleShared;

namespace Bytex.QuickItem
{
    /// <summary>
    /// Item icons as pixels, for finding them on screen. Downloaded once from the render service and saved in the
    /// module's icons folder. (Blish HUD's own asset cache gives textures for drawing, which live on the graphics card.)
    /// </summary>
    internal sealed class IconCache : IDisposable
    {
        private static readonly Logger Logger = Logger.GetLogger<IconCache>();

        private readonly string _directory;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private readonly ConcurrentDictionary<int, BgraImage> _decoded = new ConcurrentDictionary<int, BgraImage>();

        public IconCache(string directory)
        {
            _directory = Path.Combine(directory, "icons");
            Directory.CreateDirectory(_directory);
        }

        public async Task<BgraImage> GetAsync(BarItem item, CancellationToken ct)
        {
            int assetId = item.AssetId;
            if (assetId == 0) throw new AbortException("This item has no icon to look for. Choose it again.");
            if (_decoded.TryGetValue(assetId, out var cached)) return cached;

            string path = Path.Combine(_directory, assetId + ".png");
            byte[] file = null;
            if (File.Exists(path))
            {
                try { file = File.ReadAllBytes(path); }
                catch (IOException ex) { Logger.Warn(ex, $"Couldn't read {path}; downloading it again."); }
            }

            if (file == null)
            {
                try
                {
                    using (var response = await _http.GetAsync(item.Icon, ct))
                    {
                        response.EnsureSuccessStatusCode();
                        file = await response.Content.ReadAsByteArrayAsync();
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is WebException)
                {
                    Logger.Warn(ex, $"Couldn't download the icon {item.Icon}.");
                    Blocked.Check(ex);
                    throw new AbortException("Couldn't download the item's icon. Try again.");
                }

                try { File.WriteAllBytes(path, file); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Still usable this session.
                    Logger.Warn(ex, $"Couldn't save {path}.");
                    Blocked.Check(ex, path);
                }
            }

            BgraImage image;
            try
            {
                image = ScreenGrab.Decode(file);
            }
            catch (ArgumentException ex)
            {
                // Not an image (a cut-off download): drop it so the next try downloads it again.
                Logger.Warn(ex, $"The icon {path} couldn't be read.");
                try { File.Delete(path); } catch (Exception) { }
                throw new AbortException("Couldn't read the item's icon. Try again.");
            }
            _decoded[assetId] = image;
            return image;
        }

        public void Dispose() => _http.Dispose();
    }
}
