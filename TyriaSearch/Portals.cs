using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Bytex.TyriaSearch
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
        /// <summary>The map the portal is on, by name; only to make the files readable.</summary>
        [JsonProperty("from")] public string Origin { get; set; }
        /// <summary>Only for portals just noticed, not saved: the map the portal leads to.</summary>
        [JsonIgnore] public int DestinationMapId { get; set; }

        /// <summary>
        /// The portals in ref/portals.json, made from Tekkit's All-In-One marker pack, or the ones
        /// <see cref="PortalLearner"/> saved, in the same format.
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
            PortalKind.AsuraGate => "Asura Gate",
            PortalKind.Dungeon => "Dungeon Entrance",
            PortalKind.Raid => "Raid Entrance",
            PortalKind.Fractal => "Fractal Entrance",
            _ => "Portal",
        };

        public const string NameSeparator = " to ";

        /// <summary>Each kind as the review panel and settings name it.</summary>
        public static readonly (PortalKind kind, string label)[] KindLabels =
        {
            (PortalKind.Zone, "Zone Portal"),
            (PortalKind.AsuraGate, "Asura Gate"),
            (PortalKind.Dungeon, "Dungeon Entrance"),
            (PortalKind.Raid, "Raid Entrance"),
            (PortalKind.Fractal, "Fractal Entrance"),
        };

        public static string KindLabel(PortalKind kind) => KindLabels.First(k => k.kind == kind).label;
        public static PortalKind KindFromLabel(string label) => KindLabels.FirstOrDefault(k => k.label == label).kind;

        /// <summary>Search entries for the portals, placed in their area with the nearest waypoint as chat link. English names.</summary>
        public static IEnumerable<SearchEntry> Entries(IEnumerable<Portal> portals, Places places) => portals.Select(p =>
            WikiEntries.Entry(EntryKind.Portal, $"portal:{p.MapId}:{p.X:0}:{p.Y:0}", Label(p.Kind) + NameSeparator + p.Destination,
                places.At(p.X, p.Y, p.MapId) ?? new Placement { MapId = p.MapId, X = p.X, Y = p.Y }, places,
                null, null, SearchEntry.AssetIcon + IconAssetId(p.Kind)));
    }
}
