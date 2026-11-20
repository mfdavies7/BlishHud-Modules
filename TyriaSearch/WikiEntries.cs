using System;
using System.Collections.Generic;
using System.Linq;
using Gw2Sharp.ChatLinks;

namespace TyriaSearch
{
    /// <summary>
    /// Turns wiki data into search entries placed on the map: exact coordinates when the wiki has them, otherwise the
    /// centre of the named area. The chat link is the nearest waypoint.
    /// </summary>
    public static class WikiEntries
    {
        private const int MaxLocationsPerEntry = 10;

        // Wiki service names to /v2/files icon ids.
        private static readonly Dictionary<string, string> ServiceIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Account vault"] = "map_bank",
            ["Guild Vault"] = "map_guild_bank",
            ["Trading Post"] = "map_trading_post",
            ["Mystic Forge"] = "map_vendor_mystic_forge",
            ["Reinforce Armor"] = "map_repair",
            ["Merchant"] = "map_vendor",
            ["Vendor"] = "map_vendor",
            ["Event merchant"] = "map_vendor",
            ["Karma merchant"] = "map_vendor",
            ["Gathering Merchant"] = "map_vendor",
            ["Festival Merchant"] = "map_vendor_festival",
            ["Dungeon Merchant"] = "map_vendor_dungeon",
            ["Laurel Merchant"] = "map_vendor_laurel",
            ["Weaponsmith (vendor)"] = "map_vendor_weapons",
            ["Armorsmith (vendor)"] = "map_vendor_armor",
            ["Cultural weaponsmith"] = "map_vendor_cultural_weapons",
            ["Cultural armorsmith"] = "map_vendor_cultural_armor",
            ["Artificer"] = "map_crafting_artificer",
            ["Armorsmith"] = "map_crafting_armorsmith",
            ["Chef"] = "map_crafting_cook",
            ["Huntsman"] = "map_crafting_huntsman",
            ["Jeweler"] = "map_crafting_jeweler",
            ["Leatherworker"] = "map_crafting_leatherworker",
            ["Scribe"] = "map_crafting_scribe",
            ["Tailor"] = "map_crafting_tailor",
            ["Weaponsmith"] = "map_crafting_weaponsmith",
            ["Guild Registrar"] = "map_guild_registrar",
            ["Renown Heart"] = "map_heart_empty",
            ["Hero challenge"] = "map_heropoint",
            ["Adventure"] = "map_adventure",
            ["Race Organizer"] = "map_adventure",
            ["Dungeon information"] = "map_dungeon",
            ["Guild Armorer"] = "map_guild_armorer",
            ["Guild Weaponsmith"] = "map_guild_weaponsmith",
            ["Guild Commendation Trader"] = "map_vendor_guild",
            ["Guild Decoration Trader"] = "map_vendor_guild",
            ["Aurillium Collector"] = "map_vendor_aurillium",
            ["Commander Trainer"] = "map_profession_trainer",
            ["Legendary vendor"] = "map_vendor",
            ["Faction Provisioner"] = "map_vendor",
            ["Research Merchant"] = "map_vendor",
            ["Skirmish Merchant"] = "map_vendor",
            ["Support Mark Vendor"] = "map_vendor",
            ["Zephyrite merchant"] = "map_vendor",
            ["Bandit Crest Collector"] = "map_vendor",
            ["Collector of Unusual Coins"] = "map_vendor",
            ["Shard Collector"] = "map_vendor",
            ["Volatile Magic"] = "map_vendor",
            ["Unbound Magic"] = "map_vendor",
            ["Fishmonger"] = "map_vendor",
            ["Fishing Supplies"] = "map_vendor",
            ["Bartender"] = "map_vendor",
        };

        // Live wiki results: location types and page types to /v2/files icon ids.
        private static readonly Dictionary<string, string> PageIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Waypoint"] = "map_waypoint",
            ["Point of Interest"] = "map_poi",
            ["Landmark"] = "map_poi",
            ["Vista"] = "map_vista",
            ["Dungeon"] = "map_dungeon",
            ["Fractal"] = "map_fractal",
            ["Raid"] = "map_raid_entrance",
            ["Event"] = "map_special_event",
            ["Renown heart"] = "map_heart_empty",
        };

        // The wiki's names for some services aren't what players search for.
        private static readonly Dictionary<string, string> ServiceNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Account vault"] = "Bank",
            ["Guild Vault"] = "Guild bank",
            ["Reinforce Armor"] = "Repair",
        };

        /// <summary>
        /// One entry per place an NPC is found, so "banker" lists every bank. Each named location takes the wiki
        /// coordinates that fall inside its map, if any, else its area's centre. Coordinates no location accounts for
        /// (the wiki says "Kryta", or doesn't list the place) become entries of their own.
        /// </summary>
        public static List<SearchEntry> BuildNpcs(Gw2Data.NpcCacheFile npcs, Places places)
        {
            var entries = new List<SearchEntry>();
            foreach (var npc in npcs.Npcs)
            {
                var services = npc.Services ?? new List<string>();
                string serviceText = string.Join(" · ", services.Select(s => ServiceNames.TryGetValue(s, out var alias) ? alias : s));
                string icon = ServiceIcon(services, npcs.Icons);

                var coordinates = npc.Coordinates ?? new List<float[]>();
                var used = new bool[coordinates.Count];
                var placed = new HashSet<(int, int, int)>(); // two wiki names, or a name and coordinates, for the same spot
                var npcEntries = new List<SearchEntry>();
                var unplaced = new List<string>();

                void Add(string key, Placement placement, string fallbackContext)
                {
                    if (placement.X.HasValue && !placed.Add((placement.MapId, (int)placement.X.Value / 10, (int)placement.Y.Value / 10))) return;
                    npcEntries.Add(Entry(EntryKind.Npc, $"npc:{npc.Title}@{key}", npc.Title, placement, places, fallbackContext, serviceText, icon));
                }

                foreach (string location in (npc.Locations ?? new List<string>()).Take(MaxLocationsPerEntry))
                {
                    var found = places.Find(location);
                    if (!found.HasValue)
                    {
                        unplaced.Add(location); // a region like "Kryta"
                        continue;
                    }

                    var placement = found.Value;
                    int exact = ClosestUnused(coordinates, used, placement, places);
                    if (exact >= 0)
                    {
                        used[exact] = true;
                        placement.X = coordinates[exact][0];
                        placement.Y = coordinates[exact][1];
                        placement.Approximate = false;
                    }
                    Add(location, placement, null);
                }

                for (int i = 0; i < coordinates.Count; i++)
                {
                    if (used[i]) continue;
                    var at = places.At(coordinates[i][0], coordinates[i][1]);
                    if (at.HasValue) Add($"{at.Value.MapId}:{(int)coordinates[i][0]},{(int)coordinates[i][1]}", at.Value, null);
                }

                // Only when nothing better is known: the region text, or no location at all.
                if (npcEntries.Count == 0)
                {
                    if (unplaced.Count == 0) unplaced.Add(null);
                    foreach (string location in unplaced) Add(location ?? "", new Placement(), location);
                }
                entries.AddRange(npcEntries);
            }
            return entries;
        }

        /// <summary>
        /// One entry per spot an event has coordinates for, else per area it starts in (that area's centre), else
        /// the last place its location path names. Context: where, then level and type ("Group event").
        /// </summary>
        public static List<SearchEntry> BuildEvents(Gw2Data.EventCacheFile events, Places places)
        {
            var entries = new List<SearchEntry>();
            foreach (var ev in events.Events)
            {
                var details = new List<string>();
                if (ev.Level > 0) details.Add($"Level {ev.Level}");
                if (!string.IsNullOrEmpty(ev.Type) && ev.Type != "Dynamic event") details.Add(ev.Type);
                string detailText = string.Join(" · ", details);

                var placed = new HashSet<(int, int, int)>();
                var eventEntries = new List<SearchEntry>();
                void Add(string key, Placement placement, string fallbackContext)
                {
                    if (placement.X.HasValue && !placed.Add((placement.MapId, (int)placement.X.Value / 10, (int)placement.Y.Value / 10))) return;
                    eventEntries.Add(Entry(EntryKind.Event, $"event:{ev.Title}@{key}", ev.Title, placement, places, fallbackContext, detailText, Gw2Data.EventIcon));
                }

                foreach (var point in (ev.Coordinates ?? new List<float[]>()).Take(MaxLocationsPerEntry))
                {
                    var at = places.At(point[0], point[1]);
                    if (at.HasValue) Add($"{at.Value.MapId}:{(int)point[0]},{(int)point[1]}", at.Value, null);
                }

                if (eventEntries.Count == 0)
                {
                    foreach (string area in (ev.StartsIn ?? new List<string>()).Take(MaxLocationsPerEntry))
                    {
                        var found = places.Find(area);
                        if (found.HasValue) Add(area, found.Value, null);
                    }
                }

                if (eventEntries.Count == 0)
                {
                    // "Tyria; Kryta; Queensdale": the narrowest place the path names, placed if it's a known area or map.
                    string narrowest = (ev.LocationTree ?? "").Split(';').Select(s => s.Trim()).LastOrDefault(s => s.Length > 0 && s != "Various");
                    var found = places.Find(narrowest);
                    Add(narrowest ?? "", found ?? new Placement(), found.HasValue ? null : narrowest ?? ev.StartsIn?.FirstOrDefault());
                }
                entries.AddRange(eventEntries);
            }
            return entries;
        }

        private static string ServiceIcon(IEnumerable<string> services, Dictionary<string, string> icons) =>
            FileIcon(services.Select(s => ServiceIcons.TryGetValue(s, out var id) ? id : null).FirstOrDefault(id => id != null), icons);

        private static string FileIcon(string id, Dictionary<string, string> icons) =>
            id != null && icons != null && icons.TryGetValue(id, out var url) ? url : null;

        /// <summary>The unused coordinates inside the placement's map, nearest its area centre; -1 if none.</summary>
        private static int ClosestUnused(List<float[]> coordinates, bool[] used, Placement placement, Places places)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < coordinates.Count; i++)
            {
                float x = coordinates[i][0], y = coordinates[i][1];
                if (used[i] || !places.Contains(placement.MapId, x, y)) continue;
                float distance = placement.X.HasValue ? (x - placement.X.Value) * (x - placement.X.Value) + (y - placement.Y.Value) * (y - placement.Y.Value) : 0;
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// A page from a live wiki search. Placed by its exact coordinates, else as a place itself (location pages),
        /// else by where the page says it is. Items with a game id get their real chat link.
        /// The icon is the downloaded item, skill or trait's, else from the page's services or type.
        /// </summary>
        /// <param name="icons">/v2/files ids to icon URLs; null when NPCs aren't downloaded.</param>
        public static SearchEntry FromPage(Gw2Data.WikiPage page, Places places, SearchIndex index, Dictionary<string, string> icons)
        {
            Placement? placement = null;
            if (places != null)
            {
                var coordinates = page.Coordinates.FirstOrDefault();
                if (coordinates != null) placement = places.At(coordinates[0], coordinates[1]);
                placement = placement ?? places.Find(page.Title);
                foreach (string location in page.Locations)
                    placement = placement ?? places.Find(location);
            }

            var entry = Entry(EntryKind.WikiPage, $"wiki:{page.Title}", page.Title, placement ?? new Placement(), places,
                placement.HasValue ? null : page.Locations.FirstOrDefault(), null, null);
            entry.Context = string.IsNullOrEmpty(entry.Context) ? page.Context ?? "" : $"{page.Context ?? "Wiki"} · {entry.Context}";

            if (page.Context == "Item" && page.GameId > 0)
            {
                // Quantity defaults to 0, which the game shows as "0 Item"; API links use 1.
                entry.ChatLink = new ItemChatLink { ItemId = page.GameId.Value, Quantity = 1 }.ToString();
                entry.LinkIsNearestWaypoint = false;
            }

            string gameLink = !(page.GameId > 0) ? null : page.Context switch
            {
                "Item" => entry.ChatLink,
                "Skill" => new SkillChatLink { SkillId = page.GameId.Value }.ToString(),
                "Trait" => new TraitChatLink { TraitId = page.GameId.Value }.ToString(),
                _ => null,
            };
            entry.Icon = (gameLink != null ? index.Find(gameLink)?.Entry.Icon : null)
                         ?? ServiceIcon(page.Services, icons)
                         ?? FileIcon(PageIcons.TryGetValue(page.LocationType ?? page.Context ?? "", out var id) ? id : null, icons)
                         ?? SearchEntry.BundledIcon + page.Context switch
                         {
                             "NPC" => "npc",
                             "Item" => "item",
                             "Skill" => "skill",
                             "Trait" => "skill",
                             "Location" => "location",
                             _ => "page",
                         };
            return entry;
        }

        internal static SearchEntry Entry(EntryKind kind, string id, string name, Placement placement, Places places,
            string fallbackContext, string services, string icon)
        {
            var context = new List<string>();
            string mapName = places?.MapName(placement.MapId);
            if (placement.Area != null && !string.Equals(placement.Area, mapName, StringComparison.OrdinalIgnoreCase)) context.Add(placement.Area);
            if (mapName != null) context.Add(mapName);
            if (fallbackContext != null) context.Add(fallbackContext);
            if (!string.IsNullOrEmpty(services)) context.Add(services);

            var waypoint = placement.X.HasValue ? places?.NearestWaypoint(placement.MapId, placement.X.Value, placement.Y.Value) : null;
            return new SearchEntry
            {
                Kind = kind,
                Id = id,
                Name = name,
                ChatLink = waypoint?.ChatLink,
                LinkIsNearestWaypoint = waypoint != null,
                Context = string.Join(" · ", context),
                Icon = icon,
                MapId = placement.MapId,
                X = placement.X,
                Y = placement.Y,
                Approximate = placement.Approximate,
            };
        }
    }
}
