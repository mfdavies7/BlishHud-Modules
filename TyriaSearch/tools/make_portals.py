"""Builds ref/portals.json, the zone portals TyriaSearch draws on the world map, from Tekkit's All-In-One marker pack.

    python tools/make_portals.py Temp/tw_ALL_IN_ONE.taco

Reads the pack's portal markers (category tw_me_portals), converts their map positions to continent coordinates with
/v2/maps, sorts each into a kind (zone portal, asura gate, dungeon, raid, fractal) and cleans up names.
Kinds and names come from the rules below; ref/portals.json can also be edited by hand after generating.
Needs only the Python standard library.
"""
import json
import os
import sys
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

PORTAL_CATEGORY = ".tw_me_portals."
INCHES_PER_METRE = 39.3701  # marker positions are in metres, the API's map rectangles in inches

# Tekkit's names that differ from the game's.
NAME_FIXES = {
    "The Echoval Wilds": "The Echovald Wilds",
    "Silverwastes": "The Silverwastes",
    "PvP Lobby": "Heart of the Mists",
}

DUNGEONS = {
    "Ascalonian Catacombs", "Caudecus's Manor", "Twilight Arbor", "Sorrow's Embrace", "Citadel of Flame",
    "Honor of the Waves", "Crucible of Eternity", "The Ruined City of Arah",
}

# Lion's Arch reaches these zones on foot; its other portals are asura gates.
LIONS_ARCH_NEIGHBOURS = {"Gendarran Fields", "Bloodtide Coast", "Lornar's Pass"}
# Other asura gates, as (map the gate is on, destination), both directions listed.
OTHER_GATES = {
    ("Divinity's Reach", "Fields of Ruin"), ("Fields of Ruin", "Divinity's Reach"),
    ("Rata Sum", "Metrica Province"), ("Metrica Province", "Rata Sum"),
    ("Straits of Devastation", "Chantry of Secrets"), ("Chantry of Secrets", "Straits of Devastation"),
}


def kind_of(category, source, destination):
    if "_raids_" in category:
        return "Raid"
    if destination == "Fractals of the Mists":
        return "Fractal"
    if destination in DUNGEONS:
        return "Dungeon"
    if (source == "Lion's Arch" and destination not in LIONS_ARCH_NEIGHBOURS) or \
       (destination == "Lion's Arch" and source not in LIONS_ARCH_NEIGHBOURS) or \
       (source, destination) in OTHER_GATES:
        return "AsuraGate"
    return "Zone"


def read_markers(pack):
    """(category, display name, map id, x, z) for every portal marker. TacO names and attributes are case-insensitive."""
    # Categories can be defined in a different file from their markers, so collect them from every file first.
    display, pois = {}, []

    def walk(parent, path):
        for c in parent:
            if c.tag.lower() != "markercategory":
                continue
            attrs = {k.lower(): v for k, v in c.attrib.items()}
            full = (path + "." if path else "") + attrs.get("name", "").lower()
            if attrs.get("displayname"):
                display[full] = attrs["displayname"]
            walk(c, full)

    with zipfile.ZipFile(pack) as z:
        for name in z.namelist():
            if not name.lower().endswith(".xml"):
                continue
            text = z.read(name).decode("utf-8", "ignore").lstrip("﻿")
            if "tw_me_portals" not in text.lower():
                continue
            root = ET.fromstring(text)
            walk(root, "")
            for poi in root.iter():
                attrs = {k.lower(): v for k, v in poi.attrib.items()}
                if poi.tag.lower() == "poi" and PORTAL_CATEGORY in attrs.get("type", "").lower():
                    pois.append(attrs)

    missing = {a["type"].lower() for a in pois} - display.keys()
    assert not missing, f"portal categories without a display name: {sorted(missing)}"
    return [(a["type"].lower(), display[a["type"].lower()], int(a["mapid"]), float(a["xpos"]), float(a["zpos"]))
            for a in pois]


def map_bounds(map_ids):
    maps = {}
    ids = sorted(map_ids)
    for i in range(0, len(ids), 200):  # the API's maximum per request
        url = "https://api.guildwars2.com/v2/maps?ids=" + ",".join(map(str, ids[i:i + 200]))
        with urllib.request.urlopen(url) as response:
            for m in json.load(response):
                maps[m["id"]] = m
    return maps


def to_continent(m, x, z):
    (mx0, my0), (mx1, my1) = m["map_rect"]
    (cx0, cy0), (cx1, cy1) = m["continent_rect"]
    x, z = x * INCHES_PER_METRE, z * INCHES_PER_METRE
    # Map north is +z; continent y grows southwards.
    return (cx0 + (x - mx0) / (mx1 - mx0) * (cx1 - cx0),
            cy0 + (my1 - z) / (my1 - my0) * (cy1 - cy0))


def main(pack, out):
    markers = read_markers(pack)
    maps = map_bounds({m[2] for m in markers})
    portals, skipped = [], set()
    for category, name, map_id, x, z in markers:
        m = maps.get(map_id)
        if m is None:
            skipped.add(map_id)  # e.g. the WvW borderlands, which /v2/maps doesn't list
            continue
        destination = NAME_FIXES.get(name, name)
        cx, cy = to_continent(m, x, z)
        portals.append({
            "map": map_id, "continent": m["continent_id"], "x": round(cx, 1), "y": round(cy, 1),
            "kind": kind_of(category, m["name"], destination), "name": destination,
            "from": m["name"],  # only to make the file readable; the module doesn't use it
        })
    portals.sort(key=lambda p: (p["from"], p["kind"], p["name"]))
    with open(out, "w", encoding="utf-8") as f:
        f.write("[\n" + ",\n".join(json.dumps(p, ensure_ascii=False) for p in portals) + "\n]\n")

    # Self-check: the conversion puts every portal inside the map it's on.
    for p in portals:
        (cx0, cy0), (cx1, cy1) = maps[p["map"]]["continent_rect"]
        assert cx0 <= p["x"] <= cx1 and cy0 <= p["y"] <= cy1, p
    kinds = {}
    for p in portals:
        kinds[p["kind"]] = kinds.get(p["kind"], 0) + 1
    print(f"{len(portals)} portals written to {out}: {kinds}")
    if skipped:
        print(f"Skipped maps the API doesn't list: {sorted(skipped)}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1], os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ref", "portals.json"))
