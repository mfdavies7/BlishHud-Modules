# Blish HUD modules

Guild Wars 2 quality-of-life modules for [Blish HUD](https://blishhud.com).

When a module types into chat, it does so through the clipboard, then puts back whatever you had copied.

## Wiki Search

Shift + left-click an item in Guild Wars 2 to open its wiki page.

GW2 puts the item's chat link in the chat box; the module adds `/wiki ` in front of it and presses Enter, so the game opens the wiki in your browser.
Inspired by the Wiki Search feature of KenediaDev's TyrianQoL; written from scratch.

### Settings

The settings are in their own window. Click the Wiki Search icon at the top left to open or close it (by default; see Corner Icon Left Click Action), or press **Open Settings Window** on the module's page in Blish HUD's settings.

- **Register Corner Icon** (on) – register the Wiki Search icon at the top left. Off: the icon is removed and the two click action settings are disabled.
- **Corner Icon Left Click Action** (Open Settings) and **Corner Icon Right Click Action** (Toggle WikiSearch) – Open Settings opens or closes the settings window; Toggle WikiSearch switches Enabled like the hotkey; Open Wiki opens the wiki's main page in Wiki Language; Nothing ignores the click.
- **Wiki Language** – Game Language, English, German, French or Spanish.
- **Toggle Hotkey** – switches the Enabled setting on or off.
- **Enabled** (on) – turn it off to shift-click items into chat normally.
- **Overwrite Unsent Chat Text** (off) – also search when chat was already open before the click: if chat still holds text you typed but didn't send, delete it and search the item. The deleted text can't be recovered. Off: with chat already open, shift-click links items into your message as usual.
- **Work When the Map Is Open** (on) – when off, shift-clicking map points links them as usual.
- **Return to the Game After Searching** (off) – switch back to GW2 once the browser opens.
- **Key Press Delay** (60 ms, 20–250) – raise it if the command is sometimes typed wrong.
- **Wait for Shift Release Before Sending** (off) – only type `/wiki` after you let go of Shift. Turn on if the command goes missing or the link gets replaced.
- **Show Hint While Holding Shift** (on) – shown only when a click would search: enabled, in game, game focused, and chat or map closed unless the settings above allow them.

The hint can't tell whether the cursor is over an item; Blish HUD and GW2 don't expose that.

The icon (`WikiSearch/ref/textures/WikiSearch.png`) is the same one the hint shows.

## Waypoint Paste

Press a hotkey, or click the waypoint icon at the top left, to whisper a waypoint chat link to your own character. The link shows up in chat, ready to click or share. Unless Switch to /s After Whisper is on, chat stays on the whisper channel afterwards, so a message you type next is also whispered to yourself.
Inspired by the Waypoint Paste feature of KenediaDev's TyrianQoL; written from scratch.

### Settings

The settings are in their own window. Click the Waypoint Paste icon at the top left to open or close it (by default; see Corner Icon Left Click Action), or press **Open Settings Window** on the module's page in Blish HUD's settings.

- **Register Corner Icon** (on) – register the Waypoint Paste icon at the top left. Off: the icon is removed and the two click action settings are disabled.
- **Corner Icon Left Click Action** (Open Settings) and **Corner Icon Right Click Action** (Paste and Click Waypoint) – Open Settings opens or closes the settings window; Paste Waypoint whispers the waypoint like the hotkey, without clicking the link; Paste and Click Waypoint also clicks the link, but only when Auto-Click Chat Link is on; Nothing ignores the click.
- **Paste Waypoint Hotkey** – unset by default. Only works in game with chat closed; with chat open it asks you to close it first. It stops and tells you why if you alt-tab, chat closes, or a loading screen starts partway through.
- **Waypoint Chat Code** (`[&BCAJAAA=]`) – shift-click a waypoint on the map to get its code. Invalid codes show a warning instead of sending.
- **Use Copied Waypoint First** (on) – if your clipboard holds a waypoint, point of interest or vista code, paste that instead.
- **Overwrite Unsent Chat Text** (off) – if chat still holds text you typed but didn't send, delete it and send the whisper. The deleted text can't be recovered. Off: stop and leave your text in chat.
- **Switch to /s After Whisper** (off) – switch chat back to /s after the whisper, so your next message isn't whispered to yourself. With Auto-Click Chat Link, this happens before the link is clicked.
- **Auto-Click Chat Link** (off) – after the whisper is sent, click its link in chat for you. The cursor goes back to where it was.
- **Set Click Position Hotkey** – send a whisper, hover over its link in chat, press this to save the spot. Set it again if you move or resize chat.
- **Click Position X / Y** – screen position of the link, filled in by the hotkey; type to fine-tune. 0, 0 means not set.
- **Show Click Position** (off) – mark the saved click position on screen with a crosshair, to check it's over the chat link. Off: it's still shown for a few seconds after Set Click Position Hotkey is pressed or the position is changed.
- **Wait Before Clicking Link** (500 ms, 20–3000) – raise it if the click sometimes happens before the whisper shows up in chat.
- **Key Press Delay** (60 ms, 20–250) – raise it if the whisper is sometimes typed wrong.

The icon (`WaypointPaste/ref/textures/WaypointPaste.png`) is the game's map waypoint icon (`map_waypoint` from the GW2 API), recoloured to match Blish HUD's corner icons.

## Build

Requires the .NET SDK.

```
dotnet build WikiSearch -c Release
dotnet build WaypointPaste -c Release
```

Each module is written to `<Module>/bin/Release/mfdavies7.bh.<Module>.bhm`. Copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).

## Credits

The hint icon (`WikiSearch/ref/textures/WikiSearch.png`) is from [KenediaDev's TyrianQoL](https://github.com/KenediaDev/Kenedia.BlishHUD).

## Tyria Search

Search waypoints, points of interest, vistas, renown hearts, hero challenges, mastery points, adventures, NPCs, events, items, skills and traits by name, map or chat code, then copy, paste or whisper their chat links, or mark them on the map.
Combines ideas from Universal Search, Tyria's GPS, V.E.L.O.C.I.T.Y. and NPC Finder; written from scratch.

Open it by right-clicking the magnifier icon at the top left (by default; see Corner Icon Right Click Action) or with the hotkey, and type. Results update as you type, and a map or area name lists its landmarks. Under each result is what it is, then for anything on the map its area and map ("Waypoint · Shaemoor Fields · Queensdale"), followed by details such as level or services.

- **Click** a result to copy its chat link (changeable in settings). For an NPC, that's the waypoint nearest to them.
- **Enter** copies the top result, closes the window and returns to the game, ready to paste.
- **Right-click** a result for the menu (changeable in settings): Copy Chat Link, Paste into Chat or Whisper to Yourself; Show on Map / Remove from Map; Copy /wiki Command; Open Wiki Page; Add to / Remove from Favourites; Clear All Map Markers. **Shift-click** always opens the menu.
- **Show on Map** marks the result on the world map (M); you can mark several at once. If one is off screen, its marker sits at the screen edge pointing the way. Markers don't get in the way of the map: clicks and hovering reach the game's icons underneath. Remove one by right-clicking its marker (on the world map or minimap) and choosing **Remove Icon** (any other click or key closes that menu), or from the result's menu, or all with the **Clear Markers** button next to the status line, the menu or the Clear Map Markers Hotkey.
- With the search box empty, the window shows your favourites. Item names are coloured by rarity. Results the API has no icon for (some skills, traits and items, and NPCs and wiki pages without a service icon) get the wiki's game icon where it has one (saved in `tyriasearch/wiki-icons`; icons padded with a large black border, like the generic monster skill one, are cropped to fill their space). The rest get a default icon for their kind, which is also shown while an icon downloads: the game's map icon for landmarks, portals, hearts, hero challenges, mastery points, adventures and events, the wiki's generic skill and trait icons, the game's Inventory, Contacts and Compass icons for items, NPCs and places, and a plain one for other wiki pages.
- In **Everything**, once you pause typing (3 letters or more), it also asks the wiki live and lists the pages it finds below the results here (places, NPCs, objects, items), placed on the map and linked to their nearest waypoint where possible; pages named like a result here are left out. In other categories it asks only when nothing matches. Item pages get the item's chat link. A last result opens the full wiki search in your browser.
- The **Wiki** category in the dropdown skips the downloaded data and asks the wiki straight away, once you pause typing (3 letters or more). It's listed only while Search the Wiki is on.

NPCs come from the wiki. About half have exact coordinates there; the rest only name their area ("Trader's Forum"), so the marker goes to that area's centre and says *approximate*. Either way, the chat link is the nearest waypoint. NPCs listed only by map, or a region like "Kryta" without coordinates, get no marker or waypoint; clicking them opens their wiki page. An NPC in several places is listed once per place, so searching "bank" lists every banker. NPC names are in English.

Renown hearts (by their task, "Help the Seraph protect Claypool from centaurs"), hero challenges, mastery points and adventures come with the map data. Hero challenges and mastery points have no names, so search for the map ("Queensdale hero challenge") or "mastery point". All of them are placed exactly, and their chat link is the nearest waypoint; the game has no chat links for them.

Events come from the wiki: about 4,000 current ones, with their level and type (group event, meta event). Most are placed exactly; the rest go to the centre of the area they start in. The chat link is the nearest waypoint. Event names are in English.

Portals are searchable too, under **Portals**: "Portal to Kessex Hills", "Asura Gate to Hoelbrak", "Dungeon Entrance to Ascalonian Catacombs", "Raid Entrance to Spirit Vale". Searching a map's name also finds the portals on that map, "portal" or "gate" finds every kind, and two map names find the portals between them, the one from the first to the second first ("Black Citadel to Lion's Arch", "gate from Hoelbrak"). Positions are exact, the chat link is the nearest waypoint, and the wiki opens the destination's page. Portal names are in English.

Data is downloaded from the GW2 API and the wiki the first time, then in full every 30 days (see **Full Download Every (Days)**). After a game update, only what changed is fetched: new items, and places, skills and traits only if maps, skills or traits were added or removed (a check of three small lists).

- Places, skills and traits: about 1.5 MB per language, under a minute. On a full download, add a minute or two once to look up skill and trait icons the API lacks on the wiki.
- NPCs: about 2 MB, a few minutes (the wiki is slower than the API).
- Events: about 1 MB, under a minute.
- Items: about 10 MB per language, a few minutes.

Results are searchable as each part arrives. **Update Data** in the settings downloads everything again; use it if a renamed item or new point on an existing map is missing.

### Settings

The settings are in their own window, in sections (Search Window, Search Options, Search Data, World Map, Portals, Chat). Click the Tyria Search icon at the top left to open or close it (by default; see Corner Icon Left Click Action), or press **Open Settings Window** on the module's page in Blish HUD's settings.

- **Open Search Hotkey** – unset by default.
- **Register Corner Icon** (on) – register the Tyria Search icon at the top left. Off: the icon is removed and the two click action settings are disabled; open the search with the hotkey and the settings from the module's page in Blish HUD's settings.
- **Corner Icon Left Click Action** (Open Settings) and **Corner Icon Right Click Action** (Open Search) – what clicking the Tyria Search icon does: Open Settings opens or closes the settings window, Open Search opens or closes the search window, Nothing ignores the click.
- **Second Search Language** (None) – names are shown in Blish HUD's language; searches also match names in this language. None turns it off.
- **Full Download Every (Days)** (30, Every Update, 7–365 or Never) – how often all search data is downloaded again, to pick up renamed items and changes to existing maps. In between, a game update only fetches what was added. At the lowest setting, Every Update, everything (including NPCs and events) is downloaded again after each game update instead. At the highest, Never, nothing is downloaded again except what game updates add and anything missing; NPCs and events then stay as first downloaded until you press **Update Data**.
- **Open Map After Show on Map** (on) – opens the world map after Show on Map, if it isn't open already.
- **Keep Search Open on the Map** (on) – when Open Map After Show on Map opens the world map, leave the search window open. Off: hide it so it doesn't cover the map.
- **Zoom Out Map After Opening** (on) – when Open Map After Show on Map opens the world map, zooms it all the way out so the marker is on screen. It scrolls the mouse wheel, so keep the cursor over the game.
- **Game's Map Key** (M) – the World Map key from the game's Control Options, pressed by Open Map After Show on Map.
- **Clear Map Markers Hotkey** (unbound) – removes all map markers, also while the map is open.
- **Show on the Minimap** (on) – also draws Show on Map markers on the minimap, and portals when Show Portals on the World Map is on. A marker out of the minimap's range sits at its edge, pointing the way. Names are left off there for lack of room.
- **Show Portals on the World Map** (off) – marks every portal on the world map with the game's icons: blue zone portals, purple asura gates, and dungeon, raid and fractal entrances, labelled "To <map>" once you zoom in. Covers core Tyria through End of Dragons, from Tekkit's All-In-One marker pack; later maps and the WvW borderlands have none yet.
- **Learn New Portals** (on) – when you take a portal Tyria Search doesn't know, a panel at the top of the screen (drag it anywhere; it remembers) asks whether to add it, with where it leads (the destination map's name, editable) and its kind (Zone Portal, Asura Gate, Dungeon, Raid or Fractal Entrance). It then asks you to go back through it; the way back is noticed like any other portal and offered as the return portal (or press **Skip**). Each map change that isn't taken as a new portal is logged in the Blish HUD log with the reason. Added portals are saved to `learned-portals.json` in the `tyriasearch` folder, in the bundled list's format. A portal counts when the map changes within a few seconds of moving, with the world map closed, away from any known portal; waypoints, teleport items and joining a squad member are left out.
- **Search Waypoints**, **Search Points of Interest**, **Search Vistas**, **Search Portals**, **Search Renown Hearts**, **Search Hero Challenges**, **Search Mastery Points**, **Search Adventures** (all on) – which kinds of result to include. These come with the map data every result's position needs, so turning one off only leaves it out of the results. Any Search option that's off (these, and the ones below) also takes its category out of the search window's dropdown.
- **Search Skills**, **Search Traits** (on) – off, they're left out of the results and of later downloads. What was already downloaded is kept and shown again when you turn the option back on, then brought up to date.
- **Search NPCs** (on) – also downloads English map data if neither search language is English, since NPC places are matched by English name.
- **Search Events** (on) – like Search NPCs, also downloads English map data if neither search language is English.
- **Search Items** (on).
- **Search the Wiki** (on) – in Everything, also ask the wiki once you pause typing and list its pages below the results here; in other categories, ask it when nothing matches. Off: offer to open a wiki search in your browser instead, and leave the Wiki category out of the dropdown.
- **Overwrite Unsent Chat Text** (off) – for Whisper to Yourself: if chat still holds text you typed but didn't send, delete it and send the whisper. The deleted text can't be recovered. Off: stop and leave your text in chat.
- **Switch to /s After Whisper** (off) – switch chat back to /s after Whisper to Yourself, so your next message isn't whispered to yourself. Off: chat stays on the whisper channel.
- **Key Press Delay** (60 ms, 20–250) – raise it if pasted or whispered text is sometimes typed wrong.
- **Left-Click a Result** (Copy Chat Link) and **Right-Click a Result** (Show Menu) – also Paste into Chat, Whisper to Yourself, Show on Map, Open Wiki Page, Copy Wiki Command or Toggle Favourite. If the result has no chat link or map position, its wiki page opens instead.
- **Update Data** button – downloads all search data again now (skills, traits, NPCs, events and items only when they're on).
- **Clear Cache** button – deletes the saved search data (`search-*.json`) and wiki icons (`wiki-icons`) in the `tyriasearch` folder, then downloads them again. Use it if Update Data doesn't fix missing or wrong results or icons. Searches keep using the old data until the new data arrives; learned portals are kept.
- **Learned Portals** list (in the Portals section) – every learned portal, with where it leads and its kind to edit (saved on Enter, or when you pick a kind) and a **Delete** button.
- **Forget Learned Portals** button – removes every portal Learn New Portals added.
