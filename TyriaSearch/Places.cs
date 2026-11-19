using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TyriaSearch
{
    /// <summary>A position on the map, as precise as the source allows.</summary>
    public struct Placement
    {
        /// <summary>Named area (sector), or null for a whole map.</summary>
        public string Area;
        public int MapId;
        public float? X, Y;
        /// <summary>True when the position is an area's centre rather than the exact spot.</summary>
        public bool Approximate;
    }

    /// <summary>
    /// Answers "where is this?" for wiki data, which names places in English ("Trader's Forum") and sometimes gives exact
    /// continent coordinates without saying which map they're on.
    /// </summary>
    public sealed class Places
    {
        private static readonly Regex WikiSuffix = new Regex(@"\s*\([^)]*\)$"); // "Durmand Priory (location)"

        private readonly ILookup<int, SearchEntry> _waypoints;
        private readonly ILookup<int, Gw2Data.Place> _areasByMap;
        private readonly Dictionary<string, Gw2Data.Place> _areas;
        private readonly Dictionary<string, Gw2Data.Place> _maps;
        private readonly Dictionary<string, SearchEntry> _landmarks;
        private readonly List<Gw2Data.Place> _mapRects;
        private readonly Dictionary<int, Gw2Data.Place> _mapsById;
        private readonly Dictionary<int, string> _displayMapNames;
        private readonly Dictionary<int, string> _englishMapNames;

        /// <param name="english">English landmark data, to match the wiki's names.</param>
        /// <param name="display">Landmark data in the display language, for map names. May be the English data.</param>
        public Places(Gw2Data.CacheFile english, Gw2Data.CacheFile display)
        {
            _waypoints = english.Entries.Where(e => e.Kind == EntryKind.Waypoint && e.X.HasValue).ToLookup(e => e.MapId);
            var places = english.Places ?? new List<Gw2Data.Place>();
            int Waypoints(int mapId) => _waypoints[mapId].Count();

            // A name can belong to several maps (story instances copy whole areas); prefer the map with the most waypoints,
            // which is the open world one.
            Dictionary<string, T> ByName<T>(IEnumerable<T> items, Func<T, string> name, Func<T, int> mapId) => items
                .GroupBy(name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => Waypoints(mapId(i))).First(), StringComparer.OrdinalIgnoreCase);

            _areas = ByName(places.Where(p => !p.IsMap), p => p.Name, p => p.MapId);
            _areasByMap = places.Where(p => !p.IsMap).ToLookup(p => p.MapId);
            _maps = ByName(places.Where(p => p.IsMap), p => p.Name, p => p.MapId);
            _landmarks = ByName(english.Entries.Where(e => e.X.HasValue && e.Kind != EntryKind.Vista), e => e.Name, e => e.MapId);
            // Tyria before the Mists, then open world before instances, for coordinates that several maps cover.
            _mapRects = places.Where(p => p.IsMap && p.Rect?.Length == 4)
                .OrderBy(p => p.Continent).ThenByDescending(p => Waypoints(p.MapId)).ToList();
            _mapsById = _mapRects.GroupBy(p => p.MapId).ToDictionary(g => g.Key, g => g.First());
            _englishMapNames = places.Where(p => p.IsMap).GroupBy(p => p.MapId).ToDictionary(g => g.Key, g => g.First().Name);
            _displayMapNames = (display?.Places ?? places).Where(p => p.IsMap).GroupBy(p => p.MapId).ToDictionary(g => g.Key, g => g.First().Name);
        }

        public string MapName(int mapId) => _displayMapNames.TryGetValue(mapId, out var name) ? name : null;

        public string EnglishMapName(int mapId) => _englishMapNames.TryGetValue(mapId, out var name) ? name : null;

        /// <summary>Tyria (1) or the Mists (2); null for maps without a known area on a continent.</summary>
        public int? ContinentOf(int mapId) => _mapsById.TryGetValue(mapId, out var map) ? map.Continent : (int?)null;

        /// <summary>A wiki place name as an area (with its centre), a whole map (no position) or a landmark; null if unknown.</summary>
        public Placement? Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (string candidate in new[] { name, WikiSuffix.Replace(name, "") })
            {
                if (_areas.TryGetValue(candidate, out var area))
                    return new Placement { Area = area.Name, MapId = area.MapId, X = area.X, Y = area.Y, Approximate = true };
                if (_maps.TryGetValue(candidate, out var map))
                    return new Placement { MapId = map.MapId };
                if (_landmarks.TryGetValue(candidate, out var landmark))
                    return new Placement { Area = landmark.Name, MapId = landmark.MapId, X = landmark.X, Y = landmark.Y };
            }
            return null;
        }

        public bool Contains(int mapId, float x, float y) =>
            _mapsById.TryGetValue(mapId, out var map) && Inside(map, x, y);

        /// <summary>The map at exact coordinates, with the nearest named area; null if no map covers them.</summary>
        public Placement? At(float x, float y, int preferredMapId = 0)
        {
            var map = preferredMapId != 0 && Contains(preferredMapId, x, y)
                ? _mapsById[preferredMapId]
                : _mapRects.FirstOrDefault(m => Inside(m, x, y));
            if (map == null) return null;
            var area = _areasByMap[map.MapId].OrderBy(a => DistanceSquared(a.X, a.Y, x, y)).FirstOrDefault();
            return new Placement { Area = area?.Name, MapId = map.MapId, X = x, Y = y };
        }

        public SearchEntry NearestWaypoint(int mapId, float x, float y) =>
            _waypoints[mapId].OrderBy(w => DistanceSquared(w.X.Value, w.Y.Value, x, y)).FirstOrDefault();

        private static bool Inside(Gw2Data.Place map, float x, float y) =>
            x >= map.Rect[0] && y >= map.Rect[1] && x < map.Rect[2] && y < map.Rect[3];

        private static float DistanceSquared(float ax, float ay, float bx, float by) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by);
    }
}
