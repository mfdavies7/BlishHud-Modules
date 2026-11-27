# Quick Item

Put bars of your inventory items anywhere on screen. Click one, or press its hotkey, and Quick Item opens your inventory, finds the item, double-clicks it and closes the inventory again. Your cursor goes back where it was. Keep different sets of bars in layouts that switch by themselves for each character or profession, and press the inventory's Deposit All Materials button from a bar or a hotkey.

**Quick Item is off in PvP and WvW:** in structured PvP (the Heart of the Mists, arenas, tournaments) and World vs. World (the borderlands, Eternal Battlegrounds, Edge of the Mists, Armistice Bastion, Obsidian Sanctum) its bars are hidden, its hotkeys let every key through to the game, and it uses nothing. The icon at the top left is greyed out while it's off.

A rewrite of Pentose's Quick Items, without its native screen-capture library and bundled item lists.

## How to Use

1. Right-click the Quick Item icon at the top left to edit the bars. The first time, an empty bar is already waiting in the middle of the screen.
2. Click **+** on a bar and choose an item. **Deposit All Materials** is always at the top of the list. With an API key, the items in your bags are listed (this character's first); otherwise paste an item's chat link (shift-click it into chat, then select and copy it) or its ID.
3. Drag the bar's header to move it, and drag items to rearrange them. Right-click a bar or an item for more: columns, icon size, hotkeys, which layouts a bar is in, showing a bar on one character only, removing items and bars.
4. Right-click the icon again (or choose **Done Editing**) to lock the bars. Now double-click an item to use it.

The item must be in this character's bags, and chat must be closed. If the inventory is already open, it's used as it is and left open.

### Layouts

A layout is a set of bars. One is shown at a time, and only the hotkeys of the items on its bars work, so the same key can use different items in different layouts. Bars can also be shown in every layout (right-click a bar while editing, **Show in All Layouts**), for things you want everywhere.

- **Make and manage them** in the settings, under Layouts: **New Layout**, and for each one its name, **Switch To**, **Duplicate** (copies its bars too), **Delete** (click twice if it has bars) and a hotkey that switches to it.
- **Switch by themselves:** tick **Use on <character>** to show a layout whenever you play that character, or **Use as Any <profession>** for every character of that profession. A character's layout comes before its profession's. Switching by hand overrides them until you change character.
- **Switch by hand:** a layout's hotkey, **Next Layout Hotkey**, **Switch Layout** on a bar's right-click menu, or the corner icon set to **Choose Layout**. Hotkeys say which layout they switched to.
- **Move bars between them:** while editing, right-click a bar for **Move to Layout** or **Copy to Layout**.

Your bars from before layouts existed are in the first layout, **Main**.

### Deposit All Materials

Quick Item can press the inventory's **Deposit All Materials** button (the box with an arrow at the window's top right) for you: put it on a bar (it's the first entry when adding an item) or give it a key with **Deposit All Materials Hotkey**. It opens the inventory if needed, finds the button next to the bag picture, clicks it and closes the inventory again.

### Large bags

If the item isn't in view, Quick Item scrolls the inventory to find it: up to the top, then down to the bottom, stopping as soon as it's found, and afterwards scrolls back to where you had it. It finds the inventory window by the bag picture at its top left, so keep that on screen. If the inventory is already open, Quick Item leaves it open, wherever you've moved it. The cursor goes over the inventory only for each turn of the wheel and the double-click; in between it waits in the game window's top right corner, so item tooltips don't cover the icons. When the use is done it goes back where you had it.

### How the item is found

Quick Item takes a screenshot of the Guild Wars 2 window alone, without Blish HUD or anything else drawn over it, and looks for the item's icon, in colour, so items that share a picture in different colours aren't confused. If a bar covers the item, it's hidden for a moment so it doesn't take the click. Other Blish HUD windows over the item can still get in the way of the click.

If Windows can't provide the game window's own picture (it comes back blank on some systems), Quick Item copies the screen over the game window instead, hiding your bars for that moment.

## Settings

Left-click the Quick Item icon at the top left to open the settings, or press **Settings** on the module's page in Blish HUD's settings.

### Corner Icon

- **Register Corner Icon** (on) – show the Quick Item icon at the top left. Its tooltip shows the Show or Hide Bars Hotkey in square brackets after its name while one is set. Off: edit the bars with Edit Bars in the settings.
- **Corner Icon Left Click Action** (Open Settings) and **Corner Icon Right Click Action** (Edit Bars) – Open Settings, Edit Bars, Show or Hide Bars, Choose Layout (a menu of the layouts) or Nothing.

### Bars

- **Show Bars** (on) – show the bars on screen. Item hotkeys work either way.
- **Show or Hide Bars Hotkey** (unset) – turns Show Bars on or off.
- **Edit Bars** (off) – move, arrange and add items. Clicking an item doesn't use it while editing. Turned off again when Blish HUD starts.
- **Show in Combat** (on) – keep the bars on screen in combat.
- **Show While the Map Is Open** (off) – keep the bars on screen while the world map is open.
- **Use Items With** (Double-Click) – Click or Double-Click.
- **Show Hotkeys on Icons** (on) – write each item's hotkey in the corner of its icon.
- **New Bar** – add an empty bar to the layout in use, in the middle of the screen, and start editing.

### Layouts

- **Next Layout Hotkey** (unset) – switches to the next layout, in the order they're listed.
- The list of layouts, each with its name, **Switch To**, **Delete**, **Hotkey** (and **Clear**), **Duplicate**, **Use on <character>** and **Use as Any <profession>** (see [Layouts](#layouts)).
- **New Layout** – add an empty layout, switch to it, and start editing its first bar.

### Using Items

- **Deposit All Materials Hotkey** (unset) – opens the inventory, presses its Deposit All Materials button, and closes it again.
- **Inventory Key** (I) – your game's Inventory key (Options, Control Options).
- **Close Inventory Afterwards** (on) – close the inventory again if Quick Item opened it.
- **Wait for Inventory (ms)** (1000, 200–3000) – the longest to wait for the inventory to open. Quick Item carries on as soon as the inventory is fully drawn; raise this if it sometimes takes longer to appear.
- **Scroll the Inventory** (on) – scroll the inventory to find items that aren't in view, then scroll it back.
- **Scroll Step (Notches)** (2, 1–10) – mouse wheel notches per scroll step. Lower it if items further down are skipped; raise it to get through large bags faster.
- **Match Tolerance** (7, 1–30) – how different from its icon the item on screen may look. Raise it if items aren't found; lower it if the wrong item is used.
- **Key Press Delay (ms)** (60, 20–250) – time between simulated key presses and clicks. Raise it if the inventory doesn't open or the item isn't used.
- **Save Search Screenshots** (off) – save each search's screenshot with the best match boxed and scored; the last 10 are kept. Each use's steps and timings also go in the Blish HUD log. **Open Screenshot Folder** opens them.

### Item Hotkeys

Right-click an item and choose **Set Hotkey** to give it a key of its own. It works while the bars are hidden too, but not while you're typing in chat, and only while its bar's layout is in use. The key is kept from the game, so pick one you don't need for anything else. In PvP and WvW every hotkey is off, and the keys go to the game as usual.

## API Key

Listing your items needs an API key with the **account**, **characters** and **inventories** permissions (add one in Blish HUD's settings, under API Keys). Everything else works without one. The API updates inventories every few minutes, so a newly looted item may not be listed yet; paste its chat link instead.

## Troubleshooting

- **"Couldn't find your inventory window":** the inventory didn't open (check Inventory Key), or the bag picture at its top left is off screen or covered. Move the window so it's in view.
- **Items further down are skipped:** lower Scroll Step (Notches).
- **"Not in your inventory":** check the item is in this character's bags. Turn on Save Search Screenshots and try again: if the box is on the right item, raise Match Tolerance above the score shown.
- **The wrong item is used:** lower Match Tolerance.
- **The inventory doesn't open:** set Inventory Key to your game's key, or raise Key Press Delay.
- **"Couldn't find Deposit All Materials":** the button at the inventory's top right is covered, or the window is too narrow to show it. Turn on Save Search Screenshots and try again to see where it looked.
- **The bars are gone and hotkeys do nothing:** if the icon is greyed out, you're in PvP or WvW (including the Heart of the Mists and Armistice Bastion), where Quick Item is off. Or the layout in use has no bars: switch layouts, or check which layout is set for this character.

## Install

Build `bytex.quickitem_<version>.bhm` (see the [main README](../README.md#build)) and copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).

## Credits

Based on the idea of [Quick Items](https://github.com/tentose/Gw2QuickItem) by Pentose (MIT licence); written from scratch.

## Legal

Quick Item is an unofficial fan project. It is not affiliated with, endorsed by or sponsored by ArenaNet or NCSOFT.

© ArenaNet LLC. All rights reserved. NCSOFT, ArenaNet, Guild Wars, Guild Wars 2, GW2, Heart of Thorns, Path of Fire, End of Dragons, Secrets of the Obscure, Janthir Wilds, Visions of Eternity, and all associated logos, designs, and composite marks are trademarks or registered trademarks of NCSOFT Corporation. All other trademarks are the property of their respective owners.
