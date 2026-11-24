using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Bytex.TyriaSearch
{
    // Saved as numbers in the cache files: add new kinds at the end.
    public enum EntryKind { Waypoint, PointOfInterest, Vista, Skill, Trait, Item, Npc, WikiPage, Portal, RenownHeart, HeroChallenge, MasteryPoint, Adventure, Event, Effect }

    /// <summary>One searchable thing in one language. Short JSON names keep the cache files small.</summary>
    public sealed class SearchEntry
    {
        [JsonProperty("k")] public EntryKind Kind { get; set; }
        /// <summary>
        /// For things without a chat link of their own (NPCs, events, portals, renown hearts, hero challenges, mastery points,
        /// adventures, most wiki pages), the nearest waypoint's; null when no waypoint is known.
        /// </summary>
        [JsonProperty("l")] public string ChatLink { get; set; }
        [JsonProperty("n")] public string Name { get; set; }
        /// <summary>
        /// Area and map for landmarks; profession and slot for skills and traits; rarity, type and level for items; area, map
        /// and services for NPCs; the type, or effect and duration, for effects. Parts are joined with " · ".
        /// </summary>
        [JsonProperty("c")] public string Context { get; set; }
        /// <summary>The description, without its markup, for skills, traits, items, adventures and effects; null when there's none.</summary>
        [JsonProperty("d")] public string Description { get; set; }
        /// <summary>
        /// Other words the entry is found by but doesn't show, in parts joined with " · " like <see cref="Context"/>: for
        /// NPCs, what players call their services ("Cooking Station" for a master chef, "Vault" for a banker); null when none.
        /// </summary>
        [JsonProperty("t")] public string Keywords { get; set; }
        /// <summary>
        /// A render service URL, <see cref="BundledIcon"/> and the name of one shipped with the module,
        /// or <see cref="AssetIcon"/> and a game asset id.
        /// </summary>
        [JsonProperty("i")] public string Icon { get; set; }
        public const string BundledIcon = "bundled:";
        public const string AssetIcon = "asset:";
        /// <summary>Set when the chat link doesn't identify the entry (NPCs share their waypoint's).</summary>
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("m")] public int MapId { get; set; }
        /// <summary>Continent coordinates, when known.</summary>
        [JsonProperty("x")] public float? X { get; set; }
        [JsonProperty("y")] public float? Y { get; set; }
        [JsonProperty("r")] public string Rarity { get; set; }
        /// <summary>The position is the centre of the named area, not the exact spot.</summary>
        [JsonProperty("a")] public bool Approximate { get; set; }
        /// <summary>The chat link is the waypoint nearest to this (see <see cref="ChatLink"/>), not this itself.</summary>
        [JsonProperty("w")] public bool LinkIsNearestWaypoint { get; set; }

        /// <summary>Identifies the entry across languages and in saved favourites.</summary>
        [JsonIgnore] public string Key => Id ?? ChatLink;
    }

    /// <summary>An entry in the display language, plus the same entry in the second search language if there is one.</summary>
    public sealed class SearchItem
    {
        public SearchEntry Entry { get; }
        public SearchEntry Alternate { get; }

        internal readonly string[] Names;
        /// <summary>Each language's context split into its parts ("Shaemoor Fields · Queensdale"), normalized.</summary>
        internal readonly string[][] ContextParts;
        internal readonly string[] NamesWithContext;

        internal SearchItem(SearchEntry entry, SearchEntry alternate)
        {
            Entry = entry;
            Alternate = alternate;
            var languages = alternate == null ? new[] { entry } : new[] { entry, alternate };
            Names = languages.Select(e => SearchIndex.Normalize(e.Name)).ToArray();
            // Keywords count as context parts, so "cooking station" ranks its NPCs like a map name ranks its landmarks.
            ContextParts = languages.Select(e => ((e.Context ?? "") + " · " + (e.Keywords ?? "")).Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(SearchIndex.Normalize).ToArray()).ToArray();
            NamesWithContext = languages.Select(e => SearchIndex.Normalize(e.Kind == EntryKind.Portal
                // "portal"/"gate" find every kind of portal; the map it's on (context) before the destination lets "from A to B" match in order
                ? "portal gate from " + e.Context + " " + e.Name
                : e.Name + " " + e.Context + " " + e.Keywords)).ToArray();
        }
    }

    /// <summary>
    /// Where the player is: the map id, and continent coordinates like <see cref="SearchEntry.X"/>, with the continent
    /// of any map, since positions on different continents can't be compared.
    /// </summary>
    public readonly struct PlayerSpot
    {
        public readonly int MapId;
        public readonly float X, Y;
        public readonly Func<int, int> ContinentOf;
        public PlayerSpot(int mapId, float x, float y, Func<int, int> continentOf) =>
            (MapId, X, Y, ContinentOf) = (mapId, x, y, continentOf);
    }

    public sealed class SearchIndex
    {
        // Listing order by kind, indexed by EntryKind: places, NPCs and map activities first, then the ~4,500 events,
        // then skills, traits and effects, then the ~75,000 items, which would otherwise crowd everything else out of the result list.
        private static readonly int[] KindOrder =
        {
            0,  // Waypoint
            1,  // PointOfInterest
            2,  // Vista
            10, // Skill
            11, // Trait
            13, // Item
            4,  // Npc
            14, // WikiPage (only in live wiki results)
            3,  // Portal
            5,  // RenownHeart
            6,  // HeroChallenge
            7,  // MasteryPoint
            8,  // Adventure
            9,  // Event
            12, // Effect
        };

        private readonly List<SearchItem> _items;
        private readonly Dictionary<string, SearchItem> _byKey;

        public static readonly SearchIndex Empty = new SearchIndex(new SearchEntry[0], null);

        public SearchIndex(IEnumerable<SearchEntry> entries, IEnumerable<SearchEntry> alternateEntries)
        {
            var alternates = new Dictionary<string, SearchEntry>(StringComparer.Ordinal);
            foreach (var entry in alternateEntries ?? new SearchEntry[0])
                if (entry.Key != null) alternates[entry.Key] = entry;

            _items = new List<SearchItem>();
            _byKey = new Dictionary<string, SearchItem>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry.Key == null || _byKey.ContainsKey(entry.Key)) continue;
                alternates.TryGetValue(entry.Key, out var alternate);
                var item = new SearchItem(entry, alternate);
                _items.Add(item);
                _byKey.Add(entry.Key, item);
            }
        }

        public int Count => _items.Count;

        public int CountOf(EntryKind kind) => _items.Count(i => i.Entry.Kind == kind);

        /// <summary>By <see cref="SearchEntry.Key"/>, which is the chat link for everything with one of its own.</summary>
        public SearchItem Find(string key) =>
            key != null && _byKey.TryGetValue(key, out var item) ? item : null;

        /// <summary>
        /// Every query word must appear in the name or context (map, profession) of one language.
        /// Exact names first, then names starting with the query; the rest by kind (<see cref="KindOrder"/>), and within
        /// a kind: every word starts a word of the name, a part of the context starts with the query (a map or area name
        /// lists its landmarks), every word is in the name, the words in query order across name and context (for portals, origin map then
        /// destination), then other matches that need the context.
        /// Within each of the first three groups (exact names, names starting with the query, the rest), results on the
        /// player's map come before the others, so the best match still wins but a nearby one beats one elsewhere.
        /// Ties: the nearest first (on the player's continent), player skills before unlabelled NPC copies, shorter names first.
        /// Results that differ only in their chat code (or id) are listed once (see <see cref="SameInfo"/>), except skills and
        /// traits, whose tooltips (fetched by id) can still differ.
        /// </summary>
        /// <param name="here">The player's map and continent position; null when unknown.</param>
        /// <param name="preferred">Of results listed once, the one kept (e.g. a favourite); null for the first.</param>
        public List<SearchItem> Search(string query, Func<SearchItem, bool> filter, int max, PlayerSpot? here = null,
            Func<SearchItem, bool> preferred = null)
        {
            string trimmed = query?.Trim() ?? "";
            var exactLink = Find(trimmed); // a pasted chat code finds its entry
            if (exactLink != null) return filter(exactLink) ? new List<SearchItem> { exactLink } : new List<SearchItem>();

            string normalized = Normalize(trimmed);
            if (normalized.Length == 0) return new List<SearchItem>();
            string[] words = normalized.Split(' ');

            var seen = new HashSet<string>(StringComparer.Ordinal);
            return _items
                .Where(filter)
                .Select(item => (item, score: Score(item, normalized, words)))
                .Where(x => x.score >= 0)
                .OrderBy(x => Math.Min(x.score, 2))
                .ThenBy(x => here.HasValue && x.item.Entry.MapId == here.Value.MapId && x.item.Entry.X.HasValue ? 0 : 1)
                .ThenBy(x => KindOrder[(int)x.item.Entry.Kind])
                .ThenBy(x => x.score)
                .ThenBy(x => DistanceFrom(here, x.item.Entry))
                .ThenBy(x => string.IsNullOrEmpty(x.item.Entry.Context))
                .ThenBy(x => x.item.Entry.Name.Length)
                .ThenBy(x => x.item.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => preferred != null && preferred(x.item) ? 0 : 1)
                .Select(x => x.item)
                .Where(item => item.Entry.Kind == EntryKind.Skill || item.Entry.Kind == EntryKind.Trait || seen.Add(SameInfo(item)))
                .Take(max)
                .ToList();
        }

        /// <summary>
        /// Everything a result shows except its chat code and id, in both languages: results with the same are the
        /// same thing to the player (an item the game has several copies of) and are listed once.
        /// </summary>
        private static string SameInfo(SearchItem item) =>
            Info(item.Entry) + "" + (item.Alternate == null ? "" : Info(item.Alternate));

        private static string Info(SearchEntry e) => string.Join("",
            (int)e.Kind, e.Name, e.Context, e.Description, e.Icon, e.Rarity, e.MapId, e.X, e.Y, e.Approximate, e.LinkIsNearestWaypoint);

        /// <summary>
        /// Squared distance from the player to an entry on their continent, on any map, since positions are continent
        /// coordinates; for the rest (another continent, no position), the same for all.
        /// </summary>
        private static float DistanceFrom(PlayerSpot? here, SearchEntry entry)
        {
            if (!here.HasValue || !entry.X.HasValue || !entry.Y.HasValue) return float.MaxValue;
            if (entry.MapId != here.Value.MapId && here.Value.ContinentOf(entry.MapId) != here.Value.ContinentOf(here.Value.MapId))
                return float.MaxValue;
            float dx = entry.X.Value - here.Value.X, dy = entry.Y.Value - here.Value.Y;
            return dx * dx + dy * dy;
        }

        private static int Score(SearchItem item, string query, string[] words)
        {
            int best = -1;
            for (int i = 0; i < item.Names.Length; i++)
            {
                string name = item.Names[i], full = item.NamesWithContext[i];
                if (!words.All(full.Contains)) continue;

                int score = name == query ? 0
                    : name.StartsWith(query, StringComparison.Ordinal) ? 1
                    : words.All(w => StartsWord(name, w)) ? 2
                    : item.ContextParts[i].Any(part => part.StartsWith(query, StringComparison.Ordinal)) ? 3
                    : words.All(name.Contains) ? 4
                    : InOrder(full, words) ? 5
                    : 6;
                if (best < 0 || score < best) best = score;
            }
            return best;
        }

        private static bool InOrder(string text, string[] words)
        {
            int position = 0;
            foreach (var word in words)
            {
                int found = text.IndexOf(word, position, StringComparison.Ordinal);
                if (found < 0) return false;
                position = found + word.Length;
            }
            return true;
        }

        private static bool StartsWord(string text, string word) =>
            text.StartsWith(word, StringComparison.Ordinal) || text.Contains(" " + word);

        /// <summary>
        /// Lowercase, no accents (Höhle → hohle), no apostrophes (Lion's → lions), other punctuation becomes a space.
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            bool lastWasSpace = true;
            foreach (char ch in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (ch == '\'' || ch == '’') continue;
                if (char.IsLetterOrDigit(ch))
                {
                    if (ch == 'ß') sb.Append("ss");
                    else sb.Append(char.ToLowerInvariant(ch));
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
