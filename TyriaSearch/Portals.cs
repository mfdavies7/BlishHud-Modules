using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TyriaSearch
{
    /// <summary>What a portal is, which picks its map icon.</summary>
    internal enum PortalKind { Zone, AsuraGate, Dungeon, Raid, Fractal }

    /// <summary>A portal in continent coordinates, named after where it leads.</summary>
    internal sealed class Portal
    {
        [JsonProperty("map")] public int MapId { get; set; }
        [JsonProperty("continent")] public int Continent { get; set; }
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("kind"), JsonConverter(typeof(StringEnumConverter))] public PortalKind Kind { get; set; }
        [JsonProperty("name")] public string Destination { get; set; }

        /// <summary>
        /// The portals in ref/portals.json, made by tools/make_portals.py from Tekkit's All-In-One marker pack.
        /// </summary>
        public static List<Portal> Load(Stream json)
        {
            using (var reader = new JsonTextReader(new StreamReader(json)))
                return new JsonSerializer().Deserialize<List<Portal>>(reader);
        }

        /// <summary>The game's map icon for each kind, by asset id (from the wiki's map icon pages and /v2/files).</summary>
        public static int IconAssetId(PortalKind kind) => kind switch
        {
            PortalKind.AsuraGate => 102338, // purple asura gate
            PortalKind.Dungeon => 102478,
            PortalKind.Raid => 1128644,
            PortalKind.Fractal => 514379,
            _ => 759449, // blue zone portal
        };

        // Every name is "<label> to <destination>"; the wiki term is the destination (see TyriaSearchModule.WikiTerm).
        private static string Label(PortalKind kind) => kind switch
        {
            PortalKind.AsuraGate => "Asura gate",
            PortalKind.Dungeon => "Dungeon entrance",
            PortalKind.Raid => "Raid entrance",
            _ => "Portal",
        };

        public const string NameSeparator = " to ";

        /// <summary>Search entries for the portals, placed in their area with the nearest waypoint as chat link. English names.</summary>
        public static IEnumerable<SearchEntry> Entries(IEnumerable<Portal> portals, Places places) => portals.Select(p =>
            WikiEntries.Entry(EntryKind.Portal, $"portal:{p.MapId}:{p.X:0}:{p.Y:0}", Label(p.Kind) + NameSeparator + p.Destination,
                places.At(p.X, p.Y, p.MapId) ?? new Placement { MapId = p.MapId, X = p.X, Y = p.Y }, places,
                null, null, SearchEntry.AssetIcon + IconAssetId(p.Kind)));
    }
}
