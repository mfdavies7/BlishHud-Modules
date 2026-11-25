using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Bytex.QuickItem
{
    /// <summary>An image as 32-bit BGRA pixels, rows packed (stride = width * 4).</summary>
    public sealed class BgraImage
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        public BgraImage(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }
    }

    /// <summary>Where an icon was found: the centre of the slot, in the image's pixels, and how far off the match is (0 is exact).</summary>
    public readonly struct IconMatch
    {
        public readonly int X, Y, Size;
        public readonly double Score;

        public IconMatch(int x, int y, int size, double score)
        {
            X = x;
            Y = y;
            Size = size;
            Score = score;
        }
    }

    /// <summary>
    /// Finds an item's icon in a screenshot of the game, replacing the original module's native OpenCV library.
    /// The icon is matched in colour (so recoloured variants of one picture, like the three catalysts, aren't confused)
    /// with zero-mean normalised correlation, which ignores the inventory's slight darkening of icons.
    /// It's searched coarsely in a shrunken copy of the screenshot first, then the best few spots are checked at full
    /// size: the position at the expected icon size, then a few sizes around it close to that position.
    /// </summary>
    public static class IconFinder
    {
        // Icon size in the coarse search, in pixels: small enough to be fast, big enough to tell icons apart.
        private const int CoarseIconSize = 14;
        private const int Candidates = 12;
        // Once the position is found at the expected size, other sizes are tried this many pixels around it: a size a
        // few pixels off moves the best position by a pixel or so.
        private const int SizeSearchRadius = 2;

        // Templates by icon, size and mask: the same icon is searched for again and again during one use.
        private static readonly ConditionalWeakTable<BgraImage, ConcurrentDictionary<(int size, bool mask), Template>> Templates =
            new ConditionalWeakTable<BgraImage, ConcurrentDictionary<(int, bool), Template>>();

        /// <summary>
        /// The best match for <paramref name="icon"/> (the item's 64 px icon from the API) drawn at about
        /// <paramref name="expectedSize"/> pixels, or null if nothing looks like it at all.
        /// </summary>
        /// <param name="slotMask">Leave out what an inventory slot draws over an icon (rarity frame, stack count).
        /// Off for pictures that aren't in a slot.</param>
        /// <param name="sizeSpread">How far off the expected size the picture may be, as a share of it.</param>
        /// <param name="goodEnough">A match scoring this or better is taken without checking the other candidates.
        /// The most likely candidate is checked first, so this rarely changes the answer and saves most of the time.</param>
        public static IconMatch? Find(BgraImage screen, BgraImage icon, int expectedSize, bool slotMask = true, double sizeSpread = 0.05,
            double goodEnough = -1)
        {
            if (expectedSize < 16 || screen.Width < expectedSize || screen.Height < expectedSize) return null;

            // Coarse: both shrunk by the same factor.
            double factor = Math.Min(1.0, (double)CoarseIconSize / expectedSize);
            int smallWidth = Math.Max(1, (int)Math.Round(screen.Width * factor));
            int smallHeight = Math.Max(1, (int)Math.Round(screen.Height * factor));
            var smallScreen = Resample(screen, smallWidth, smallHeight);
            var coarse = TemplateFor(icon, Math.Max(4, (int)Math.Round(expectedSize * factor)), slotMask);
            var candidates = CoarseCandidates(smallScreen, smallWidth, smallHeight, coarse);
            if (candidates.Count == 0) return null;

            // Fine: around each candidate at full size. Read straight from the screenshot's bytes, as a float copy of
            // a whole 1440p screen would be 44 MB.
            // The expected size can be a pixel or two off (rounding, DPI scaling), more so for big icons.
            int spread = Math.Max(2, (int)Math.Round(expectedSize * sizeSpread));
            var templates = Enumerable.Range(expectedSize - spread, spread * 2 + 1)
                .Where(s => s >= 16).Select(s => TemplateFor(icon, s, slotMask)).ToArray();
            var expected = TemplateFor(icon, expectedSize, slotMask);
            var offsets = templates.Select(t => t.ScreenOffsets(screen.Width)).ToArray();
            var expectedOffsets = expected.ScreenOffsets(screen.Width);
            int radius = (int)Math.Ceiling(1 / factor) + 2;

            IconMatch? Refine((int x, int y, double score) candidate)
            {
                int cx = (int)Math.Round((candidate.x + coarse.Size / 2.0) / factor);
                int cy = (int)Math.Round((candidate.y + coarse.Size / 2.0) / factor);
                // Where it is, at the expected size; then what size it is, around there.
                var best = Around(screen, expected, expectedOffsets, cx, cy, radius, null);
                if (best == null) return null;
                for (int i = 0; i < templates.Length; i++)
                    best = Around(screen, templates[i], offsets[i], best.Value.X, best.Value.Y, SizeSearchRadius, best);
                return best;
            }

            // The likeliest candidate first: it's almost always the item.
            var first = Refine(candidates[0]);
            if (first != null && first.Value.Score <= goodEnough) return first;

            var bests = new IconMatch?[candidates.Count];
            bests[0] = first;
            Parallel.For(1, candidates.Count, index => bests[index] = Refine(candidates[index]));
            return bests.Where(b => b != null).OrderBy(b => b.Value.Score).FirstOrDefault();
        }

        /// <summary>The best match of the template centred within <paramref name="radius"/> of a spot, or <paramref name="best"/> if none beats it.</summary>
        private static IconMatch? Around(BgraImage screen, Template template, int[] offsets, int cx, int cy, int radius, IconMatch? best)
        {
            int half = template.Size / 2;
            for (int y = cy - half - radius; y <= cy - half + radius; y++)
            {
                if (y < 0 || y + template.Size > screen.Height) continue;
                for (int x = cx - half - radius; x <= cx - half + radius; x++)
                {
                    if (x < 0 || x + template.Size > screen.Width) continue;
                    double score = Score(screen.Pixels, (y * screen.Width + x) * 4, offsets, template);
                    if (best == null || score < best.Value.Score)
                        best = new IconMatch(x + half, y + half, template.Size, score);
                }
            }
            return best;
        }

        /// <summary>
        /// How far off <paramref name="icon"/> drawn at <paramref name="size"/> pixels, centred on the given spot, is
        /// from the screen there, scored as in <see cref="Find"/>: a quick check where something was found before.
        /// </summary>
        public static double ScoreAt(BgraImage screen, BgraImage icon, int centreX, int centreY, int size, bool slotMask = true)
        {
            if (size < 16) return 1;
            int left = centreX - size / 2, top = centreY - size / 2;
            if (left < 0 || top < 0 || left + size > screen.Width || top + size > screen.Height) return 1;
            var template = TemplateFor(icon, size, slotMask);
            return Score(screen.Pixels, (top * screen.Width + left) * 4, template.ScreenOffsets(screen.Width), template);
        }

        private static Template TemplateFor(BgraImage icon, int size, bool slotMask) =>
            Templates.GetOrCreateValue(icon).GetOrAdd((size, slotMask), key => Template.Create(icon, key.size, key.mask));

        private static List<(int x, int y, double score)> CoarseCandidates(float[] screen, int width, int height, Template template)
        {
            int columns = width - template.Size + 1, rows = height - template.Size + 1;
            if (columns <= 0 || rows <= 0) return new List<(int, int, double)>();
            var offsets = template.ScreenOffsets(width, channels: 3);
            var scores = new double[columns * rows];
            Parallel.For(0, rows, y =>
            {
                for (int x = 0; x < columns; x++) scores[y * columns + x] = Score(screen, (y * width + x) * 3, offsets, template);
            });

            // The best spots, at least half an icon apart.
            var order = new int[scores.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(scores, order);
            var picked = new List<(int x, int y, double score)>();
            int spacing = Math.Max(2, template.Size / 2);
            for (int k = 0; k < order.Length && picked.Count < Candidates; k++)
            {
                int x = order[k] % columns, y = order[k] / columns;
                if (picked.Any(p => Math.Abs(p.x - x) < spacing && Math.Abs(p.y - y) < spacing)) continue;
                picked.Add((x, y, scores[k]));
            }
            return picked;
        }

        /// <summary>
        /// 1 − the zero-mean normalised correlation of the masked pixels (all three channels together):
        /// 0 is a perfect match, 1 unrelated, 2 inverted. A flat patch of screen scores 1.
        /// </summary>
        /// <param name="origin">Where the template's top left falls in <paramref name="screen"/>.</param>
        /// <param name="offsets">Each masked pixel's place from there (<see cref="Template.ScreenOffsets"/>).</param>
        private static double Score(float[] screen, int origin, int[] offsets, Template template)
        {
            double sum = 0, sumSquares = 0, cross = 0;
            var values = template.Values;
            for (int k = 0, t = 0; k < offsets.Length; k++, t += 3)
            {
                int s = origin + offsets[k];
                float b = screen[s], g = screen[s + 1], r = screen[s + 2];
                sum += b + g + r;
                sumSquares += b * b + g * g + r * r;
                cross += b * values[t] + g * values[t + 1] + r * values[t + 2];
            }

            // The template's values are already zero-mean with unit norm, so only the screen side needs centring.
            double variance = sumSquares - sum * sum / template.Count;
            if (variance <= 1e-6) return 1;
            return 1 - cross / Math.Sqrt(variance);
        }

        /// <summary>As <see cref="Score(float[], int, int[], Template)"/>, reading a BGRA screenshot directly.</summary>
        private static double Score(byte[] screen, int origin, int[] offsets, Template template)
        {
            // Whole numbers for the screen side: exact, and quicker than floating point.
            long sum = 0, sumSquares = 0;
            double cross = 0;
            var values = template.Values;
            for (int k = 0, t = 0; k < offsets.Length; k++, t += 3)
            {
                int s = origin + offsets[k];
                int b = screen[s], g = screen[s + 1], r = screen[s + 2];
                sum += b + g + r;
                sumSquares += b * b + g * g + r * r;
                cross += b * values[t] + g * values[t + 1] + r * values[t + 2];
            }

            double variance = sumSquares - (double)sum * sum / template.Count;
            if (variance <= 1e-6) return 1;
            return 1 - cross / Math.Sqrt(variance);
        }

        /// <summary>An icon at one size: zero-mean, unit-norm colour values for the pixels that count.</summary>
        private sealed class Template
        {
            public int Size;
            /// <summary>Three colour values for each masked pixel, in <see cref="Pixels"/> order.</summary>
            public float[] Values;
            /// <summary>The masked pixels, as y * size + x.</summary>
            public int[] Pixels;
            public int Count; // masked pixels × 3 channels

            // The last offsets worked out, as one object so another thread never sees half of it.
            private Tuple<int, int, int[]> _offsets;

            /// <summary>Each masked pixel's place in an image of this width, from where the template's top left falls.</summary>
            public int[] ScreenOffsets(int width, int channels = 4)
            {
                var cached = _offsets;
                if (cached != null && cached.Item1 == width && cached.Item2 == channels) return cached.Item3;
                var offsets = new int[Pixels.Length];
                for (int k = 0; k < offsets.Length; k++)
                    offsets[k] = (Pixels[k] / Size * width + Pixels[k] % Size) * channels;
                _offsets = Tuple.Create(width, channels, offsets);
                return offsets;
            }

            public static Template Create(BgraImage icon, int size, bool slotMask)
            {
                var color = Resample(ToFloat(icon), icon.Width, icon.Height, size, size);
                var alpha = Resample(AlphaToFloat(icon), icon.Width, icon.Height, size, size, channels: 1);
                var mask = slotMask ? SlotMask(size) : Enumerable.Repeat(true, size * size).ToArray();
                for (int i = 0; i < mask.Length; i++) mask[i] &= alpha[i] > 250; // see-through edges show the slot behind

                var pixels = Enumerable.Range(0, mask.Length).Where(i => mask[i]).ToArray();
                double mean = 0;
                foreach (int i in pixels) mean += color[i * 3] + color[i * 3 + 1] + color[i * 3 + 2];
                mean /= Math.Max(1, pixels.Length * 3);

                var values = new float[pixels.Length * 3];
                double norm = 0;
                for (int k = 0; k < pixels.Length; k++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        float v = (float)(color[pixels[k] * 3 + c] - mean);
                        values[k * 3 + c] = v;
                        norm += v * v;
                    }
                }
                float scale = norm > 0 ? (float)(1 / Math.Sqrt(norm)) : 0;
                for (int i = 0; i < values.Length; i++) values[i] *= scale;

                return new Template { Size = size, Values = values, Pixels = pixels, Count = pixels.Length * 3 };
            }
        }

        /// <summary>
        /// The part of a slot that shows only the icon: not the rarity frame around the edge, nor the top right,
        /// where the game writes the stack count.
        /// </summary>
        private static bool[] SlotMask(int size)
        {
            var mask = new bool[size * size];
            int border = Math.Max(2, (int)Math.Round(size * 0.07));
            int countLeft = (int)Math.Round(size * 0.40), countBottom = (int)Math.Round(size * 0.32);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    mask[y * size + x] = x >= border && y >= border && x < size - border && y < size - border
                                         && !(x >= countLeft && y < countBottom);
            return mask;
        }

        private static float[] ToFloat(BgraImage image)
        {
            var result = new float[image.Width * image.Height * 3];
            var p = image.Pixels;
            for (int i = 0, j = 0; i < result.Length; i += 3, j += 4)
            {
                result[i] = p[j];
                result[i + 1] = p[j + 1];
                result[i + 2] = p[j + 2];
            }
            return result;
        }

        private static float[] AlphaToFloat(BgraImage image)
        {
            var result = new float[image.Width * image.Height];
            for (int i = 0; i < result.Length; i++) result[i] = image.Pixels[i * 4 + 3];
            return result;
        }

        /// <summary>Shrinks a BGRA screenshot to a three-channel float image, without a full-size float copy first.</summary>
        private static float[] Resample(BgraImage image, int newWidth, int newHeight)
        {
            int width = image.Width, height = image.Height;
            var pixels = image.Pixels;
            var horizontal = new float[newWidth * height * 3];
            var xWeights = Weights(width, newWidth);
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < newWidth; x++)
                {
                    float b = 0, g = 0, r = 0;
                    foreach (var (index, weight) in xWeights[x])
                    {
                        int p = (y * width + index) * 4;
                        b += pixels[p] * weight;
                        g += pixels[p + 1] * weight;
                        r += pixels[p + 2] * weight;
                    }
                    int o = (y * newWidth + x) * 3;
                    horizontal[o] = b;
                    horizontal[o + 1] = g;
                    horizontal[o + 2] = r;
                }
            });
            return ResampleVertical(horizontal, newWidth, height, newHeight, 3);
        }

        /// <summary>Area-averaging resize (each output pixel is the mean of the input it covers), done one axis at a time.</summary>
        private static float[] Resample(float[] source, int width, int height, int newWidth, int newHeight, int channels = 3)
        {
            var horizontal = new float[newWidth * height * channels];
            var xWeights = Weights(width, newWidth);
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < newWidth; x++)
                    foreach (var (index, weight) in xWeights[x])
                        for (int c = 0; c < channels; c++)
                            horizontal[(y * newWidth + x) * channels + c] += source[(y * width + index) * channels + c] * weight;
            });

            return ResampleVertical(horizontal, newWidth, height, newHeight, channels);
        }

        private static float[] ResampleVertical(float[] horizontal, int newWidth, int height, int newHeight, int channels)
        {
            var result = new float[newWidth * newHeight * channels];
            var yWeights = Weights(height, newHeight);
            Parallel.For(0, newHeight, y =>
            {
                foreach (var (index, weight) in yWeights[y])
                    for (int x = 0; x < newWidth * channels; x++)
                        result[y * newWidth * channels + x] += horizontal[index * newWidth * channels + x] * weight;
            });
            return result;
        }

        /// <summary>For each output pixel, the input pixels it covers and how much of each (summing to 1). Also enlarges.</summary>
        private static List<(int index, float weight)>[] Weights(int from, int to)
        {
            var weights = new List<(int, float)>[to];
            double step = (double)from / to;
            for (int i = 0; i < to; i++)
            {
                weights[i] = new List<(int, float)>();
                double start = i * step, end = start + step;
                if (step < 1)
                {
                    // Enlarging: linear interpolation between the two nearest inputs.
                    double centre = Math.Max(0, Math.Min(from - 1, (i + 0.5) * step - 0.5));
                    int low = (int)Math.Floor(centre), high = Math.Min(from - 1, low + 1);
                    float t = (float)(centre - low);
                    weights[i].Add((low, 1 - t));
                    if (high != low) weights[i].Add((high, t));
                    continue;
                }
                for (int j = (int)Math.Floor(start); j < Math.Min(from, (int)Math.Ceiling(end)); j++)
                {
                    double covered = Math.Min(end, j + 1) - Math.Max(start, j);
                    if (covered > 0) weights[i].Add((j, (float)(covered / step)));
                }
            }
            return weights;
        }
    }
}
