# Tyria Search

One search box for everything in Tyria: waypoints, points of interest, vistas, renown hearts, hero challenges, mastery points, adventures, NPCs, events, portals, effects, items, skills and traits. Find what you're after by name, map or chat code, then copy, paste or whisper its chat link, or mark it on the map.

Combines ideas from Universal Search, Tyria's GPS, V.E.L.O.C.I.T.Y. and NPC Finder.

## How to Use

Right-click the magnifier icon at the top left, or press the Open Search Hotkey, and start typing. Results update as you type. Pick a category from the dropdown, or keep it on Everything. With the search box empty, you see your favourites.

The first time, the search data is downloaded, which takes a few minutes. You can search what has arrived while the rest downloads. After that it keeps itself up to date.

### What You Can Search

- **By name:** "Shaemoor Waypoint", "Omnomberry Bar", "Signet of Rage".
- **By map or area:** "Queensdale" lists its landmarks. Hero challenges and mastery points have no names, so search "Queensdale hero challenge" or "mastery point".
- **By chat code:** paste a code to see what it is.
- **NPCs by service:** "bank" lists every banker. NPCs are also found by what they stand beside or what players call it: "cooking station" lists every master chef, "crafting station" every master craftsman ("Hoelbrak crafting station" just that city's), and "vault", "TP", "repair anvil" and "forge" work too.
- **Portals:** "Portal to Kessex Hills", "Asura Gate to Hoelbrak", "Dungeon Entrance to Ascalonian Catacombs". Two map names find the portals between them ("Black Citadel to Lion's Arch"), and "portal" or "gate" lists them all.
- **Effects:** boons, conditions and other effects, plus food and utility effects, listed under the consumable's name.
- **The wiki:** in Everything, pages from the wiki are listed below the results once you pause typing. The Wiki category searches only the wiki. When nothing matches, a result offers to search the wiki in your browser.

The best matches come first: exact names, then names starting with what you typed, then the rest. Within each of those, results on the map you're on come first. Among equal matches, the nearest to you comes first, on other maps too. Results that differ only in their chat code, like the game's identical copies of some items, are listed once, showing your favourite if one of them is. Skills and traits are always listed separately, since their tooltips can still differ.

Each result shows what it is and where: "Waypoint · Shaemoor Fields · Queensdale". Hover over a skill, trait or item to see its game tooltip, or over anything else to see its chat link. Skill and trait tooltips show PvE values; some differ in PvP and WvW. NPC, event, effect and portal names are in English. NPCs and events whose exact spot isn't known are placed at the centre of their area, marked *approximate*.

### Actions

- **Click** a result to copy its chat link. For NPCs, events, hearts and other things without a link of their own, that's the nearest waypoint.
- **Enter** copies the top result and returns you to the game, ready to paste.
- **Right-click** for the menu: Copy Chat Link, Paste into Chat, Whisper to Yourself, Show on Map, Copy /wiki Command, Open Wiki Page, Add to Favourites and Clear All Map Markers. **Shift-click** always opens the menu.

What clicking and right-clicking do can be changed in the settings.

### The Map

- **Show on Map** marks a result on the world map and minimap. You can mark several at once. A marker that's off screen sits at the edge, pointing the way, and each shows how far away it is.
- **Remove a marker** by right-clicking it and choosing **Remove Icon**, or from the result's menu. **Clear Markers** next to the status line (or the Clear Map Markers Hotkey) removes them all.
- **Portals:** turn on Show Portals on the World Map to see every zone portal, asura gate, and dungeon, raid and fractal entrance, labelled with where they lead.
- **New portals:** with Learn New Portals on, when you take a portal Tyria Search doesn't know, it offers to add it, then asks you to go back through for the return portal.

## Settings

Left-click the Tyria Search icon at the top left to open the settings, or press **Settings** on the module's page in Blish HUD's settings. Each slider has a box to type its value in, and a **Default** button (greyed out at the default) that puts it back.

### Search Window

- **Register Corner Icon** (on) – show the Tyria Search icon at the top left. Its tooltip shows the Open Search Hotkey in square brackets after its name while one is set. Off: open the search with the hotkey.
- **Corner Icon Left Click Action** (Open Settings) and **Corner Icon Right Click Action** (Open Search) – Open Settings, Open Search or Nothing.
- **Open Search Hotkey** (unset) – shows or hides the search window.
- **Left-Click a Result** (Copy Chat Link) and **Right-Click a Result** (Show Menu) – also Paste into Chat, Whisper to Yourself, Show on Map, Open Wiki Page, Copy Wiki Command or Toggle Favourite. A result with no chat link or map position opens its wiki page instead.

### Search Options

- **Search Waypoints**, **Search Points of Interest**, **Search Vistas**, **Search Portals**, **Search Renown Hearts**, **Search Hero Challenges**, **Search Mastery Points**, **Search Adventures** (all on) – which kinds of result to include. Turning one off (these or the ones below) also removes its category from the dropdown.
- **Search NPCs**, **Search Events**, **Search Effects**, **Search Items**, **Search Skills**, **Search Traits** (all on).
- **Search the Wiki** (on) – list the Wiki category, and in Everything also list wiki pages below the results.
- **List Wiki Pages Below Results** (on) – list wiki pages below the results in every category, not just Everything.

### Search Data

- **Second Search Language** (None) – names are shown in Blish HUD's language; searches also match names in this one.
- **Full Download Every (Days)** (30, Every Update, 7–365 or Never) – how often all search data is downloaded again, to pick up renamed items and map changes. New content from game updates is always added. Every Update downloads everything again after each game update; Never only adds what updates bring.
- **Update Data** button – downloads all search data again now. Use it if something new or renamed is missing.
- **Clear Cache** button – deletes the downloaded search data and icons and downloads them again. Use it if Update Data doesn't fix missing or wrong results. Learned portals are kept.

### World Map

- **Clear Map Markers Hotkey** (unset) – removes all map markers.
- **Open Map After Show on Map** (on) – opens the world map after Show on Map.
- **Keep Search Open on the Map** (on) – leave the search window open when the map opens. Off: hide it so it doesn't cover the map.
- **Zoom Out Map After Opening** (on) – zoom the map all the way out so the marker is on screen. Keep the cursor over the game.
- **Game's Map Key** (M) – your World Map key from the game's Control Options.
- **Show on the Minimap** (on) – also show markers and portals on the minimap.
- **Show Distance on the World Map** (on) – write how far each marker is from you under its name, in game units (the ones skill ranges use). "~" marks an approximate position.
- **Show Distance on the Minimap** (off) – the same under each marker on the minimap.

### Portals

- **Show Portals on the World Map** (off) – mark every portal on the world map with the game's icons, labelled "To <map>" once you zoom in. Covers core Tyria through End of Dragons; later maps and the WvW borderlands have none yet.
- **Learn New Portals** (off) – when you take a portal Tyria Search doesn't know, a panel at the top of the screen offers to add it, with where it leads and its kind, both editable. It then asks you to go back through it for the return portal (or press **Skip**). **Do Not Ask Again for This Location** never asks about a portal at that spot again, and **Do Not Ask Again for This Destination** never asks about portals to that map, from anywhere. Portals to and from fractals, guild halls, homesteads, the Mistlock Sanctuary, PvP and WvW are never asked about, since the game can take you there from anywhere and their exits lead back to where you came from.
- **Learned Portals** list – every portal you've added, to edit or **Delete**.
- **Forget Learned Portals** button – removes every portal you've added.
- **Forget Ignored Locations** button – asks again about portals you chose Do Not Ask Again for This Location for.
- **Forget Ignored Destinations** button – asks again about portals to maps you chose Do Not Ask Again for This Destination for.

### Chat

- **Overwrite Unsent Chat Text** (off) – for Whisper to Yourself: if chat holds text you typed but didn't send, delete it and send the whisper. That text can't be recovered. Off: stop and leave your text in chat.
- **Switch to /s After Whisper** (off) – switch chat back to /s after Whisper to Yourself, so your next message isn't whispered to yourself.
- **Key Press Delay (ms)** (60, 20–250) – raise it if pasted or whispered text is sometimes typed wrong.

## Install

Build `bytex.tyriasearch_<version>.bhm` (see the [main README](../README.md#build)) and copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).

## Credits

Portal positions are from Tekkit's All-In-One marker pack. NPCs, events, effects and wiki results come from the [Guild Wars 2 Wiki](https://wiki.guildwars2.com).

## Legal

Tyria Search is an unofficial fan project. It is not affiliated with, endorsed by or sponsored by ArenaNet or NCSOFT.

© ArenaNet LLC. All rights reserved. NCSOFT, ArenaNet, Guild Wars, Guild Wars 2, GW2, Heart of Thorns, Path of Fire, End of Dragons, Secrets of the Obscure, Janthir Wilds, Visions of Eternity, and all associated logos, designs, and composite marks are trademarks or registered trademarks of NCSOFT Corporation. All other trademarks are the property of their respective owners.
