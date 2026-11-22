# Wiki Search

Shift + left-click an item in Guild Wars 2 to open its wiki page.

GW2 puts the item's chat link in the chat box; the module adds `/wiki ` in front of it and presses Enter, so the game opens the wiki in your browser.
Inspired by the Wiki Search feature of KenediaDev's TyrianQoL; written from scratch.

## Settings

The settings are in their own window. Click the Wiki Search icon at the top left to open or close it (by default; see Corner Icon Left Click Action), or press **Open Settings Window** on the module's page in Blish HUD's settings.

- **Register Corner Icon** (on) – register the Wiki Search icon at the top left. The icon is greyed out while Enabled is off, so it shows whether Wiki Search is on. Off: the icon is removed and the two click action settings are disabled.
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

The icon (`ref/textures/WikiSearch.png`) is the same one the hint shows. It is from [KenediaDev's TyrianQoL](https://github.com/KenediaDev/Kenedia.BlishHUD).

## Install

Build `mfdavies7.bh.WikiSearch.bhm` (see the [main README](../README.md#build)) and copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).
