using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace Bytex.TyriaSearch
{
    /// <summary>A stretch of tooltip text in one colour.</summary>
    internal readonly struct TooltipRun
    {
        public readonly string Text;
        public readonly Color Color;
        public TooltipRun(string text, Color color)
        {
            Text = text;
            Color = color;
        }
    }

    /// <summary>One line (or wrapped paragraph) of a <see cref="TooltipContent"/>, with an optional icon before it.</summary>
    internal sealed class TooltipLine
    {
        public readonly List<TooltipRun> Runs = new List<TooltipRun>();
        public BitmapFont Font = GameService.Content.DefaultFont14;
        public AsyncTexture2D Icon;
        /// <summary>Drawn while <see cref="Icon"/> is still downloading.</summary>
        public Texture2D Placeholder;
        public int IconSize = 20;
        public int Indent;
        public int SpaceBefore;
        /// <summary>A thin rule instead of text.</summary>
        public bool Separator;
        /// <summary>A coin amount, in copper, instead of text.</summary>
        public int? Coins;
        /// <summary>Header only: values at the right, each followed by its icon, such as activation time and recharge.</summary>
        public readonly List<(string Text, AsyncTexture2D Icon)> Right = new List<(string, AsyncTexture2D)>();
        /// <summary>Drawn on the icon's lower left, as the game shows a buff's stacks; 0 or 1 for none.</summary>
        public int IconCount;

        public TooltipLine Add(string text, Color color)
        {
            if (!string.IsNullOrEmpty(text)) Runs.Add(new TooltipRun(text, color));
            return this;
        }
    }

    /// <summary>
    /// The body of a game-style tooltip, drawn in one control: lines of coloured, wrapped text with icons, as the
    /// game lays out skill, trait and item tooltips. Sized to fit its lines at a fixed width.
    /// </summary>
    internal sealed class TooltipContent : Control
    {
        private const int ContentWidth = 320;
        private const int IconGap = 6;
        private const int CoinIconSize = 16;
        private static readonly Color SeparatorColor = new Color(255, 255, 255, 50);

        // /v2/files ui_coin_gold, ui_coin_silver and ui_coin_copper.
        private const string GoldIcon = "https://render.guildwars2.com/file/090A980A96D39FD36FBB004903644C6DBEFB1FFB/156904.png";
        private const string SilverIcon = "https://render.guildwars2.com/file/E5A2197D78ECE4AE0349C8B3710D033D22DB0DA6/156907.png";
        private const string CopperIcon = "https://render.guildwars2.com/file/6CF8F96A3299CFC75D5CC90617C3C70331A1EF0E/156902.png";

        private sealed class Word
        {
            public string Text;
            public Color Color;
            public Rectangle Bounds;
        }

        private sealed class Placed
        {
            public TooltipLine Line;
            public int Top, Height, IconTop;
            public readonly List<Word> Words = new List<Word>();
        }

        private readonly List<Placed> _placed = new List<Placed>();
        private readonly Func<string, AsyncTexture2D> _renderIcon;
        // Kept while the tooltip shows: Blish HUD holds them weakly, and asking each frame gave a fresh texture after a
        // collection, transparent until reloaded, so the coins flickered.
        private readonly Dictionary<string, AsyncTexture2D> _coinIcons = new Dictionary<string, AsyncTexture2D>();

        /// <param name="renderIcon">A render service icon by URL (see <see cref="TyriaSearchModule.RenderIcon"/>), for the coins.</param>
        public TooltipContent(IEnumerable<TooltipLine> lines, Func<string, AsyncTexture2D> renderIcon)
        {
            _renderIcon = renderIcon;
            Width = ContentWidth;
            int y = 0;
            foreach (var line in lines)
            {
                if (_placed.Count > 0) y += line.SpaceBefore;
                var placed = new Placed { Line = line, Top = y };
                if (line.Separator) placed.Height = 9;
                else if (line.Coins.HasValue) placed.Height = CoinIconSize + 2;
                else
                {
                    bool hasIcon = line.Icon != null || line.Placeholder != null;
                    int left = line.Indent + (hasIcon ? line.IconSize + IconGap : 0);
                    int right = ContentWidth - RightWidth(line);
                    int rows = Wrap(line, left, right, placed.Words);
                    int textHeight = rows * line.Font.LineHeight;
                    placed.Height = Math.Max(hasIcon ? line.IconSize : 0, textHeight);
                    // Text beside a taller icon is centred on it; an icon beside several lines sits at the top.
                    int textOffset = (placed.Height - textHeight) / 2;
                    foreach (var word in placed.Words) word.Bounds.Y += textOffset;
                    placed.IconTop = rows > 1 ? 0 : (placed.Height - line.IconSize) / 2;
                }
                _placed.Add(placed);
                y += placed.Height;
            }
            Height = y;
        }

        /// <summary>The recharge icon at the right of a header, about the height of its text, as in game.</summary>
        private const int RightIconSize = 16;

        private const int RightGap = 10;

        private static int RightWidth(TooltipLine line) =>
            line.Right.Count == 0 ? 0 : line.Right.Sum(part => RightPartWidth(line.Font, part)) + RightGap * line.Right.Count;

        private static int RightPartWidth(BitmapFont font, (string Text, AsyncTexture2D Icon) part) =>
            Measure(font, part.Text) + (part.Icon != null ? 3 + RightIconSize : 0);

        private static int Measure(BitmapFont font, string text) => (int)Math.Ceiling(font.MeasureString(text).Width);

        /// <summary>Lays the line's words out between <paramref name="left"/> and <paramref name="right"/>; returns the number of rows.</summary>
        private static int Wrap(TooltipLine line, int left, int right, List<Word> words)
        {
            var font = line.Font;
            int space = Math.Max(3, Measure(font, "a a") - Measure(font, "aa"));
            int x = left, row = 0;
            foreach (var run in line.Runs)
            foreach (Match token in Regex.Matches(run.Text, @"\n| +|[^ \n]+"))
            {
                if (token.Value == "\n")
                {
                    row++;
                    x = left;
                }
                else if (token.Value[0] == ' ')
                {
                    if (x > left) x += space;
                }
                else
                {
                    int width = Measure(font, token.Value);
                    if (x > left && x + width > right)
                    {
                        row++;
                        x = left;
                    }
                    words.Add(new Word
                    {
                        Text = token.Value, Color = run.Color,
                        Bounds = new Rectangle(x, row * font.LineHeight, width + 4, font.LineHeight),
                    });
                    x += width;
                }
            }
            return row + 1;
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            foreach (var placed in _placed)
            {
                var line = placed.Line;
                if (line.Separator)
                {
                    spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, new Rectangle(0, placed.Top + 4, Width, 1), SeparatorColor);
                    continue;
                }
                if (line.Coins.HasValue)
                {
                    PaintCoins(spriteBatch, line, placed.Top);
                    continue;
                }

                var iconRect = new Rectangle(line.Indent, placed.Top + placed.IconTop, line.IconSize, line.IconSize);
                // Until a downloading icon arrives the placeholder shows, and stays if Blish HUD's download failed.
                if (TyriaSearchModule.IsLoaded(line.Icon)) spriteBatch.DrawOnCtrl(this, line.Icon, iconRect);
                else if (line.Placeholder != null) spriteBatch.DrawOnCtrl(this, line.Placeholder, iconRect);

                foreach (var word in placed.Words)
                {
                    var rect = word.Bounds;
                    rect.Y += placed.Top;
                    spriteBatch.DrawStringOnCtrl(this, word.Text, line.Font, rect, word.Color, false, HorizontalAlignment.Left, VerticalAlignment.Top);
                }

                if (line.IconCount > 1)
                {
                    // Stacks, as the game draws them on a buff's icon.
                    var font = GameService.Content.DefaultFont12;
                    var countRect = new Rectangle(iconRect.X + 1, iconRect.Bottom - font.LineHeight + 2, iconRect.Width, font.LineHeight);
                    spriteBatch.DrawStringOnCtrl(this, line.IconCount.ToString(CultureInfo.CurrentCulture), font, countRect, Color.White,
                        false, true, 1, HorizontalAlignment.Left, VerticalAlignment.Top);
                }

                // Right to left, so the last part (recharge) ends at the edge.
                int right = Width;
                for (int i = line.Right.Count - 1; i >= 0; i--)
                {
                    var (text, icon) = line.Right[i];
                    if (icon != null)
                    {
                        right -= RightIconSize;
                        spriteBatch.DrawOnCtrl(this, icon, new Rectangle(right, placed.Top + (placed.Height - RightIconSize) / 2, RightIconSize, RightIconSize));
                        right -= 3;
                    }
                    int textWidth = Measure(line.Font, text);
                    right -= textWidth;
                    spriteBatch.DrawStringOnCtrl(this, text, line.Font,
                        new Rectangle(right, placed.Top + (placed.Height - line.Font.LineHeight) / 2, textWidth + 4, line.Font.LineHeight), Color.White,
                        false, HorizontalAlignment.Left, VerticalAlignment.Top);
                    right -= RightGap;
                }
            }
        }

        /// <summary>"1 [gold] 20 [silver] 33 [copper]", leaving out zero amounts above copper.</summary>
        private void PaintCoins(SpriteBatch spriteBatch, TooltipLine line, int top)
        {
            int copper = line.Coins.Value;
            var parts = new List<(int amount, string icon)>();
            if (copper >= 10000) parts.Add((copper / 10000, GoldIcon));
            if (copper >= 100) parts.Add((copper / 100 % 100, SilverIcon));
            parts.Add((copper % 100, CopperIcon));
            int x = line.Indent;
            foreach (var (amount, icon) in parts)
            {
                string text = amount.ToString(CultureInfo.CurrentCulture);
                int width = Measure(line.Font, text);
                spriteBatch.DrawStringOnCtrl(this, text, line.Font, new Rectangle(x, top, width + 4, CoinIconSize + 2), Color.White,
                    false, HorizontalAlignment.Left, VerticalAlignment.Middle);
                x += width + 2;
                // Asked again if Blish HUD's download failed, which gives the module's own download instead.
                if (!_coinIcons.TryGetValue(icon, out var texture) || TyriaSearchModule.IsErrorTexture(texture))
                    _coinIcons[icon] = texture = _renderIcon(icon);
                if (TyriaSearchModule.IsLoaded(texture)) spriteBatch.DrawOnCtrl(this, texture, new Rectangle(x, top + 1, CoinIconSize, CoinIconSize));
                x += CoinIconSize + 6;
            }
        }
    }

    /// <summary>
    /// Game-style tooltips for skill, trait, item and effect results: what the game's own tooltip shows, from the API, then
    /// the search's own information below. Each skill, trait or item is fetched once, the first time its tooltip shows,
    /// in the display language, and kept for the session. Effects show what the search data already has.
    /// </summary>
    internal sealed class GameTooltips
    {
        private static readonly Logger Logger = Logger.GetLogger<GameTooltips>();

        // Colours of the game's tooltips.
        private static readonly Color TitleColor = Color.White;
        private static readonly Color TextColor = new Color(225, 225, 225);
        private static readonly Color FactColor = new Color(240, 240, 240);
        private static readonly Color StatColor = new Color(85, 210, 85);
        private static readonly Color MutedColor = new Color(160, 160, 160);
        private static readonly Color FooterColor = new Color(190, 190, 190);

        /// <summary>Colours of the API's &lt;c=@…&gt; tags.</summary>
        private static readonly Dictionary<string, Color> TagColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["@flavor"] = new Color(155, 205, 225),
            ["@reminder"] = new Color(170, 170, 170),
            ["@abilitytype"] = new Color(255, 236, 140),
            ["@warning"] = new Color(255, 100, 100),
            ["@task"] = new Color(255, 200, 120),
            ["@quest"] = new Color(255, 200, 120),
            ["@event"] = new Color(255, 200, 120),
        };

        /// <summary>The API's attribute ids as the game names them.</summary>
        private static readonly Dictionary<string, string> AttributeNames = new Dictionary<string, string>
        {
            ["CritDamage"] = "Ferocity",
            ["ConditionDamage"] = "Condition Damage",
            ["Healing"] = "Healing Power",
            ["BoonDuration"] = "Concentration",
            ["ConditionDuration"] = "Expertise",
            ["AgonyResistance"] = "Agony Resistance",
        };

        private static readonly Dictionary<string, string> WeaponNames = new Dictionary<string, string>
        {
            ["LongBow"] = "Longbow",
            ["ShortBow"] = "Short Bow",
            ["Harpoon"] = "Spear",
            ["Speargun"] = "Harpoon Gun",
            ["LargeBundle"] = "Bundle",
            ["SmallBundle"] = "Bundle",
            ["ToyTwoHanded"] = "Toy",
        };

        private static readonly Dictionary<string, string> ArmorSlots = new Dictionary<string, string>
        {
            ["Coat"] = "Chest",
            ["Leggings"] = "Legs",
            ["Gloves"] = "Hands",
            ["Helm"] = "Head",
            ["HelmAquatic"] = "Aquatic Headgear",
            ["Boots"] = "Feet",
        };

        // /v2/files ui_upgrade_slot_open and ui_infusion_slot_agony: the game's empty slot icons.
        private const string UpgradeSlotIcon = "https://render.guildwars2.com/file/0C6956A85FDB6F5FFF49D708E10549D1A8459003/517197.png";
        private const string InfusionSlotIcon = "https://render.guildwars2.com/file/F54BC4283FFAB7531073FF08F470A9730E47FB4D/683590.png";

        // Weapons with two upgrade slots.
        private static readonly HashSet<string> TwoHanded = new HashSet<string>
        {
            "Greatsword", "Hammer", "LongBow", "ShortBow", "Rifle", "Staff", "Harpoon", "Speargun", "Trident",
        };

        private readonly Gw2Data _data;
        private readonly Func<string> _language;
        private readonly Func<SearchEntry, AsyncTexture2D> _iconFor;
        private readonly Func<string, AsyncTexture2D> _renderIcon;
        private readonly Func<SearchEntry, Texture2D> _placeholderFor;
        private readonly CancellationToken _ct;
        // By endpoint, id and language. A failed fetch is tried again the next time.
        private readonly Dictionary<(string, int, string), Task<object>> _fetched = new Dictionary<(string, int, string), Task<object>>();
        // From the wiki, the same in every language: activation times by skill id, and healing coefficients by
        // whether it's a trait and id.
        private readonly Dictionary<int, Task<double?>> _activationTimes = new Dictionary<int, Task<double?>>();
        private readonly Dictionary<(bool, int), Task<Dictionary<double, double>>> _coefficients = new Dictionary<(bool, int), Task<Dictionary<double, double>>>();

        /// <summary>What a skill or trait tooltip adds from the wiki, which the API doesn't give.</summary>
        private sealed class WikiExtras
        {
            public static readonly WikiExtras None = new WikiExtras(null, new Dictionary<double, double>());

            /// <summary>A skill's activation time in seconds; null if unknown, 0 for instant skills.</summary>
            public readonly double? Activation;
            /// <summary>Healing Power coefficients of healing and barrier facts, by the API's amount (see <see cref="Gw2Data.HealingCoefficientsAsync"/>).</summary>
            public readonly Dictionary<double, double> Coefficients;

            public WikiExtras(double? activation, Dictionary<double, double> coefficients)
            {
                Activation = activation;
                Coefficients = coefficients;
            }

            /// <summary>Whether there's anything to add to the tooltip.</summary>
            public bool Any => Activation > 0 || Coefficients.Count > 0;
        }

        /// <summary>The game's activation time icon (a beige clock like the recharge icon's disc); neither the API nor the wiki has it.</summary>
        private const int ActivationAssetId = 496252;

        /// <param name="renderIcon">A render service icon by URL (see <see cref="TyriaSearchModule.RenderIcon"/>).</param>
        public GameTooltips(Gw2Data data, Func<string> language, Func<SearchEntry, AsyncTexture2D> iconFor,
            Func<string, AsyncTexture2D> renderIcon, Func<SearchEntry, Texture2D> placeholderFor, CancellationToken ct)
        {
            _data = data;
            _language = language;
            _iconFor = iconFor;
            _renderIcon = renderIcon;
            _placeholderFor = placeholderFor;
            _ct = ct;
        }

        /// <summary>
        /// A skill's activation time and a skill's or trait's healing coefficients from the wiki. Whatever the wiki
        /// can't be reached for is left out (and asked again next time).
        /// </summary>
        private async Task<WikiExtras> WikiExtrasAsync(SearchEntry entry)
        {
            int id = Id(entry).Value;
            bool trait = entry.Kind == EntryKind.Trait;
            var activation = trait
                ? Task.FromResult<double?>(null)
                : FromWikiAsync(_activationTimes, id, () => _data.SkillActivationTimeAsync(id, _ct), $"the activation time of skill {id}", null);
            var coefficients = FromWikiAsync(_coefficients, (trait, id), () => _data.HealingCoefficientsAsync(id, trait, _ct),
                $"the healing coefficients of {(trait ? "trait" : "skill")} {id}", WikiExtras.None.Coefficients);
            return new WikiExtras(await activation, await coefficients);
        }

        /// <summary>The cached answer for <paramref name="key"/>, fetched if there's none yet or it failed; <paramref name="fallback"/> if it fails.</summary>
        private async Task<T> FromWikiAsync<TKey, T>(Dictionary<TKey, Task<T>> cache, TKey key, Func<Task<T>> fetch, string what, T fallback)
        {
            Task<T> task;
            lock (cache)
            {
                if (!cache.TryGetValue(key, out task) || task.IsFaulted || task.IsCanceled)
                    cache[key] = task = fetch();
            }
            try
            {
                return await task;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Debug(ex, $"Couldn't get {what} from the wiki.");
                return fallback; // the rest of the tooltip still shows
            }
        }

        /// <summary>Whether <see cref="Create"/> makes a game-style tooltip for the entry: skills, traits and items with a chat link, and effects.</summary>
        public static bool Supports(SearchEntry entry) => entry.Kind == EntryKind.Effect || Id(entry) != null;

        /// <summary>
        /// A tooltip showing what the search already has while the rest is fetched, then the game's tooltip. Make it only
        /// when it's about to show (the row is hovered): the fetch starts here. A skill's activation time and healing
        /// coefficients come from the wiki, which is slower: the tooltip shows without them, then gains them when they arrive.
        /// </summary>
        /// <param name="footer">The search's own information, shown below a rule.</param>
        public Tooltip Create(SearchEntry entry, string footer)
        {
            var tooltip = new Tooltip();
            // Effects have everything already; nothing to fetch.
            if (entry.Kind == EntryKind.Effect)
            {
                _ = new TooltipContent(EffectLines(entry, footer), _renderIcon) { Parent = tooltip };
                return tooltip;
            }

            bool disposed = false;
            tooltip.Disposed += (_, __) => disposed = true;
            TooltipContent shown = new TooltipContent(FallbackLines(entry, footer, "Loading the tooltip…"), _renderIcon) { Parent = tooltip };
            _ = FillAsync();
            return tooltip;

            // Controls change on the main thread, between frames.
            void Show(List<TooltipLine> lines) => GameService.Overlay.QueueMainThreadUpdate(gameTime =>
            {
                if (disposed) return;
                shown.Parent = null; // the tooltip resizes to its new content
                shown.Dispose();
                shown = new TooltipContent(lines, _renderIcon) { Parent = tooltip };
            });

            async Task FillAsync()
            {
                var wiki = entry.Kind == EntryKind.Skill || entry.Kind == EntryKind.Trait ? WikiExtrasAsync(entry) : null;
                bool hasWiki = wiki?.Status == TaskStatus.RanToCompletion; // known from an earlier hover
                try
                {
                    Show(await LinesAsync(entry, footer, hasWiki ? wiki.Result : WikiExtras.None));
                }
                catch (OperationCanceledException)
                {
                    return; // unloading
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, $"Couldn't get the tooltip of {entry.Name} from the API.");
                    Show(FallbackLines(entry, footer, "Couldn't get the full tooltip from the API just now."));
                    return;
                }

                if (wiki == null || hasWiki) return;
                try
                {
                    var extras = await wiki; // fails only when unloading; leaves out what the wiki couldn't say
                    if (!extras.Any) return; // instant skills show no activation time
                    Show(await LinesAsync(entry, footer, extras)); // the API's part is kept, so this is quick
                }
                catch (OperationCanceledException)
                {
                    // unloading
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, $"Couldn't add the wiki's details to the tooltip of {entry.Name}.");
                }
            }
        }

        /// <summary>An effect's tooltip: icon and name, its type or (for a consumable's) the effect and duration, then its description.</summary>
        private List<TooltipLine> EffectLines(SearchEntry entry, string footer)
        {
            var lines = new List<TooltipLine> { Header(entry, entry.Name, TitleColor, null) };
            if (!string.IsNullOrEmpty(entry.Context)) lines.Add(new TooltipLine { SpaceBefore = 4 }.Add(entry.Context, MutedColor));
            AddDescription(lines, entry.Description);
            AddFooter(lines, footer);
            return lines;
        }

        /// <summary>The id in the entry's chat link: [&amp;06…] skills and [&amp;07…] traits have 4 bytes after the type, [&amp;02…] items a count then 3.</summary>
        private static int? Id(SearchEntry entry)
        {
            string link = entry.ChatLink;
            if (entry.LinkIsNearestWaypoint || link == null || !link.StartsWith("[&") || !link.EndsWith("]")) return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(link.Substring(2, link.Length - 3)); }
            catch (FormatException) { return null; }
            switch (entry.Kind)
            {
                case EntryKind.Skill when bytes.Length >= 5 && bytes[0] == 0x06:
                case EntryKind.Trait when bytes.Length >= 5 && bytes[0] == 0x07:
                    return BitConverter.ToInt32(bytes, 1);
                case EntryKind.Item when bytes.Length >= 5 && bytes[0] == 0x02:
                    return bytes[2] | bytes[3] << 8 | bytes[4] << 16;
                default:
                    return null;
            }
        }

        private async Task<T> FetchAsync<T>(string endpoint, int id) where T : class
        {
            var key = (endpoint, id, _language());
            Task<object> task;
            lock (_fetched)
            {
                if (!_fetched.TryGetValue(key, out task) || task.IsFaulted || task.IsCanceled)
                    _fetched[key] = task = FetchOneAsync<T>(endpoint, id, key.Item3);
            }
            return (T)await task;
        }

        private async Task<object> FetchOneAsync<T>(string endpoint, int id, string language) =>
            (await _data.GetByIdsAsync<T>(endpoint, new[] { id }, language, _ct)).FirstOrDefault()
            ?? throw new InvalidOperationException($"The API has no {endpoint} {id}.");

        /// <param name="wiki">What's known so far from the wiki.</param>
        private async Task<List<TooltipLine>> LinesAsync(SearchEntry entry, string footer, WikiExtras wiki)
        {
            int id = Id(entry).Value;
            var lines = new List<TooltipLine>();
            switch (entry.Kind)
            {
                case EntryKind.Skill:
                    var skill = await FetchAsync<ApiSkill>("skills", id);
                    lines.Add(Header(entry, entry.Name, TitleColor, skill.Facts, wiki.Activation));
                    AddDescription(lines, skill.Description);
                    if (skill.Initiative > 0) lines.Add(new TooltipLine { SpaceBefore = 4 }.Add($"Initiative: {skill.Initiative}", FactColor));
                    if (skill.Cost > 0) lines.Add(new TooltipLine { SpaceBefore = 4 }.Add($"Energy Cost: {skill.Cost}", FactColor));
                    AddFacts(lines, skill.Facts, wiki.Coefficients);
                    AddPveNote(lines);
                    break;
                case EntryKind.Trait:
                    var trait = await FetchAsync<ApiTrait>("traits", id);
                    lines.Add(Header(entry, entry.Name, TitleColor, trait.Facts));
                    AddDescription(lines, trait.Description);
                    AddFacts(lines, trait.Facts, wiki.Coefficients);
                    AddPveNote(lines);
                    break;
                default:
                    var item = await FetchAsync<ApiItem>("items", id);
                    await AddItemAsync(lines, entry, item);
                    break;
            }
            AddFooter(lines, footer);
            return lines;
        }

        /// <summary>What the search already has: name, icon and description, a note, then the footer.</summary>
        private List<TooltipLine> FallbackLines(SearchEntry entry, string footer, string note)
        {
            var color = entry.Rarity != null && SearchWindow.RarityColors.TryGetValue(entry.Rarity, out var rarity) ? rarity : TitleColor;
            var lines = new List<TooltipLine> { Header(entry, entry.Name, color, null) };
            if (entry.Description != null) lines.Add(new TooltipLine { SpaceBefore = 6 }.Add(entry.Description, TextColor));
            lines.Add(new TooltipLine { SpaceBefore = 6, Font = GameService.Content.DefaultFont12 }.Add(note, MutedColor));
            AddFooter(lines, footer);
            return lines;
        }

        /// <summary>Icon and name; for skills and traits, the activation time and recharge at the right, as in game.</summary>
        private TooltipLine Header(SearchEntry entry, string name, Color color, List<ApiFact> facts, double? activation = null)
        {
            var line = new TooltipLine
            {
                Font = GameService.Content.DefaultFont18, Icon = _iconFor(entry), Placeholder = _placeholderFor(entry), IconSize = 32,
            }.Add(name, color);
            // Instant skills show none.
            if (activation > 0) line.Right.Add((Fraction(activation.Value, line.Font), ActivationTexture()));
            var recharge = facts?.FirstOrDefault(f => f.Type == "Recharge" && f.Value > 0);
            if (recharge != null) line.Right.Add((Number(recharge.Value.Value), Texture(recharge.Icon)));
            return line;
        }

        private static AsyncTexture2D ActivationTexture() => AsyncTexture2D.FromAssetId(ActivationAssetId);

        /// <summary>
        /// Seconds as the game shows activation times, in quarters: "¾", "1½", "2"; others as decimals. Spelled "3/4"
        /// if the font has no ¼ ½ ¾ characters.
        /// </summary>
        private static string Fraction(double seconds, BitmapFont font)
        {
            int whole = (int)Math.Floor(seconds);
            double rest = seconds - whole;
            int quarters = Math.Abs(rest) < 0.01 ? 0 : Math.Abs(rest - 0.25) < 0.01 ? 1 : Math.Abs(rest - 0.5) < 0.01 ? 2 : Math.Abs(rest - 0.75) < 0.01 ? 3 : -1;
            if (quarters < 0) return seconds.ToString("0.##", CultureInfo.CurrentCulture);
            if (quarters == 0) return whole.ToString(CultureInfo.CurrentCulture);
            char glyph = quarters == 1 ? '¼' : quarters == 2 ? '½' : '¾';
            if (font.GetCharacterRegion(glyph) != null) return whole == 0 ? glyph.ToString() : $"{whole}{glyph}";
            string spelled = quarters == 1 ? "1/4" : quarters == 2 ? "1/2" : "3/4";
            return whole == 0 ? spelled : $"{whole} {spelled}";
        }

        private static void AddDescription(List<TooltipLine> lines, string description)
        {
            var line = new TooltipLine { SpaceBefore = 6 };
            line.Runs.AddRange(Markup(description, TextColor));
            if (line.Runs.Count > 0) lines.Add(line);
        }

        /// <param name="coefficients">Healing Power coefficients of healing and barrier facts, by amount.</param>
        private void AddFacts(List<TooltipLine> lines, List<ApiFact> facts, Dictionary<double, double> coefficients)
        {
            bool first = true;
            foreach (var fact in facts ?? new List<ApiFact>())
            {
                if (fact.Type == "Recharge") continue; // in the header, as in game
                string text = FactText(fact, coefficients);
                if (string.IsNullOrWhiteSpace(text)) continue; // BuffArray facts are empty
                bool buff = fact.Type == "Buff" || fact.Type == "PrefixedBuff";
                var line = new TooltipLine { SpaceBefore = first ? 8 : 2, Icon = Texture(fact.Icon), IconCount = buff ? fact.ApplyCount ?? 0 : 0 };
                if (fact.Type == "PrefixedBuff" && fact.Prefix != null)
                {
                    // The prefix buff's icon stands before the fact's own, as in game.
                    lines.Add(new TooltipLine { SpaceBefore = line.SpaceBefore, Icon = Texture(fact.Prefix.Icon) }.Add(BuffText(fact.Prefix, true), FactColor));
                    line.SpaceBefore = 2;
                    line.Indent = 12;
                }
                line.Add(text, FactColor);
                lines.Add(line);
                first = false;
            }
        }

        /// <summary>
        /// A fact as the game words it. Numbers that depend on the character's stats are shown as how they're worked out,
        /// since the game's own number is for the character's stats: damage as its power coefficient, and healing and
        /// barrier as the amount at 0 Healing Power plus the wiki's coefficient (just the amount until that's known).
        /// </summary>
        private static string FactText(ApiFact f, Dictionary<double, double> coefficients)
        {
            string text = f.Text;
            string Hits() => f.HitCount > 1 ? $" ({f.HitCount}x)" : "";
            switch (f.Type)
            {
                case "Buff":
                case "PrefixedBuff":
                    return BuffText(f, false); // stacks are drawn on the icon
                case "Damage":
                    return $"{text ?? "Damage"}{Hits()}" + (f.DamageMultiplier > 0 ? $": {f.DamageMultiplier.Value.ToString("0.###", CultureInfo.CurrentCulture)} power coefficient" : "");
                case "Heal":
                case "HealingAdjust":
                    return $"{text ?? "Heal"}{Hits()}";
                case "AttributeAdjust":
                    // Healing and barrier are AttributeAdjust facts targeting Healing (Power), with the amount at 0 Healing Power.
                    string adjust = $"{text ?? Attribute(f.Target)}: {Number(f.Value ?? 0)}";
                    return f.Target == "Healing" && f.Value.HasValue && coefficients.TryGetValue(f.Value.Value, out double coefficient)
                        ? $"{adjust} + {coefficient.ToString("0.0###", CultureInfo.CurrentCulture)} {Times} Healing Power"
                        : adjust;
                case "Number":
                case "Range":
                    return $"{text}: {Number(f.Value ?? 0)}";
                case "Distance":
                case "Radius":
                    return $"{text}: {Number(f.Distance ?? 0)}";
                case "Percent":
                    return $"{text}: {Number(f.Percent ?? 0)}%";
                case "Duration":
                case "Time":
                    return $"{text}: {Seconds(f.Duration ?? 0)}";
                case "ComboField":
                    return $"{text ?? "Combo Field"}: {f.FieldType}";
                case "ComboFinisher":
                    return $"{text ?? "Combo Finisher"}: {f.FinisherType}" + (f.Percent > 0 && f.Percent < 100 ? $" ({Number(f.Percent.Value)}% Chance)" : "");
                case "BuffConversion":
                    return $"{text ?? "Attribute Conversion"}: {Number(f.Percent ?? 0)}% of {Attribute(f.Source)} as {Attribute(f.Target)}";
                case "StunBreak":
                    return "Breaks Stun"; // the API's text is "StunBreak"
                case "Unblockable":
                    return text ?? "Unblockable";
                default:
                    // NoData ("Reflects Missiles"), and the odd fact with no type ("Life Force Cost" and a percent).
                    if (text == null) return "";
                    if (f.Percent.HasValue) return $"{text}: {Number(f.Percent.Value)}%";
                    if (f.Value.HasValue) return $"{text}: {Number(f.Value.Value)}";
                    if (f.Duration.HasValue) return $"{text}: {Seconds(f.Duration.Value)}";
                    return text;
            }
        }

        /// <summary>"Bleeding (8s): Deals damage every second; stacks intensity.", with "3x " before it if <paramref name="withCount"/>.</summary>
        private static string BuffText(ApiFact f, bool withCount)
        {
            string count = withCount && f.ApplyCount > 1 ? $"{f.ApplyCount}x " : "";
            string duration = f.Duration > 0 ? $" ({Seconds(f.Duration.Value)})" : "";
            string description = string.IsNullOrWhiteSpace(f.Description) ? "" : $": {StripMarkup(f.Description)}";
            return $"{count}{f.Status ?? f.Text}{duration}{description}";
        }

        // ---- Items ----

        private async Task AddItemAsync(List<TooltipLine> lines, SearchEntry entry, ApiItem item)
        {
            var details = item.Details ?? new ApiItemDetails();
            var flags = item.Flags ?? new List<string>();
            var rarityColor = item.Rarity != null && SearchWindow.RarityColors.TryGetValue(item.Rarity, out var c) ? c : TitleColor;
            lines.Add(Header(entry, entry.Name, rarityColor, null));

            // Upgrades and infusions in the item are other items; fetched together.
            var slotted = new List<int>();
            if (details.SuffixItemId > 0) slotted.Add(details.SuffixItemId.Value);
            if (int.TryParse(details.SecondarySuffixItemId, out int secondary) && secondary > 0) slotted.Add(secondary);
            slotted.AddRange((details.InfusionSlots ?? new List<ApiInfusionSlot>()).Where(s => s.ItemId > 0).Select(s => s.ItemId.Value));
            var upgrades = new Dictionary<int, ApiItem>();
            foreach (var pair in await Task.WhenAll(slotted.Distinct().Select(async id =>
                     {
                         try { return (id, await FetchAsync<ApiItem>("items", id)); }
                         catch (Exception ex) when (!(ex is OperationCanceledException)) { return (id, (ApiItem)null); }
                     })))
                if (pair.Item2 != null) upgrades[pair.id] = pair.Item2;

            if (details.MinPower > 0 || details.MaxPower > 0)
                lines.Add(new TooltipLine { SpaceBefore = 6 }.Add($"Weapon Strength: {Number(details.MinPower)} - {Number(details.MaxPower)}", FactColor));
            if (details.Defense > 0) lines.Add(new TooltipLine { SpaceBefore = 6 }.Add($"Defense: {Number(details.Defense)}", FactColor));

            int before = lines.Count;
            AddInfix(lines, details.InfixUpgrade, 0);
            if (lines.Count > before) lines[before].SpaceBefore = 6;
            if (details.InfixUpgrade == null && details.StatChoices?.Count > 0)
                lines.Add(new TooltipLine { SpaceBefore = 6 }.Add("Double-click to select stats.", StatColor));

            // Runes: bonuses by how many are worn.
            AddBonuses(lines, details.Bonuses, 0);

            // Consumables: the effect they give.
            if (!string.IsNullOrEmpty(details.Name) || details.DurationMs > 0)
            {
                var effect = new TooltipLine { SpaceBefore = 6, Icon = Texture(details.Icon), IconSize = 24 };
                effect.Add(details.Name, FactColor);
                if (details.DurationMs > 0) effect.Add($" ({Duration(details.DurationMs.Value)})", FactColor);
                if (!string.IsNullOrWhiteSpace(details.Description))
                {
                    effect.Add(string.IsNullOrEmpty(details.Name) ? "" : ": ", FactColor);
                    effect.Runs.AddRange(Markup(details.Description, TextColor));
                }
                lines.Add(effect);
            }

            // Upgrade slots, filled or not; then infusion slots.
            if (item.Type == "Weapon" || item.Type == "Armor" || item.Type == "Back")
            {
                int slots = item.Type == "Weapon" && TwoHanded.Contains(details.Type ?? "") ? 2 : 1;
                var filled = new[] { details.SuffixItemId ?? 0, secondary }.Where(id => id > 0).ToList();
                bool first = true;
                for (int i = 0; i < Math.Max(slots, filled.Count); i++)
                {
                    int space = first ? 8 : 4;
                    first = false;
                    if (i < filled.Count && upgrades.TryGetValue(filled[i], out var upgrade)) AddUpgrade(lines, upgrade, space);
                    else lines.Add(new TooltipLine { SpaceBefore = space, Icon = Texture(UpgradeSlotIcon) }.Add("Unused Upgrade Slot", MutedColor));
                }
            }
            foreach (var slot in details.InfusionSlots ?? new List<ApiInfusionSlot>())
            {
                if (slot.ItemId > 0 && upgrades.TryGetValue(slot.ItemId.Value, out var infusion)) AddUpgrade(lines, infusion, 4);
                else
                {
                    string kind = slot.Flags?.Contains("Enrichment") == true ? "Enrichment" : "Infusion";
                    lines.Add(new TooltipLine { SpaceBefore = 4, Icon = Texture(InfusionSlotIcon) }.Add($"Unused {kind} Slot", MutedColor));
                }
            }

            if (details.Size > 0) lines.Add(new TooltipLine { SpaceBefore = 6 }.Add($"{details.Size} Slots", FactColor));
            if (details.Charges > 0) lines.Add(new TooltipLine { SpaceBefore = 6 }.Add($"{details.Charges} Uses", FactColor));

            // Rarity, type and level, as the game lists them below the stats.
            lines.Add(new TooltipLine { SpaceBefore = 8 }.Add(item.Rarity, rarityColor));
            lines.Add(new TooltipLine().Add(ItemType(item), FactColor));
            if (item.Level > 0) lines.Add(new TooltipLine().Add($"Required Level: {item.Level}", FactColor));
            var restrictions = (item.Restrictions ?? new List<string>()).Where(r => !string.IsNullOrEmpty(r)).Select(SplitWords).ToList();
            if (restrictions.Count > 0) lines.Add(new TooltipLine().Add($"Only for: {string.Join(", ", restrictions)}", FactColor));

            AddDescription(lines, item.Description);

            foreach (string text in Binding(flags)) lines.Add(new TooltipLine { SpaceBefore = 4 }.Add(text, FactColor));
            if (flags.Contains("Unique")) lines.Add(new TooltipLine { SpaceBefore = 4 }.Add("Unique", FactColor));
            if (item.VendorValue > 0 && !flags.Contains("NoSell")) lines.Add(new TooltipLine { SpaceBefore = 6, Coins = item.VendorValue });
        }

        /// <summary>"+251 Power" for each attribute, and the buff's lines ("+5% Damage").</summary>
        private static void AddInfix(List<TooltipLine> lines, ApiInfixUpgrade infix, int indent)
        {
            if (infix == null) return;
            foreach (var attribute in infix.Attributes ?? new List<ApiAttribute>())
                lines.Add(new TooltipLine { Indent = indent }.Add($"+{Number(attribute.Modifier)} {Attribute(attribute.Attribute)}", StatColor));
            if (!string.IsNullOrWhiteSpace(infix.Buff?.Description))
            {
                var line = new TooltipLine { Indent = indent };
                line.Runs.AddRange(Markup(infix.Buff.Description, StatColor));
                lines.Add(line);
            }
        }

        private static void AddBonuses(List<TooltipLine> lines, List<string> bonuses, int indent)
        {
            if (bonuses == null) return;
            for (int i = 0; i < bonuses.Count; i++)
            {
                var line = new TooltipLine { Indent = indent, SpaceBefore = i == 0 ? 6 : 0 }.Add($"({i + 1}): ", MutedColor);
                line.Runs.AddRange(Markup(bonuses[i], MutedColor));
                lines.Add(line);
            }
        }

        /// <summary>A slotted upgrade or infusion: its icon and name, then what it gives, indented.</summary>
        private void AddUpgrade(List<TooltipLine> lines, ApiItem upgrade, int spaceBefore)
        {
            var color = upgrade.Rarity != null && SearchWindow.RarityColors.TryGetValue(upgrade.Rarity, out var c) ? c : TitleColor;
            lines.Add(new TooltipLine { SpaceBefore = spaceBefore, Icon = Texture(upgrade.Icon), IconSize = 20 }.Add(upgrade.Name, color));
            AddInfix(lines, upgrade.Details?.InfixUpgrade, 26);
            var bonuses = upgrade.Details?.Bonuses;
            if (bonuses != null)
            {
                int before = lines.Count;
                AddBonuses(lines, bonuses, 26);
                if (lines.Count > before) lines[before].SpaceBefore = 0;
            }
        }

        private static string ItemType(ApiItem item)
        {
            var details = item.Details;
            string detail = details?.Type;
            switch (item.Type)
            {
                case "Armor":
                    string slot = detail != null && ArmorSlots.TryGetValue(detail, out var name) ? name : SplitWords(detail);
                    return string.IsNullOrEmpty(details?.WeightClass) || details.WeightClass == "Clothing" ? slot : $"{details.WeightClass} {slot} Armor";
                case "Weapon":
                    return detail != null && WeaponNames.TryGetValue(detail, out var weapon) ? weapon : SplitWords(detail);
                case "Back":
                    return "Back Item";
                default:
                    // "Food" says more than "Consumable"; "Default" and "Generic" say nothing.
                    return string.IsNullOrEmpty(detail) || detail == "Default" || detail == "Generic" ? SplitWords(item.Type) : SplitWords(detail);
            }
        }

        private static IEnumerable<string> Binding(List<string> flags)
        {
            if (flags.Contains("SoulbindOnAcquire")) yield return "Soulbound";
            else if (flags.Contains("AccountBound")) yield return "Account Bound";
            else if (flags.Contains("AccountBindOnUse")) yield return "Account Bound on Use";
            if (flags.Contains("SoulBindOnUse") && !flags.Contains("SoulbindOnAcquire")) yield return "Soulbound on Use";
        }

        // ---- Footer ----

        /// <summary>The API gives skills' and traits' PvE values only; PvP and WvW can differ (game mode splits).</summary>
        private static void AddPveNote(List<TooltipLine> lines) =>
            lines.Add(new TooltipLine { SpaceBefore = 6, Font = GameService.Content.DefaultFont12 }.Add("PvE tooltip", MutedColor));

        private static void AddFooter(List<TooltipLine> lines, string footer)
        {
            if (string.IsNullOrEmpty(footer)) return;
            lines.Add(new TooltipLine { SpaceBefore = 6, Separator = true });
            lines.Add(new TooltipLine { Font = GameService.Content.DefaultFont12 }.Add(footer, FooterColor));
        }

        // ---- Text ----

        /// <summary>The API's markup as coloured runs: &lt;c=@flavor&gt;…&lt;/c&gt; (or &lt;c=#rrggbb&gt;) colours its text, &lt;br&gt; breaks the line.</summary>
        internal static List<TooltipRun> Markup(string text, Color color)
        {
            var runs = new List<TooltipRun>();
            if (string.IsNullOrWhiteSpace(text)) return runs;
            text = Regex.Replace(text.Trim(), @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            var outer = new Stack<Color>();
            var current = color;
            int position = 0;
            foreach (Match tag in Regex.Matches(text, @"<c(?:=([^>]*))?>|</c>", RegexOptions.IgnoreCase))
            {
                if (tag.Index > position) runs.Add(new TooltipRun(text.Substring(position, tag.Index - position), current));
                // A bare <c> closes like </c> in some descriptions.
                if (!tag.Groups[1].Success) current = outer.Count > 0 ? outer.Pop() : color;
                else
                {
                    outer.Push(current);
                    current = TagColor(tag.Groups[1].Value, current);
                }
                position = tag.Index + tag.Length;
            }
            if (position < text.Length) runs.Add(new TooltipRun(text.Substring(position), current));
            return runs;
        }

        private static Color TagColor(string value, Color fallback)
        {
            if (TagColors.TryGetValue(value, out var color)) return color;
            if (value.StartsWith("#") && value.Length == 7 && int.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                return new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return fallback;
        }

        private static string StripMarkup(string text) =>
            Regex.Replace(Regex.Replace(text, @"<br\s*/?>", " ", RegexOptions.IgnoreCase), @"</?c(=[^>]*)?>", "", RegexOptions.IgnoreCase).Trim();

        private AsyncTexture2D Texture(string url) => _renderIcon(url);

        private static string Attribute(string id) => id == null ? "" : AttributeNames.TryGetValue(id, out var name) ? name : SplitWords(id);

        private static string Number(double value) => value.ToString(value % 1 == 0 ? "N0" : "N2", CultureInfo.CurrentCulture);

        /// <summary>"×", or "x" if the tooltip font has no × character.</summary>
        private static string Times => GameService.Content.DefaultFont14.GetCharacterRegion('×') != null ? "×" : "x";

        private static string Seconds(double seconds) => $"{seconds.ToString("0.##", CultureInfo.CurrentCulture)}s";

        /// <summary>"30 m", "1 h", "1 h 30 m", "45 s", as consumables show their effect's duration.</summary>
        private static string Duration(long ms)
        {
            var time = TimeSpan.FromMilliseconds(ms);
            if (time.TotalHours >= 1) return time.Minutes > 0 ? $"{(int)time.TotalHours} h {time.Minutes} m" : $"{(int)time.TotalHours} h";
            if (time.TotalMinutes >= 1) return $"{(int)time.TotalMinutes} m";
            return $"{(int)time.TotalSeconds} s";
        }

        /// <summary>"UpgradeComponent" → "Upgrade Component".</summary>
        private static string SplitWords(string text) => text == null ? null : Regex.Replace(text, "(?<=[a-z])(?=[A-Z])", " ");
    }
}
