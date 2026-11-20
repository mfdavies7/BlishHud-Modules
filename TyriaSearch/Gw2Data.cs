using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Gw2Sharp.ChatLinks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TyriaSearch
{
    /// <summary>
    /// Downloads searchable data from the GW2 API and the wiki, and keeps it in cache files:
    /// landmarks, map activities, skills and traits per language; items per language; NPCs and events (the wiki is English only).
    /// </summary>
    public sealed class Gw2Data : IDisposable
    {
        private const string Api = "https://api.guildwars2.com/v2/";
        private const string WikiApi = "https://wiki.guildwars2.com/api.php";
        private const int CacheVersion = 3;
        // The places file has its own version, so adding to it doesn't download items and NPCs again too.
        // 4: renown hearts, hero challenges, mastery points and adventures.
        private const int MainCacheVersion = 4;
        private const int WikiAskLimit = 5000; // the ask API stops paging around offset 5,500
        private const int MaxAttempts = 5;
        private const int ItemPageSize = 200; // the API's maximum

        /// <summary>Name given to vistas, which the API almost never names.</summary>
        public const string UnnamedVistaName = "Vista";
        // The API doesn't name these at all; the map name (context) tells them apart.
        private const string HeroChallengeName = "Hero Challenge";
        private const string MasteryPointName = "Mastery Point";

        // From /v2/files (map_waypoint, map_poi, map_vista, map_heart_empty, map_heropoint, map_adventure).
        public const string WaypointIcon = "https://render.guildwars2.com/file/32633AF8ADEA696A1EF56D3AE32D617B10D3AC57/157353.png";
        private const string PoiIcon = "https://render.guildwars2.com/file/25B230711176AB5728E86F5FC5F0BFAE48B32F6E/97461.png";
        private const string VistaIcon = "https://render.guildwars2.com/file/A2C16AF497BA3A0903A0499FFBAF531477566F10/358415.png";
        private const string HeartIcon = "https://render.guildwars2.com/file/09ACBA53B7412CC3C76E7FEF39929843C20CB0E4/102440.png";
        private const string HeroChallengeIcon = "https://render.guildwars2.com/file/B4EC6BB3FDBC42557C3CAE0CAA9E57EBF9E462E3/156626.png";
        private const string AdventureIcon = "https://render.guildwars2.com/file/94ED0C4A49295657095EF5D09CC8C4E6709E6FCA/961368.png";
        // The wiki's "Event cog (map icon)"; /v2/files has no event icon.
        public const string EventIcon = "https://render.guildwars2.com/file/9F7E0DB4681D22F7927B11B87593386FD77BFABE/102390.png";

        // Mastery point icons by the API's mastery region, as game asset ids (from the wiki's "Mastery point (…)" files).
        private static readonly Dictionary<string, int> MasteryIcons = new Dictionary<string, int>
        {
            ["Tyria"] = 962101,
            ["Maguuma"] = 962063,
            ["Desert"] = 1770234,
            ["Tundra"] = 2221470,
            ["Jade"] = 2604760,
            ["Sky"] = 3124914,
            ["Wild"] = 3379046,
            ["Magic"] = 3713114,
        };

        // The wiki's ask API stops paging at offset 5,500, so NPCs are fetched in groups by the first letter of their page.
        // Other first characters (punctuation, accented capitals) are looked up in the page list at download time.
        private static readonly string[] TitleStarts =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(c => c.ToString()).ToArray();

        // Title ranges of the page list outside A-Z and 0-9: before "0", between "9" and "A", and after "Z".
        private static readonly string[] OtherTitleRanges = { "apto=0", "apfrom=%3B&apto=A", "apfrom=%5C" };

        // Coordinates are subobjects of the page they belong to ("Miyani#Location coordinate pair 1").
        private const string CoordinatePairs = "[[Has context::Location coordinate pair]]";
        private const string CoordinatePrintouts = "|?Has coordinate data object|?Has x coordinate|?Has y coordinate";
        private const int ContinentSize = 131072; // continent coordinates run from 0 to below this

        private static readonly JsonSerializer Serializer = new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
        };

        private readonly string _cacheDirectory;
        private readonly HttpClient _http;
        // The API allows short bursts of about 300 requests and 5 per second after that.
        private readonly SemaphoreSlim _apiSlots = new SemaphoreSlim(4);
        // Fewer at once for the wiki, which is run by volunteers.
        private readonly SemaphoreSlim _wikiSlots = new SemaphoreSlim(2);

        public Gw2Data(string cacheDirectory)
        {
            _cacheDirectory = cacheDirectory;
            // Floors are up to 8 MB of JSON each, about a tenth of that gzipped.
            _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            {
                Timeout = TimeSpan.FromSeconds(60),
            };
            // The wiki refuses requests without one.
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TyriaSearch/0.2 (Blish HUD module)");
        }

        /// <summary>A named area (sector) or a whole map, used to place NPCs, whose wiki pages only name where they are.</summary>
        public sealed class Place
        {
            [JsonProperty("n")] public string Name { get; set; }
            [JsonProperty("m")] public int MapId { get; set; }
            [JsonProperty("o")] public int Continent { get; set; }
            [JsonProperty("x")] public float X { get; set; }
            [JsonProperty("y")] public float Y { get; set; }
            [JsonProperty("map")] public bool IsMap { get; set; }
            /// <summary>Maps only: the map's area on the continent, left, top, right, bottom.</summary>
            [JsonProperty("r")] public float[] Rect { get; set; }
        }

        public sealed class CacheFile
        {
            public int Version { get; set; }
            public string Language { get; set; }
            /// <summary>The game build the data was last confirmed current for.</summary>
            public int GameBuild { get; set; }
            /// <summary>When everything was last downloaded; updates in between only add or confirm.</summary>
            public DateTime DownloadedUtc { get; set; }
            /// <summary>When the data was last downloaded or confirmed current.</summary>
            public DateTime? CheckedUtc { get; set; }
            public List<SearchEntry> Entries { get; set; }
            /// <summary>Only in the landmark file.</summary>
            public List<Place> Places { get; set; }
            /// <summary>Landmark file only: <see cref="MainFingerprintAsync"/> at download time.</summary>
            public string Fingerprint { get; set; }
            /// <summary>Item file only: every id the API listed, so a later update fetches only new ones.</summary>
            public List<int> ItemIds { get; set; }
        }

        public sealed class WikiNpc
        {
            [JsonProperty("n")] public string Title { get; set; }
            [JsonProperty("l")] public List<string> Locations { get; set; }
            [JsonProperty("s")] public List<string> Services { get; set; }
            /// <summary>Exact continent positions from the page, when editors added them; about half of NPCs have them.</summary>
            [JsonProperty("p")] public List<float[]> Coordinates { get; set; }
        }

        /// <summary>A page found by a live wiki search.</summary>
        public sealed class WikiPage
        {
            public string Title { get; set; }
            /// <summary>What the page is about: NPC, Location, Item, Object…; null for general pages.</summary>
            public string Context { get; set; }
            /// <summary>Location pages only: Waypoint, Point of Interest, Dungeon, Area…</summary>
            public string LocationType { get; set; }
            public List<string> Services { get; set; }
            public List<string> Locations { get; set; }
            public List<float[]> Coordinates { get; set; }
            public int? GameId { get; set; }
        }

        public sealed class NpcCacheFile
        {
            public int Version { get; set; }
            public DateTime DownloadedUtc { get; set; }
            public List<WikiNpc> Npcs { get; set; }
            /// <summary>/v2/files ids (map_bank, map_vendor…) to icon URLs.</summary>
            public Dictionary<string, string> Icons { get; set; }
        }

        public sealed class WikiEvent
        {
            [JsonProperty("n")] public string Title { get; set; }
            /// <summary>The areas the event starts in, by wiki page name ("Shaemoor Fields").</summary>
            [JsonProperty("s")] public List<string> StartsIn { get; set; }
            /// <summary>Where it is, widest first: "Tyria; Kryta; Queensdale; Shaemoor Fields", or "Various".</summary>
            [JsonProperty("t")] public string LocationTree { get; set; }
            [JsonProperty("v")] public int Level { get; set; }
            /// <summary>"Dynamic event", "Group event"…</summary>
            [JsonProperty("e")] public string Type { get; set; }
            /// <summary>Exact continent positions from the page, when editors added them.</summary>
            [JsonProperty("p")] public List<float[]> Coordinates { get; set; }
        }

        public sealed class EventCacheFile
        {
            public int Version { get; set; }
            public DateTime DownloadedUtc { get; set; }
            public List<WikiEvent> Events { get; set; }
        }

        private string MainPath(string language) => Path.Combine(_cacheDirectory, $"search-data-{language}.json");
        private string ItemsPath(string language) => Path.Combine(_cacheDirectory, $"search-items-{language}.json");
        private string NpcsPath => Path.Combine(_cacheDirectory, "search-npcs.json");
        private string EventsPath => Path.Combine(_cacheDirectory, "search-events.json");

        // Null if there is no saved data, or it's from an older version of the module.
        public CacheFile LoadMain(string language) => Valid(Load<CacheFile>(MainPath(language)), MainCacheVersion);
        public CacheFile LoadItems(string language) => Valid(Load<CacheFile>(ItemsPath(language)), CacheVersion);

        public NpcCacheFile LoadNpcs()
        {
            var cache = Load<NpcCacheFile>(NpcsPath);
            return cache?.Version == CacheVersion && cache.Npcs != null ? cache : null;
        }

        public EventCacheFile LoadEvents()
        {
            var cache = Load<EventCacheFile>(EventsPath);
            return cache?.Version == CacheVersion && cache.Events != null ? cache : null;
        }

        private static CacheFile Valid(CacheFile cache, int version) => cache?.Version == version && cache.Entries != null ? cache : null;

        private static T Load<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            using (var reader = new JsonTextReader(File.OpenText(path))) return Serializer.Deserialize<T>(reader);
        }

        private void Save(string path, object value)
        {
            // Write then move, so a crash mid-write can't leave a broken cache behind.
            Directory.CreateDirectory(_cacheDirectory);
            string temp = path + ".tmp";
            using (var writer = File.CreateText(temp)) Serializer.Serialize(writer, value);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        // ---- Landmarks, skills and traits ----

        /// <summary>
        /// Changes when maps, skills or traits are added or removed: three small id lists instead of the full data,
        /// which is every map floor at up to 8 MB each. The same in every language.
        /// </summary>
        public async Task<string> MainFingerprintAsync(CancellationToken ct)
        {
            var lists = await Task.WhenAll(new[] { "maps", "skills", "traits" }.Select(e => GetAsync<List<int>>($"{Api}{e}", _apiSlots, ct)));
            string text = string.Join("|", lists.Select(ids => string.Join(",", ids.OrderBy(id => id))));
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>Records that saved landmark data is still current for this build, without downloading it.</summary>
        public void ConfirmMain(CacheFile cache, int gameBuild)
        {
            cache.GameBuild = gameBuild;
            cache.CheckedUtc = DateTime.UtcNow;
            Save(MainPath(cache.Language), cache);
        }

        public async Task<CacheFile> DownloadMainAsync(string language, int gameBuild, string fingerprint, IProgress<string> progress, CancellationToken ct)
        {
            var places = new List<Place>();
            var entries = await DownloadLandmarksAsync(language, places, progress, ct);
            progress.Report("skills and traits…");
            entries.AddRange(await DownloadSkillsAndTraitsAsync(language, ct));

            var cache = new CacheFile
            {
                Version = MainCacheVersion,
                Language = language,
                GameBuild = gameBuild,
                DownloadedUtc = DateTime.UtcNow,
                CheckedUtc = DateTime.UtcNow,
                Entries = entries,
                Places = places,
                Fingerprint = fingerprint,
            };
            Save(MainPath(language), cache);
            return cache;
        }

        private async Task<List<SearchEntry>> DownloadLandmarksAsync(string language, List<Place> places, IProgress<string> progress, CancellationToken ct)
        {
            // Tyria (1) and the Mists (2). Maps are spread over many floors, and some (Path of Fire) are missing from
            // their own default floor, so every floor is read and duplicates are dropped.
            var floors = new List<(int continent, int floor)>();
            foreach (int continent in new[] { 1, 2 })
            {
                var ids = await GetAsync<List<int>>($"{Api}continents/{continent}/floors", _apiSlots, ct);
                floors.AddRange(ids.Select(id => (continent, id)));
            }

            int done = 0;
            var results = await Task.WhenAll(floors.Select(async f =>
            {
                var floor = await GetAsync<FloorDto>($"{Api}continents/{f.continent}/floors/{f.floor}?lang={language}", _apiSlots, ct);
                progress.Report($"map data, {Interlocked.Increment(ref done)} of {floors.Count} floors…");
                return (f.continent, floor);
            }));

            var seenLinks = new HashSet<string>(StringComparer.Ordinal);
            var seenMaps = new HashSet<int>();
            var seenSectors = new HashSet<(int, string)>();
            var entries = new List<SearchEntry>();
            foreach (var (continent, floor) in results)
            foreach (var region in floor?.Regions?.Values ?? Enumerable.Empty<RegionDto>())
            foreach (var map in region.Maps?.Values ?? Enumerable.Empty<MapDto>())
            {
                string mapName = map.Name?.Trim() ?? "";
                if (seenMaps.Add(map.Id) && mapName.Length > 0 && map.ContinentRect?.Length == 2)
                {
                    places.Add(new Place
                    {
                        Name = mapName,
                        MapId = map.Id,
                        Continent = continent,
                        X = (map.ContinentRect[0][0] + map.ContinentRect[1][0]) / 2,
                        Y = (map.ContinentRect[0][1] + map.ContinentRect[1][1]) / 2,
                        IsMap = true,
                        Rect = new[] { map.ContinentRect[0][0], map.ContinentRect[0][1], map.ContinentRect[1][0], map.ContinentRect[1][1] },
                    });
                }

                foreach (var sector in map.Sectors?.Values ?? Enumerable.Empty<SectorDto>())
                {
                    string name = sector.Name?.Trim();
                    if (string.IsNullOrEmpty(name) || sector.Coord?.Length != 2 || !seenSectors.Add((map.Id, name))) continue;
                    places.Add(new Place { Name = name, MapId = map.Id, Continent = continent, X = sector.Coord[0], Y = sector.Coord[1] });
                }

                foreach (var poi in map.PointsOfInterest?.Values ?? Enumerable.Empty<PoiDto>())
                {
                    if (string.IsNullOrWhiteSpace(poi.ChatLink) || !seenLinks.Add(poi.ChatLink)) continue;
                    var kind = poi.Type == "waypoint" ? EntryKind.Waypoint : poi.Type == "vista" ? EntryKind.Vista : EntryKind.PointOfInterest;
                    string name = poi.Name?.Trim();
                    // Almost no vistas have names; the map name tells them apart. Other unnamed points can't be searched for.
                    if (string.IsNullOrEmpty(name))
                    {
                        if (kind != EntryKind.Vista) continue;
                        name = UnnamedVistaName;
                    }

                    entries.Add(new SearchEntry
                    {
                        Kind = kind,
                        ChatLink = poi.ChatLink,
                        Name = name,
                        Context = mapName,
                        Icon = kind == EntryKind.Waypoint ? WaypointIcon : kind == EntryKind.Vista ? VistaIcon : PoiIcon,
                        MapId = map.Id,
                        X = poi.Coord?.Length == 2 ? poi.Coord[0] : (float?)null,
                        Y = poi.Coord?.Length == 2 ? poi.Coord[1] : (float?)null,
                    });
                }

                AddActivities(map, mapName, entries, seenLinks);
            }

            // Hearts, hero challenges, mastery points and adventures have no chat link of their own: give them the nearest waypoint's.
            var waypoints = entries.Where(e => e.Kind == EntryKind.Waypoint && e.X.HasValue).ToLookup(e => e.MapId);
            foreach (var entry in entries.Where(e => e.ChatLink == null && e.X.HasValue))
            {
                var nearest = waypoints[entry.MapId]
                    .OrderBy(w => (w.X.Value - entry.X.Value) * (w.X.Value - entry.X.Value) + (w.Y.Value - entry.Y.Value) * (w.Y.Value - entry.Y.Value))
                    .FirstOrDefault();
                entry.ChatLink = nearest?.ChatLink;
                entry.LinkIsNearestWaypoint = nearest != null;
            }
            return entries;
        }

        /// <summary>
        /// Renown hearts, hero challenges, mastery points and adventures on one map. Keys other than chat links are the
        /// same in every language, so a second search language finds its names. <paramref name="seen"/> drops the
        /// copies on other floors.
        /// </summary>
        private static void AddActivities(MapDto map, string mapName, List<SearchEntry> entries, HashSet<string> seen)
        {
            void Add(EntryKind kind, string key, string name, string context, string icon, float[] coord)
            {
                if (coord?.Length != 2 || string.IsNullOrWhiteSpace(name) || !seen.Add(key)) return;
                entries.Add(new SearchEntry
                {
                    Kind = kind,
                    Id = key,
                    Name = name.Trim(),
                    Context = context,
                    Icon = icon,
                    MapId = map.Id,
                    X = coord[0],
                    Y = coord[1],
                });
            }

            foreach (var task in map.Tasks?.Values ?? Enumerable.Empty<TaskDto>())
            {
                // The wiki's page title is the objective without its full stop.
                string name = task.Objective?.Trim().TrimEnd('.');
                string context = task.Level > 0 ? $"{mapName} · Level {task.Level}" : mapName;
                // Not the API's chat link: it's a point of interest link made from the heart's id, usually some other map's waypoint.
                Add(EntryKind.RenownHeart, $"heart:{task.Id}", name, context, HeartIcon, task.Coord);
            }

            // Hero challenge ids ("0-7") only count within a map.
            foreach (var challenge in map.SkillChallenges ?? new List<SkillChallengeDto>())
                Add(EntryKind.HeroChallenge, $"hero:{map.Id}:{challenge.Id}", HeroChallengeName, mapName, HeroChallengeIcon, challenge.Coord);

            foreach (var point in map.MasteryPoints ?? new List<MasteryPointDto>())
            {
                string icon = point.Region != null && MasteryIcons.TryGetValue(point.Region, out int asset) ? SearchEntry.AssetIcon + asset : null;
                Add(EntryKind.MasteryPoint, $"mastery:{point.Id}", MasteryPointName, mapName, icon, point.Coord);
            }

            foreach (var adventure in map.Adventures ?? new List<AdventureDto>())
                Add(EntryKind.Adventure, $"adventure:{adventure.Id}", adventure.Name, mapName, AdventureIcon, adventure.Coord);
        }

        private async Task<List<SearchEntry>> DownloadSkillsAndTraitsAsync(string language, CancellationToken ct)
        {
            var skillsTask = GetAsync<List<SkillDto>>($"{Api}skills?ids=all&lang={language}", _apiSlots, ct);
            var traitsTask = GetAsync<List<TraitDto>>($"{Api}traits?ids=all&lang={language}", _apiSlots, ct);
            var specsTask = GetAsync<List<SpecializationDto>>($"{Api}specializations?ids=all&lang={language}", _apiSlots, ct);
            var professionsTask = GetAsync<List<ProfessionDto>>($"{Api}professions?ids=all&lang={language}", _apiSlots, ct);
            await Task.WhenAll(skillsTask, traitsTask, specsTask, professionsTask);

            // Skills and specializations name professions by English id; show the translated name.
            var professions = professionsTask.Result.ToDictionary(p => p.Id, p => p.Name);
            string Profession(string id) => id != null && professions.TryGetValue(id, out var name) ? name : id;
            var specs = specsTask.Result.ToDictionary(s => s.Id);

            var entries = new List<SearchEntry>();
            foreach (var skill in skillsTask.Result)
            {
                if (string.IsNullOrWhiteSpace(skill.Name) || string.IsNullOrWhiteSpace(skill.ChatLink)) continue;
                var context = (skill.Professions ?? new List<string>()).Select(Profession).ToList();
                if (!string.IsNullOrEmpty(skill.Slot)) context.Add(skill.Slot.Replace('_', ' '));
                entries.Add(new SearchEntry
                {
                    Kind = EntryKind.Skill,
                    ChatLink = skill.ChatLink,
                    Name = skill.Name.Trim(),
                    Context = string.Join(" · ", context), // empty for NPC and monster skills
                    Icon = skill.Icon,
                });
            }

            foreach (var trait in traitsTask.Result)
            {
                if (string.IsNullOrWhiteSpace(trait.Name)) continue;
                var context = new List<string>();
                if (specs.TryGetValue(trait.Specialization, out var spec))
                {
                    context.Add(Profession(spec.Profession));
                    context.Add(spec.Name);
                }
                if (!string.IsNullOrEmpty(trait.Slot)) context.Add(trait.Slot);
                entries.Add(new SearchEntry
                {
                    Kind = EntryKind.Trait,
                    // The API leaves trait chat links out; they're built from the id.
                    ChatLink = new TraitChatLink { TraitId = trait.Id }.ToString(),
                    Name = trait.Name.Trim(),
                    Context = string.Join(" · ", context),
                    Icon = trait.Icon,
                });
            }
            return entries;
        }

        // ---- Items ----

        /// <summary>About 75,000 items in pages of 200; takes a few minutes.</summary>
        public async Task<CacheFile> DownloadItemsAsync(string language, int gameBuild, IProgress<string> progress, CancellationToken ct)
        {
            var ids = await GetAsync<List<int>>($"{Api}items", _apiSlots, ct);
            int pages = (ids.Count + ItemPageSize - 1) / ItemPageSize;

            int done = 0;
            var results = await Task.WhenAll(Enumerable.Range(0, pages).Select(async page =>
            {
                var items = await GetAsync<List<ItemDto>>($"{Api}items?page={page}&page_size={ItemPageSize}&lang={language}", _apiSlots, ct);
                progress.Report($"items, {Interlocked.Increment(ref done)} of {pages} pages…");
                return items;
            }));

            var cache = new CacheFile
            {
                Version = CacheVersion,
                Language = language,
                GameBuild = gameBuild,
                DownloadedUtc = DateTime.UtcNow,
                CheckedUtc = DateTime.UtcNow,
                Entries = ItemEntries(results.SelectMany(r => r ?? new List<ItemDto>())),
                ItemIds = ids,
            };
            Save(ItemsPath(language), cache);
            return cache;
        }

        /// <summary>
        /// After a game update: fetches only items added since the saved data, usually one or two requests.
        /// Changes to existing items wait for the next full download. Saved data without an id list is downloaded again.
        /// </summary>
        public async Task<CacheFile> UpdateItemsAsync(CacheFile cache, int gameBuild, IProgress<string> progress, CancellationToken ct)
        {
            if (cache.ItemIds == null) return await DownloadItemsAsync(cache.Language, gameBuild, progress, ct);

            var ids = await GetAsync<List<int>>($"{Api}items", _apiSlots, ct);
            var known = new HashSet<int>(cache.ItemIds);
            var added = ids.Where(id => !known.Contains(id)).ToList();
            progress.Report($"{added.Count} new items…");
            var pages = await Task.WhenAll(Enumerable.Range(0, (added.Count + ItemPageSize - 1) / ItemPageSize).Select(page =>
                GetAsync<List<ItemDto>>($"{Api}items?ids={string.Join(",", added.Skip(page * ItemPageSize).Take(ItemPageSize))}&lang={cache.Language}", _apiSlots, ct)));

            // A new file rather than changing the one the current index was built from.
            var updated = new CacheFile
            {
                Version = CacheVersion,
                Language = cache.Language,
                GameBuild = gameBuild,
                DownloadedUtc = cache.DownloadedUtc,
                CheckedUtc = DateTime.UtcNow,
                Entries = cache.Entries.Concat(ItemEntries(pages.SelectMany(p => p ?? new List<ItemDto>()))).ToList(),
                ItemIds = ids,
            };
            Save(ItemsPath(cache.Language), updated);
            return updated;
        }

        private static List<SearchEntry> ItemEntries(IEnumerable<ItemDto> items)
        {
            var entries = new List<SearchEntry>();
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.ChatLink)) continue;
                // "Greatsword" says more than "Weapon"; "Default" and "Generic" say nothing.
                string detail = item.Details?.Type;
                string type = string.IsNullOrEmpty(detail) || detail == "Default" || detail == "Generic" ? item.Type : detail;
                var context = new List<string> { item.Rarity, SplitWords(type) };
                if (item.Level > 0) context.Add($"Level {item.Level}");
                entries.Add(new SearchEntry
                {
                    Kind = EntryKind.Item,
                    ChatLink = item.ChatLink,
                    Name = item.Name.Trim(),
                    Context = string.Join(" · ", context.Where(c => !string.IsNullOrEmpty(c))),
                    Icon = item.Icon,
                    Rarity = item.Rarity,
                });
            }
            return entries;
        }

        /// <summary>"UpgradeComponent" → "Upgrade Component".</summary>
        private static string SplitWords(string text) => text == null ? null : Regex.Replace(text, "(?<=[a-z])(?=[A-Z])", " ");

        // ---- NPCs ----

        /// <summary>About 20,000 NPCs and 11,000 coordinates in roughly 150 wiki requests; takes a few minutes.</summary>
        public async Task<NpcCacheFile> DownloadNpcsAsync(IProgress<string> progress, CancellationToken ct)
        {
            var iconsTask = GetAsync<List<FileDto>>($"{Api}files?ids=all", _apiSlots, ct);

            var starts = await AllTitleStartsAsync(ct);
            int done = 0, total = starts.Count + 1;
            void Step() => progress.Report($"NPCs from the wiki, {Interlocked.Increment(ref done)} of {total} parts…");

            var groupsTask = Task.WhenAll(starts.Select(async start =>
            {
                var (pages, _) = await AskAllAsync(
                    $"[[Has context::NPC]][[Is historical::false]][[~{start}*]]|?Located in|?Has service", ct);
                Step();
                // shortcut: a letter with more than ~5,000 NPCs would be cut short; the largest has about 2,300.
                return pages.Select(page => new WikiNpc
                {
                    Title = page.Name,
                    Locations = PageNames(page.Value["printouts"]?["Located in"]),
                    Services = PageNames(page.Value["printouts"]?["Has service"]),
                }).ToList();
            }));
            var coordinatesTask = DownloadCoordinatesAsync(
                "[[Has coordinate data object.Has context::NPC]][[Has coordinate data object.Is historical::false]]", ct)
                .ContinueWith(t => { Step(); return t.Result; }, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

            var npcs = (await groupsTask).SelectMany(g => g).GroupBy(n => n.Title).Select(g => g.First()).ToList();
            var coordinates = await coordinatesTask;
            foreach (var npc in npcs)
                if (coordinates.TryGetValue(npc.Title, out var points)) npc.Coordinates = points;

            var cache = new NpcCacheFile
            {
                Version = CacheVersion,
                DownloadedUtc = DateTime.UtcNow,
                Npcs = npcs,
                Icons = (await iconsTask).ToDictionary(f => f.Id, f => f.Icon),
            };
            Save(NpcsPath, cache);
            return cache;
        }

        // ---- Events ----

        /// <summary>
        /// About 4,500 current events and their coordinates in roughly 30 wiki requests. That's close to the ask API's
        /// paging limit: if the list is cut short, it's fetched again in groups by first letter, like NPCs.
        /// </summary>
        public async Task<EventCacheFile> DownloadEventsAsync(IProgress<string> progress, CancellationToken ct)
        {
            const string condition = "[[Has context::Event]][[Is historical::false]]";
            const string printouts = "|?Starts in|?Located in tree|?Has event level|?Has event type";
            progress.Report("events from the wiki…");

            var coordinatesTask = DownloadCoordinatesAsync(
                "[[Has coordinate data object.Has context::Event]][[Has coordinate data object.Is historical::false]]", ct);
            var (pages, complete) = await AskAllAsync(condition + printouts, ct);
            if (!complete)
            {
                progress.Report("events from the wiki, in parts…");
                var starts = await AllTitleStartsAsync(ct);
                var groups = await Task.WhenAll(starts.Select(async start => (await AskAllAsync($"{condition}[[~{start}*]]{printouts}", ct)).results));
                pages = groups.SelectMany(g => g).ToList();
            }

            var coordinates = await coordinatesTask;
            var events = pages.GroupBy(p => p.Name).Select(g => g.First()).Select(page =>
            {
                var printout = page.Value["printouts"];
                var level = (printout?["Has event level"] as JArray)?.FirstOrDefault();
                coordinates.TryGetValue(page.Name, out var points);
                return new WikiEvent
                {
                    Title = page.Name,
                    StartsIn = PageNames(printout?["Starts in"]),
                    LocationTree = (printout?["Located in tree"] as JArray)?.FirstOrDefault()?.Value<string>(),
                    Level = level?.Type == JTokenType.Integer || level?.Type == JTokenType.Float ? level.Value<int>() : 0,
                    Type = (printout?["Has event type"] as JArray)?.FirstOrDefault()?.Value<string>(),
                    Coordinates = points,
                };
            }).ToList();

            var cache = new EventCacheFile { Version = CacheVersion, DownloadedUtc = DateTime.UtcNow, Events = events };
            Save(EventsPath, cache);
            return cache;
        }

        private async Task<List<string>> AllTitleStartsAsync(CancellationToken ct) => TitleStarts.Concat(await OtherTitleStartsAsync(ct)).ToList();

        /// <summary>First characters of main-namespace pages outside A-Z and 0-9, such as '"', '(' and 'É'. A few requests.</summary>
        private async Task<HashSet<string>> OtherTitleStartsAsync(CancellationToken ct)
        {
            var starts = new HashSet<string>();
            foreach (string range in OtherTitleRanges)
            {
                for (string next = ""; next != null;)
                {
                    var response = await GetAsync<JObject>(
                        $"{WikiApi}?action=query&list=allpages&format=json&apnamespace=0&apfilterredir=nonredirects&aplimit=max&{range}{next}", _wikiSlots, ct);
                    var pages = response["query"]?["allpages"] as JArray ?? throw new HttpRequestException("The wiki's answer had no page list.");
                    foreach (string title in pages.Select(p => p["title"]?.Value<string>()).Where(t => !string.IsNullOrEmpty(t)))
                        starts.Add(title.Substring(0, char.IsHighSurrogate(title[0]) && title.Length > 1 ? 2 : 1));
                    string apcontinue = response["continue"]?["apcontinue"]?.Value<string>();
                    next = apcontinue == null ? null : $"&apcontinue={Uri.EscapeDataString(apcontinue)}";
                }
            }
            starts.ExceptWith(TitleStarts); // the range ends include "0" and "A" themselves
            // shortcut: '?' and '*' are wildcards in ask patterns and would match every page, so those titles are missed;
            // the wiki has one "?" page and it is no NPC or event. Find an escape if any ever start with them.
            starts.ExceptWith(new[] { "?", "*" });
            return starts;
        }

        /// <summary>
        /// Coordinates of every page matching the condition, by page title. Asked for in slices of the x axis;
        /// a slice too big for the ask API's paging limit is split in half until it fits.
        /// </summary>
        private async Task<Dictionary<string, List<float[]>>> DownloadCoordinatesAsync(string condition, CancellationToken ct)
        {
            var pairs = new Dictionary<string, (string page, float[] point)>(); // by subobject, as slice edges overlap
            async Task FetchSlice(int from, int to)
            {
                var (results, complete) = await AskAllAsync(
                    $"{CoordinatePairs}{condition}[[Has x coordinate::≥{from}]][[Has x coordinate::<{to}]]{CoordinatePrintouts}", ct);
                if (!complete && to - from > 1)
                {
                    int middle = (from + to) / 2;
                    await Task.WhenAll(FetchSlice(from, middle), FetchSlice(middle, to));
                    return;
                }
                lock (pairs)
                {
                    foreach (var result in results)
                    {
                        var pair = ReadCoordinatePair(result);
                        if (pair.HasValue) pairs[result.Name] = pair.Value;
                    }
                }
            }

            const int slices = 8; // start several at once; most are well under the limit
            await Task.WhenAll(Enumerable.Range(0, slices).Select(i => FetchSlice(i * ContinentSize / slices, (i + 1) * ContinentSize / slices)));
            return pairs.Values.GroupBy(p => p.page).ToDictionary(g => g.Key, g => g.Select(p => p.point).ToList());
        }

        private static (string page, float[] point)? ReadCoordinatePair(JProperty result)
        {
            var printouts = result.Value["printouts"];
            string page = (printouts?["Has coordinate data object"] as JArray)?.FirstOrDefault()?["fulltext"]?.Value<string>();
            var x = (printouts?["Has x coordinate"] as JArray)?.FirstOrDefault();
            var y = (printouts?["Has y coordinate"] as JArray)?.FirstOrDefault();
            if (page == null || x == null || y == null) return null;
            return (page, new[] { x.Value<float>(), y.Value<float>() });
        }

        /// <summary>
        /// Runs a wiki ask query through all its pages. Not complete when the wiki's paging limit cut it short.
        /// </summary>
        private async Task<(List<JProperty> results, bool complete)> AskAllAsync(string query, CancellationToken ct)
        {
            var results = new List<JProperty>();
            for (int offset = 0; ;)
            {
                var response = await AskAsync($"{query}|limit=500|offset={offset}", ct);
                results.AddRange(AskResults(response));

                int? next = response["query-continue-offset"]?.Value<int>();
                if (next == null) return (results, true);
                // Past the wiki's limit the offset starts over instead of stopping.
                if (next <= offset || next >= WikiAskLimit) return (results, false);
                offset = next.Value;
            }
        }

        private Task<JObject> AskAsync(string query, CancellationToken ct) =>
            GetAsync<JObject>($"{WikiApi}?action=ask&format=json&query={Uri.EscapeDataString(query)}", _wikiSlots, ct);

        private static IEnumerable<JProperty> AskResults(JObject response)
        {
            var results = response["query"]?["results"] ?? throw new HttpRequestException("The wiki's answer had no results section.");
            // An empty result is a JSON array rather than an object.
            return (results as JObject)?.Properties() ?? Enumerable.Empty<JProperty>();
        }

        private static List<string> PageNames(JToken values) =>
            (values as JArray)?.Select(v => v["fulltext"]?.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();

        // ---- Live wiki search ----

        /// <summary>
        /// Searches the wiki's text and returns the best pages with what they are, where they are and any exact coordinates.
        /// Three requests, about a second.
        /// </summary>
        public async Task<List<WikiPage>> SearchWikiAsync(string text, int max, CancellationToken ct)
        {
            var search = await GetAsync<JObject>(
                $"{WikiApi}?action=query&list=search&format=json&srnamespace=0&srlimit={max}&srsearch={Uri.EscapeDataString(text)}", _wikiSlots, ct);
            var titles = (search["query"]?["search"] as JArray)?.Select(h => h["title"]?.Value<string>()).Where(t => t != null).ToList()
                         ?? new List<string>();
            if (titles.Count == 0) return new List<WikiPage>();

            string pageList = string.Join("||", titles);
            var detailsTask = AskAsync($"[[{pageList}]]|?Has context|?Has location type|?Has service|?Located in|?Has game id|limit={max}", ct);
            var coordinatesTask = AskAsync($"{CoordinatePairs}[[Has coordinate data object::{pageList}]]{CoordinatePrintouts}|limit=500", ct);

            var details = AskResults(await detailsTask).ToDictionary(r => r.Name, r => r.Value["printouts"]);
            var coordinates = AskResults(await coordinatesTask).Select(ReadCoordinatePair).Where(p => p.HasValue).Select(p => p.Value)
                .GroupBy(p => p.page).ToDictionary(g => g.Key, g => g.Select(p => p.point).ToList());

            // In the search's order, which is by relevance.
            return titles.Select(title =>
            {
                details.TryGetValue(title, out var printouts);
                coordinates.TryGetValue(title, out var points);
                var gameId = (printouts?["Has game id"] as JArray)?.FirstOrDefault();
                return new WikiPage
                {
                    Title = title,
                    Context = (printouts?["Has context"] as JArray)?.FirstOrDefault()?.Value<string>(),
                    LocationType = (printouts?["Has location type"] as JArray)?.FirstOrDefault()?.Value<string>(),
                    Services = PageNames(printouts?["Has service"]),
                    Locations = PageNames(printouts?["Located in"]),
                    Coordinates = points ?? new List<float[]>(),
                    GameId = gameId?.Type == JTokenType.Integer || gameId?.Type == JTokenType.Float ? gameId.Value<int>() : (int?)null,
                };
            }).ToList();
        }

        // ---- HTTP ----

        /// <summary>Retries rate limits (429), server errors (the API often returns 504) and timeouts.</summary>
        private async Task<T> GetAsync<T>(string url, SemaphoreSlim slots, CancellationToken ct)
        {
            for (int attempt = 1; ; attempt++)
            {
                string failure;
                await slots.WaitAsync(ct);
                try
                {
                    using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        int status = (int)response.StatusCode;
                        if (response.IsSuccessStatusCode)
                        {
                            using (var stream = await response.Content.ReadAsStreamAsync())
                            using (var reader = new JsonTextReader(new StreamReader(stream)))
                                return Serializer.Deserialize<T>(reader);
                        }
                        failure = $"HTTP {status}";
                        if (status != 429 && status < 500) attempt = MaxAttempts; // won't get better by retrying
                    }
                }
                catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is IOException) && !ct.IsCancellationRequested)
                {
                    failure = ex.Message;
                }
                finally
                {
                    slots.Release();
                }

                if (attempt >= MaxAttempts) throw new HttpRequestException($"Request {url} failed: {failure}");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }

        public void Dispose() => _http.Dispose();

        // Only the fields the search needs; Json.NET skips the rest.
        private sealed class FloorDto { [JsonProperty("regions")] public Dictionary<string, RegionDto> Regions { get; set; } }
        private sealed class RegionDto { [JsonProperty("maps")] public Dictionary<string, MapDto> Maps { get; set; } }

        private sealed class MapDto
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("continent_rect")] public float[][] ContinentRect { get; set; }
            [JsonProperty("points_of_interest")] public Dictionary<string, PoiDto> PointsOfInterest { get; set; }
            [JsonProperty("sectors")] public Dictionary<string, SectorDto> Sectors { get; set; }
            [JsonProperty("tasks")] public Dictionary<string, TaskDto> Tasks { get; set; }
            [JsonProperty("skill_challenges")] public List<SkillChallengeDto> SkillChallenges { get; set; }
            [JsonProperty("mastery_points")] public List<MasteryPointDto> MasteryPoints { get; set; }
            [JsonProperty("adventures")] public List<AdventureDto> Adventures { get; set; }
        }

        /// <summary>A renown heart.</summary>
        private sealed class TaskDto
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("objective")] public string Objective { get; set; }
            [JsonProperty("level")] public int Level { get; set; }
            [JsonProperty("coord")] public float[] Coord { get; set; }
        }

        private sealed class SkillChallengeDto
        {
            [JsonProperty("id")] public string Id { get; set; }
            [JsonProperty("coord")] public float[] Coord { get; set; }
        }

        private sealed class MasteryPointDto
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("region")] public string Region { get; set; }
            [JsonProperty("coord")] public float[] Coord { get; set; }
        }

        private sealed class AdventureDto
        {
            [JsonProperty("id")] public string Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("coord")] public float[] Coord { get; set; }
        }

        private sealed class PoiDto
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("chat_link")] public string ChatLink { get; set; }
            [JsonProperty("coord")] public float[] Coord { get; set; }
        }

        private sealed class SectorDto
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("coord")] public float[] Coord { get; set; }
        }

        private sealed class SkillDto
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("chat_link")] public string ChatLink { get; set; }
            [JsonProperty("icon")] public string Icon { get; set; }
            [JsonProperty("professions")] public List<string> Professions { get; set; }
            [JsonProperty("slot")] public string Slot { get; set; }
        }

        private sealed class TraitDto
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("icon")] public string Icon { get; set; }
            [JsonProperty("specialization")] public int Specialization { get; set; }
            [JsonProperty("slot")] public string Slot { get; set; }
        }

        private sealed class SpecializationDto
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("profession")] public string Profession { get; set; }
        }

        private sealed class ProfessionDto
        {
            [JsonProperty("id")] public string Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
        }

        private sealed class ItemDto
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("rarity")] public string Rarity { get; set; }
            [JsonProperty("level")] public int Level { get; set; }
            [JsonProperty("chat_link")] public string ChatLink { get; set; }
            [JsonProperty("icon")] public string Icon { get; set; }
            [JsonProperty("details")] public ItemDetailsDto Details { get; set; }
        }

        private sealed class ItemDetailsDto { [JsonProperty("type")] public string Type { get; set; } }

        private sealed class FileDto
        {
            [JsonProperty("id")] public string Id { get; set; }
            [JsonProperty("icon")] public string Icon { get; set; }
        }
    }
}
