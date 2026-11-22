# Tyria Search

One search box for everything in Tyria: waypoints, points of interest, vistas, renown hearts, hero challenges, mastery points, adventures, NPCs, events, portals, items, skills, traits and effects. Find what you're after by name, map or chat code, then copy, paste or whisper its chat link, or mark it on the map.

## Searching

Open the search by right-clicking the magnifier icon at the top left, or with a hotkey, and start typing. Results update as you type.

- **Search by map or area:** "Queensdale" lists its landmarks; "Queensdale hero challenge" finds that map's hero challenges.
- **Search by chat code:** paste a code to see what it is.
- **Find NPCs by service:** "bank" lists every banker, each with the waypoint nearest to them.
- **Find portals between maps:** "Black Citadel to Lion's Arch", "gate from Hoelbrak".
- **Filter by category** with the dropdown, or keep it on Everything.
- **A second language:** names show in Blish HUD's language, and you can also match names in a second one, handy when guildmates play in another language.
- **The wiki, live:** pages the downloaded data doesn't have are fetched from the wiki as you type and listed below the results, placed on the map where possible.

Each result shows what it is and where: "Waypoint · Shaemoor Fields · Queensdale". With the search box empty, you see your favourites.

## Game-Style Tooltips

Hover over a skill, trait or item to see the game's own tooltip, fetched from the GW2 API:

- **Skills and traits:** activation time and recharge, the description, and every fact: range, boons and conditions with their durations, combo fields and finishers, stun breaks, and damage as its power coefficient.
- **Items:** name in its rarity's colour, weapon strength or defense, attributes, rune bonuses, a consumable's effect, upgrade and infusion slots, rarity, type, level, binding and the merchant's price.
- **Effects:** boons, conditions and the effects of food and utility items, with their descriptions.

## Actions

- **Click** a result to copy its chat link. For NPCs, events and other things without a link of their own, that's the nearest waypoint.
- **Enter** copies the top result and returns you to the game, ready to paste.
- **Right-click** for everything else: paste into chat, whisper to yourself, show on the map, copy a /wiki command, open the wiki page, or add to favourites.

What clicking and right-clicking do can be changed in the settings.

## The Map

- **Show on Map** marks results on the world map, as many as you like. A marker off screen sits at the screen's edge, pointing the way. Markers never get in the way: clicks and hovering reach the game's icons underneath. Right-click a marker to remove it.
- **The minimap** shows the markers too.
- **Portals on the map:** optionally mark every zone portal, asura gate, and dungeon, raid and fractal entrance with the game's icons, labelled with where they lead.
- **Learns new portals:** take a portal it doesn't know, and it offers to add it, then asks you to go back through for the return portal.

## Data

Everything is downloaded from the GW2 API and the wiki the first time (a few minutes, mostly items), then kept up to date: after a game update only what changed is fetched, and everything is downloaded again every 30 days. You can search as soon as each part arrives. NPC, event, effect and portal names are in English.

## Settings

Left-click the Tyria Search icon to open the settings, or press **Settings** on the module's page in Blish HUD. They're grouped into Search Window, Search Options (which kinds of result to include), Search Data (second language, how often to download), World Map, Portals and Chat. The [full settings list](https://github.com/mfdavies7/BlishHud-Modules/blob/main/TyriaSearch/README.md#settings) describes each one.

## Credits

Combines ideas from Universal Search, Tyria's GPS, V.E.L.O.C.I.T.Y. and NPC Finder; written from scratch. Portal positions are from Tekkit's All-In-One marker pack. NPCs, events, effects and live results come from the [Guild Wars 2 Wiki](https://wiki.guildwars2.com).

Found a bug or have an idea? [Open an issue on GitHub](https://github.com/mfdavies7/BlishHud-Modules/issues).
