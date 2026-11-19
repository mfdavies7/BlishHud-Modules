# Blish HUD modules

Guild Wars 2 quality-of-life modules for [Blish HUD](https://blishhud.com).

## Wiki Search

Shift + left-click an item in Guild Wars 2 to open its wiki page.

GW2 puts the item's chat link in the chat box; the module adds `/wiki ` in front of it and presses Enter, so the game opens the wiki in your browser.
Inspired by the Wiki Search feature of KenediaDev's TyrianQoL; written from scratch.

### Settings

- **Wiki language** – Game Language, English, German, French or Spanish.
- **Toggle hotkey** – switch the Enabled setting on or off.
- **Enabled** (on) – turn it off to shift-click items into chat normally.
- **Work when chat is already open** (off) – when off, shift-click links items into your message as usual. When on, everything in the chat box is searched.
- **Work when the map is open** (on) – when off, shift-clicking map points links them as usual.
- **Return to the game after searching** (off) – switch back to GW2 once the browser opens.
- **Restore clipboard** (on) – put back what you had copied; the module pastes `/wiki ` through the clipboard.
- **Key press delay** (60 ms, 20–250) – raise it if the command is sometimes typed wrong.
- **Wait for Shift release before sending** (off) – only type `/wiki` after you let go of Shift. Turn on if the command goes missing or the link gets replaced.
- **Show hint while holding Shift** (on) – shown only when a click would search: enabled, in game, game focused, and chat or map closed unless the settings above allow them.

The hint can't tell whether the cursor is over an item; Blish HUD and GW2 don't expose that.

## Waypoint Paste

Press a hotkey to whisper a waypoint chat link to your own character. The link shows up in chat, ready to click or share. Afterwards chat is switched back to `/s`, so your next message isn't whispered to yourself.
Inspired by the Waypoint Paste feature of KenediaDev's TyrianQoL; written from scratch.

### Settings

- **Paste waypoint hotkey** – unset by default. Only works in game with chat closed; with chat open it asks you to close it first. It stops and tells you why if you alt-tab, chat closes, or a loading screen starts partway through.
- **Waypoint chat code** (`[&BCAJAAA=]`) – shift-click a waypoint on the map to get its code. Invalid codes show a warning instead of sending.
- **Use copied waypoint first** (off) – if your clipboard holds a waypoint, point of interest or vista code, paste that instead.
- **Restore clipboard** (on) – put back what you had copied; the module types through the clipboard.
- **Overwrite unsent chat text** (off) – if chat still holds text you typed but didn't send, delete it and paste the waypoint. Off: stop and leave your text in chat.
- **Auto Click Chat Link** (off) – after the whisper is sent, click its link in chat for you. The cursor goes back to where it was.
- **Set click position hotkey** – send a whisper, hover over its link in chat, press this to save the spot. Set it again if you move or resize chat.
- **Click position X / Y** – screen position of the link, filled in by the hotkey; type to fine-tune. 0, 0 means not set.
- **Wait before clicking link** (500 ms, 20–3000) – raise it if the click sometimes happens before the whisper shows up in chat.
- **Key press delay** (60 ms, 20–250) – raise it if the whisper is sometimes typed wrong.

## Build

Requires the .NET SDK.

```
dotnet build WikiSearch -c Release
dotnet build WaypointPaste -c Release
```

Each module is written to `<Module>/bin/Release/<Module>.bhm`. Copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).

## Credits

The hint icon (`WikiSearch/ref/textures/WikiSearch.png`) is from [KenediaDev's TyrianQoL](https://github.com/KenediaDev/Kenedia.BlishHUD).

## Tyria Search

Search waypoints, points of interest, vistas, NPCs, items, skills and traits by name, map or chat code, then copy, paste or whisper their chat links, or mark them on the map.
Combines ideas from Universal Search, Tyria's GPS, V.E.L.O.C.I.T.Y. and NPC Finder; written from scratch.

Open it with the magnifier icon at the top left or the hotkey, and type. Results update as you type, and a map name lists that map's landmarks.

- **Click** a result to copy its chat link (changeable in settings). For an NPC, that's the waypoint nearest to them.
- **Enter** copies the top result, closes the window and returns to the game, ready to paste.
- **Right-click** a result for the menu (changeable in settings): copy, paste into chat or whisper to yourself; Show on map / Remove from map; Copy /wiki command; Open wiki page; Add to / Remove from favourites; Clear all map markers. **Shift-click** always opens the menu.
- **Show on map** marks the result on the world map (M); you can mark several at once. If one is off screen, its marker sits at the screen edge pointing the way. Remove one from its menu, or all with the **Clear markers** button next to the status line, the menu or the Clear map markers hotkey.
- With the search box empty, the window shows your favourites. Item names are coloured by rarity. Results without a game icon (most NPCs, many wiki pages) get a plain one for their kind.
- If nothing matches, it asks the wiki live and lists the pages it finds (places, NPCs, objects, items), placed on the map and linked to their nearest waypoint where possible. Item pages get the item's chat link. A last result opens the full wiki search in your browser.

NPCs come from the wiki. About half have exact coordinates there; the rest only name their area ("Trader's Forum"), so the marker goes to that area's centre and says *approximate*. Either way, the chat link is the nearest waypoint. NPCs listed only by map, or a region like "Kryta" without coordinates, get no marker or waypoint; clicking them opens their wiki page. An NPC in several places is listed once per place, so searching "bank" lists every banker. NPC names are in English.

Portals are searchable too, under **Portals**: "Portal to Kessex Hills", "Asura gate to Hoelbrak", "Dungeon entrance to Ascalonian Catacombs", "Raid entrance to Spirit Vale". Searching a map's name also finds the portals on that map, "portal" or "gate" finds every kind, and two map names find the portals between them, the one from the first to the second first ("Black Citadel to Lion's Arch", "gate from Hoelbrak"). Positions are exact, the chat link is the nearest waypoint, and the wiki opens the destination's page. Portal names are in English.

Data is downloaded from the GW2 API and the wiki the first time, then in full once a month. After a game update, only what changed is fetched: new items, and places, skills and traits only if maps, skills or traits were added or removed (a check of three small lists).

- Places, skills and traits: about 1.5 MB per language, under a minute.
- NPCs: about 2 MB, a few minutes (the wiki is slower than the API).
- Items: about 10 MB per language, a few minutes.

Results are searchable as each part arrives. **Update data** in the settings downloads everything again; use it if a renamed item or new point on an existing map is missing.

### Settings

The settings are in their own window, in sections (search window, search options, search data, world map, portals, chat). Right-click the Tyria Search icon at the top left to open or close it (or left-click, with Swap icon clicks on), or press **Open settings window** on the module's page in Blish HUD's settings.

- **Open search hotkey** – unset by default.
- **Swap icon clicks** (off) – left-click the Tyria Search icon for the settings and right-click it for the search window.
- **Second search language** (None) – names are shown in Blish HUD's language; searches also match names in this language. None turns it off.
- **Open map on Show on map** (on) – opens the world map after Show on map, if it isn't open already.
- **Game's map key** (M) – the World Map key from the game's Control Options, pressed by Open map on Show on map.
- **Clear map markers hotkey** (unbound) – removes all map markers, also while the map is open.
- **Show zone portals on the world map** (off) – marks every portal on the world map with the game's icons: blue zone portals, purple asura gates, and dungeon, raid and fractal entrances, labelled "To <map>" once you zoom in. Covers core Tyria through End of Dragons, from Tekkit's All-In-One marker pack; later maps and the WvW borderlands have none yet.
- **Learn new portals** (on) – when you take a portal Tyria Search doesn't know, a panel at the top of the screen (drag it anywhere; it remembers) asks whether to add it, with where it leads (the destination map's name, editable) and its kind (zone portal, asura gate, dungeon, raid or fractal entrance). It then asks you to go back through it; the way back is noticed like any other portal and offered as the return portal (or press **Skip**). Each map change that isn't taken as a new portal is logged in the Blish HUD log with the reason. Added portals are saved to `learned-portals.json` in the `tyriasearch` folder, in the bundled list's format. A portal counts when the map changes within a few seconds of moving, with the world map closed, away from any known portal; waypoints, teleport items and joining a squad member are left out.
- **Search waypoints**, **Search points of interest**, **Search vistas**, **Search portals**, **Search skills**, **Search traits** (all on) – which kinds of result to include. These are downloaded together, so turning one off only leaves it out of the results.
- **Search NPCs** (on) – also downloads English map data if neither search language is English, since NPC places are matched by English name.
- **Search items** (on).
- **Search the wiki when nothing matches** (on) – off: offer to open a wiki search in your browser instead.
- **Restore clipboard** (on) – put back what you had copied after Paste into chat or Whisper to yourself.
- **Key press delay** (60 ms, 20–250) – raise it if pasted or whispered text is sometimes typed wrong.
- **Left-click a result** (Copy chat link) and **Right-click a result** (Show menu) – also Paste into chat, Whisper to yourself, Show on map, Open wiki page, Copy wiki command or Toggle favourite. If the result has no chat link or map position, its wiki page opens instead.
- **Update data** button – downloads all search data again now (NPCs and items only when they're on).
- **Learned portals** list (in the Portals section) – every learned portal, with where it leads and its kind to edit (saved on Enter, or when you pick a kind) and a **Delete** button.
- **Forget learned portals** button – removes every portal Learn new portals added.
