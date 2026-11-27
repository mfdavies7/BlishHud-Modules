# Wiki Search

Shift + left-click an item in Guild Wars 2 to open its wiki page in your browser. It works on anything you can shift-click into chat: items in your inventory, bank and the Trading Post, rewards, skills, and points on the world map.

Inspired by the Wiki Search feature of KenediaDev's TyrianQoL; written from scratch.

## How to Use

1. Hold Shift. A hint next to the cursor shows that a click will search (it hides while the cursor is over a corner icon or a Blish HUD window).
2. Left-click an item. Its wiki page opens in your browser.

To shift-click items into chat as usual, turn Wiki Search off with the Toggle Hotkey or by right-clicking its icon at the top left (or, with Turn Off on Right-Click on, by right-clicking anywhere in the game). The icon is greyed out while Wiki Search is off, and it stays off or on when Blish HUD restarts.

While Copy Item Name or Item Destruction is turned on, Wiki Search leaves shift-clicks to them.

## Settings

Left-click the Wiki Search icon at the top left to open the settings, or press **Settings** on the module's page in Blish HUD's settings.

- **Register Corner Icon** (on) – show the Wiki Search icon at the top left. Its tooltip shows the Toggle Hotkey in square brackets after its name while one is set.
- **Corner Icon Left Click Action** (Open Settings) and **Corner Icon Right Click Action** (Toggle Wiki Search) – Open Settings, Toggle Wiki Search, Open Wiki (the wiki's main page) or Nothing.
- **Toggle Hotkey** (unset) – turns Wiki Search on or off.
- **Enabled** (on) – turn it off to shift-click items into chat normally. It stays as you left it when Blish HUD restarts.
- **Turn Off on Right-Click** (off) – right-click anywhere in the game to turn Wiki Search off. Turn it back on with the Toggle Hotkey or the corner icon.
- **Wiki Language** (Game Language) – Game Language, English, German, French or Spanish.
- **Overwrite Unsent Chat Text** (off) – also search when chat is already open, deleting any text you typed but didn't send. That text can't be recovered. Off: with chat open, shift-click links items into your message as usual.
- **Work When the Map Is Open** (on) – off: shift-clicking map points links them as usual.
- **Return to the Game After Searching** (off) – switch back to Guild Wars 2 once the browser opens.
- **Key Press Delay (ms)** (60, 20–250) – raise it if the command is sometimes typed wrong.
- **Wait for Shift Release Before Sending** (off) – turn it on if the command goes missing or the link gets replaced.
- **Show Hint While Holding Shift** (on) – show the hint next to the cursor.
- **Hide Hint Over Blish HUD Windows** (on) – hide the hint while the cursor is over a Blish HUD window, menu or another module's panel. Always hidden over corner icons (Blish HUD's own or Blish Icon Manager's). Greyed out while Show Hint While Holding Shift is off.

## Troubleshooting

- **The command is typed wrong or not sent:** raise Key Press Delay, or turn on Wait for Shift Release Before Sending.
- **Nothing happens:** check that Wiki Search is on (the icon isn't greyed out), and that chat was closed when you clicked, unless Overwrite Unsent Chat Text is on.

## Install

Build `bytex.wikisearch_<version>.bhm` (see the [main README](../README.md#build)) and copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).

## Legal

Wiki Search is an unofficial fan project. It is not affiliated with, endorsed by or sponsored by ArenaNet or NCSOFT.

© ArenaNet LLC. All rights reserved. NCSOFT, ArenaNet, Guild Wars, Guild Wars 2, GW2, Heart of Thorns, Path of Fire, End of Dragons, Secrets of the Obscure, Janthir Wilds, Visions of Eternity, and all associated logos, designs, and composite marks are trademarks or registered trademarks of NCSOFT Corporation. All other trademarks are the property of their respective owners.
