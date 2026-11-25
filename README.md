# Blish HUD modules

Guild Wars 2 quality-of-life modules for [Blish HUD](https://blishhud.com). Each module has its own README with what it does and every setting.

| Module | What it does |
| --- | --- |
| [Wiki Search](WikiSearch/README.md) | Shift + left-click an item in game to open its wiki page. |
| [Waypoint Paste](WaypointPaste/README.md) | Press a hotkey to whisper a waypoint chat link to yourself, ready to click or share. |
| [Tyria Search](TyriaSearch/README.md) | Search waypoints, points of interest, vistas, hearts, NPCs, events, portals, items, skills and traits, then copy, paste or whisper their chat links, or mark them on the map. |

Every module keeps its settings in its own window, opened from its icon at the top left or from the **Settings** button on the module's page in Blish HUD's settings.

## Build

Requires the .NET SDK.

```
dotnet build WikiSearch -c Release
dotnet build WaypointPaste -c Release
dotnet build TyriaSearch -c Release
```

Each module is written to `<Module>/bin/Release/bytex.<module>_<version>.bhm` (the version from its `manifest.json`). Copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).

## Credits

Wiki Search and Waypoint Paste are inspired by KenediaDev's TyrianQoL, and Tyria Search combines ideas from previous search modules (Universal Search, Tyria's GPS, V.E.L.O.C.I.T.Y. and NPC Finder).

## Legal

Wiki Search, Waypoint Paste and Tyria Search are unofficial fan projects. They are not affiliated with, endorsed by or sponsored by ArenaNet or NCSOFT.

© ArenaNet LLC. All rights reserved. NCSOFT, ArenaNet, Guild Wars, Guild Wars 2, GW2, Heart of Thorns, Path of Fire, End of Dragons, Secrets of the Obscure, Janthir Wilds, Visions of Eternity, and all associated logos, designs, and composite marks are trademarks or registered trademarks of NCSOFT Corporation. All other trademarks are the property of their respective owners.
