using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blish_HUD;
using Bytex.ModuleShared;
using Newtonsoft.Json;

namespace Bytex.TyriaSearch
{
    /// <summary>
    /// Notices portals the bundled list doesn't have, for the player to review; the ones they add are saved to
    /// learned-portals.json in the module's directory, in ref/portals.json's format (so they can be copied into it).
    /// A portal is taken to be used when the map changes shortly after the player moved, with the world map closed (so not a
    /// waypoint), from a spot no known portal is near. Teleport items and joining a squad member are channelled standing still.
    /// After a new portal, the next portal noticed straight back is taken as its return portal (see <see cref="ExpectReturn"/>);
    /// while the player is asked to go back, that trip skips the checks above, as they know it's through the return portal.
    /// Spots the player said not to ask about again are saved to ignored-portals.json, in the same format, and never asked about.
    /// Nor are portals to the destinations in ref/ignored-destinations.json (places joined from anywhere through the game's
    /// panels, such as fractals, guild halls, PvP and WvW) or ones the player added, saved to ignored-destinations.json.
    /// Portals from the bundled ones aren't either: their exits lead back to wherever the player came from.
    /// </summary>
    internal sealed class PortalLearner
    {
        private static readonly Logger Logger = Logger.GetLogger<PortalLearner>();

        // Continent units are about 24 inches (0.6 m). Portals are wide and the marker is at their centre.
        private const float AnyPortalRadius = 100; // another portal here: probably the same one, named differently
        private const float SameDestinationRadius = 600;
        private const float MinMove = 0.5f; // jitter while standing still
        private static readonly TimeSpan MovedWithin = TimeSpan.FromSeconds(5); // dungeon entrances ask first
        private static readonly TimeSpan MapClosedFor = TimeSpan.FromSeconds(2);

        private readonly string _path, _ignoredPath, _ignoredDestinationsPath;
        private readonly List<Portal> _learned, _ignored;
        // English map names, as the API gives them; some have stray spaces.
        private readonly HashSet<string> _bundledIgnoredDestinations;
        private readonly List<string> _ignoredDestinations;

        // The last Mumble update; it stops during loading screens, so these hold the spot before one.
        private int _tick, _mapId;
        private string _character;
        private float _x, _y;
        private DateTime _updated, _moved, _mapOpen;

        // The return trip being waited for: from the map a new portal led to, back to the map it was on.
        private int _returnFrom, _returnTo;

        /// <param name="bundledIgnoredDestinations">The contents of ref/ignored-destinations.json.</param>
        public PortalLearner(string directory, Stream bundledIgnoredDestinations)
        {
            _path = Path.Combine(directory, "learned-portals.json");
            _learned = Load(_path);
            _ignoredPath = Path.Combine(directory, "ignored-portals.json");
            _ignored = Load(_ignoredPath);
            _bundledIgnoredDestinations = new HashSet<string>(ReadNames(bundledIgnoredDestinations).Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
            _ignoredDestinationsPath = Path.Combine(directory, "ignored-destinations.json");
            _ignoredDestinations = LoadNames(_ignoredDestinationsPath);
        }

        public IReadOnlyList<Portal> Learned => _learned;

        /// <summary>The spots the player said not to ask about again.</summary>
        public IReadOnlyList<Portal> Ignored => _ignored;

        /// <summary>The destinations the player said not to ask about portals to; not the bundled ones.</summary>
        public IReadOnlyList<string> IgnoredDestinations => _ignoredDestinations;

        /// <summary>The learned portals changed and were saved: true when portals were added or removed, false for an edit.</summary>
        public event Action<bool> Changed;

        private static List<Portal> Load(string path)
        {
            try
            {
                if (File.Exists(path))
                    using (var stream = File.OpenRead(path))
                        return Portal.Load(stream) ?? new List<Portal>();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Couldn't read {path}; starting it again.");
            }
            return new List<Portal>();
        }

        private static List<string> ReadNames(Stream json)
        {
            using (var reader = new JsonTextReader(new StreamReader(json)))
                return new JsonSerializer().Deserialize<List<string>>(reader) ?? new List<string>();
        }

        private static List<string> LoadNames(string path)
        {
            try
            {
                if (File.Exists(path))
                    using (var stream = File.OpenRead(path))
                        return ReadNames(stream);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Couldn't read {path}; starting it again.");
            }
            return new List<string>();
        }

        private bool IsIgnoredDestination(string map)
        {
            map = map.Trim();
            return _bundledIgnoredDestinations.Contains(map) || _ignoredDestinations.Any(d => string.Equals(d.Trim(), map, StringComparison.OrdinalIgnoreCase));
        }

        public bool ExpectingReturn => _returnFrom != 0;

        /// <summary>Marks the next portal noticed from the portal's destination map back to its map as the return portal.</summary>
        public void ExpectReturn(int fromMap, int toMap) => (_returnFrom, _returnTo) = (fromMap, toMap);

        public void CancelReturn() => (_returnFrom, _returnTo) = (0, 0);

        /// <summary>Call every frame. Returns a portal just taken that isn't known, else null; nothing is saved until <see cref="Add"/>.</summary>
        /// <param name="places">Null until map data is loaded; nothing is noticed before then.</param>
        /// <param name="known">The bundled and learned portals, and those waiting for review.</param>
        /// <param name="isReturn">The portal is the return trip <see cref="ExpectReturn"/> asked for.</param>
        public Portal Update(Places places, IReadOnlyList<Portal> known, out bool isReturn)
        {
            isReturn = false;
            var mumble = GameService.Gw2Mumble;
            if (!mumble.IsAvailable) return null;
            int tick = mumble.RawClient.Tick;
            if (tick == _tick) return null;
            _tick = tick;

            var now = DateTime.UtcNow;
            int mapId = mumble.CurrentMap.Id;
            string character = mumble.PlayerCharacter.Name;
            var position = mumble.RawClient.PlayerLocationMap;
            float x = (float)position.X, y = (float)position.Y;
            Portal learned = null;

            if (mapId != _mapId || character != _character)
            {
                if (_mapId != 0 && mapId != 0 && character == _character)
                    learned = MapChanged(places, known, mapId);
                isReturn = learned != null && _mapId == _returnFrom && mapId == _returnTo;
                if (_mapId == _returnFrom) CancelReturn(); // went back, or somewhere else
                _moved = DateTime.MinValue;
            }
            else if (Math.Abs(x - _x) > MinMove || Math.Abs(y - _y) > MinMove)
            {
                _moved = now;
            }

            if (mumble.UI.IsMapOpen) _mapOpen = now;
            _mapId = mapId;
            _character = character;
            _x = x;
            _y = y;
            _updated = now;
            return learned;
        }

        /// <summary>The portal just taken from the spot before the loading screen, if it's one to ask about. Logs why not.</summary>
        private Portal MapChanged(Places places, IReadOnlyList<Portal> known, int toMap)
        {
            int fromMap = _mapId;
            float x = _x, y = _y;
            double movedAgo = (_updated - _moved).TotalSeconds, mapOpenAgo = (_updated - _mapOpen).TotalSeconds;
            Portal Skip(string why)
            {
                Logger.Info($"Map {fromMap} to {toMap} from {x:0.0}, {y:0.0} not taken as a new portal: {why}.");
                return null;
            }
            // The player was asked to go back through the return portal, so it isn't a waypoint, teleport or known portal.
            bool expected = fromMap == _returnFrom && toMap == _returnTo;

            if (!expected && movedAgo > MovedWithin.TotalSeconds) return Skip(_moved == DateTime.MinValue ? "didn't move on the map" : $"last moved {movedAgo:0.0} s before");
            if (!expected && mapOpenAgo < MapClosedFor.TotalSeconds) return Skip($"world map open {mapOpenAgo:0.0} s before (a waypoint)");
            if (places == null) return Skip("map data not loaded yet");
            // Instances and maps the API doesn't place on a continent can't be drawn or searched by position.
            int? continent = places.ContinentOf(fromMap);
            string destination = places.EnglishMapName(toMap);
            if (continent == null || !places.Contains(fromMap, x, y)) return Skip("not on a map the API places on a continent");
            string origin = places.EnglishMapName(fromMap);
            if (origin != null && _bundledIgnoredDestinations.Contains(origin.Trim())) return Skip($"portals from {origin} aren't learned");
            if (destination == null) return Skip("destination map unknown to the API");
            // Fractals, guild halls and the like are joined from anywhere, so the spot before says nothing.
            if (IsIgnoredDestination(destination)) return Skip($"portals to {destination} aren't learned");

            float DistanceSquared(Portal p) => (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y);
            bool Near(Portal p) => p.MapId == fromMap && (DistanceSquared(p) < AnyPortalRadius * AnyPortalRadius
                || p.Destination == destination && DistanceSquared(p) < SameDestinationRadius * SameDestinationRadius);
            // Checked even on the expected return trip: the player asked never to be asked here.
            var ignored = _ignored.FirstOrDefault(Near);
            if (ignored != null) return Skip($"told not to ask again here (portal to {ignored.Destination} at {ignored.X:0.0}, {ignored.Y:0.0})");
            var near = known.FirstOrDefault(Near);
            if (!expected && near != null) return Skip($"known portal to {near.Destination} at {near.X:0.0}, {near.Y:0.0}");

            Logger.Info($"Noticed a new {(expected ? "return " : "")}portal on map {fromMap} at {x:0.0}, {y:0.0} to {destination} (map {toMap}).");
            return new Portal
            {
                MapId = fromMap, Continent = continent.Value, X = (float)Math.Round(x, 1), Y = (float)Math.Round(y, 1),
                Kind = PortalKind.Zone, Destination = destination, Origin = origin,
                DestinationMapId = toMap, DestinationMap = destination,
            };
        }

        public void Add(Portal portal)
        {
            _learned.Add(portal);
            Save();
            Changed?.Invoke(true);
        }

        public void Remove(Portal portal)
        {
            if (!_learned.Remove(portal)) return;
            Save();
            Changed?.Invoke(true);
        }

        /// <summary>Call after changing a learned portal's destination or kind.</summary>
        public void Edited()
        {
            Save();
            Changed?.Invoke(false);
        }

        public void Forget()
        {
            _learned.Clear();
            Save();
            Changed?.Invoke(true);
        }

        /// <summary>Never asks about a portal at this portal's spot again.</summary>
        public void Ignore(Portal portal)
        {
            _ignored.Add(portal);
            Save(_ignored, _ignoredPath);
        }

        public void ForgetIgnored()
        {
            _ignored.Clear();
            Save(_ignored, _ignoredPath);
        }

        /// <summary>Never asks about a portal to this map again, from anywhere.</summary>
        /// <param name="map">The destination's English map name, not the name the player gave the portal.</param>
        public void IgnoreDestination(string map)
        {
            if (string.IsNullOrWhiteSpace(map) || IsIgnoredDestination(map)) return;
            _ignoredDestinations.Add(map.Trim());
            Save(_ignoredDestinations.Select(n => JsonConvert.SerializeObject(n)), _ignoredDestinationsPath);
        }

        public void ForgetIgnoredDestinations()
        {
            _ignoredDestinations.Clear();
            Save(_ignoredDestinations.Select(n => JsonConvert.SerializeObject(n)), _ignoredDestinationsPath);
        }

        private void Save() => Save(_learned, _path);

        /// <summary>One portal per line, like ref/portals.json.</summary>
        private static void Save(List<Portal> portals, string path) => Save(portals.Select(p => JsonConvert.SerializeObject(p)), path);

        /// <summary>One JSON value per line, in an array.</summary>
        private static void Save(IEnumerable<string> lines, string path)
        {
            try
            {
                File.WriteAllText(path, "[\n" + string.Join(",\n", lines) + "\n]\n");
            }
            catch (Exception ex)
            {
                Blocked.Check(ex, path);
                Logger.Warn(ex, $"Couldn't save {path}.");
            }
        }
    }
}
