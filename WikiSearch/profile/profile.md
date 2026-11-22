# Wiki Search

Shift + left-click an item in Guild Wars 2 to open its wiki page.

When you shift-click an item, the game puts its chat link in the chat box. Wiki Search adds `/wiki ` in front of it and sends it, so the game opens the item's wiki page in your browser. It works on anything you can shift-click into chat: items in your inventory, bank and the Trading Post, rewards, skills, and points on the world map.

## Features

- **One click:** no typing, no copying names. Hold Shift, click, read.
- **Your language:** search the English, German, French or Spanish wiki, or follow your game client's language.
- **A hint while you hold Shift**, so you know a click will search. It only shows when a click would actually search.
- **Toggle it any time:** with a hotkey, or by right-clicking its icon at the top left. The icon greys out while Wiki Search is off, so you can tell at a glance.
- **Safe with chat:** if chat is already open with a message you're typing, shift-click links the item into your message as usual (unless you choose otherwise).
- **Your clipboard is left alone:** the command is typed through the clipboard, and whatever you had copied is put back afterwards.

## Settings

Left-click the Wiki Search icon at the top left to open its settings, or press **Settings** on the module's page in Blish HUD.

| Setting | Default | What it does |
| --- | --- | --- |
| Register Corner Icon | On | Shows the Wiki Search icon at the top left. |
| Corner Icon Left / Right Click Action | Open Settings / Toggle Wiki Search | Open Settings, Toggle Wiki Search, Open Wiki (the main page) or Nothing. |
| Toggle Hotkey | Unset | Turns Wiki Search on or off. |
| Enabled | On | Turn off to shift-click items into chat normally. |
| Wiki Language | Game Language | Game Language, English, German, French or Spanish. |
| Overwrite Unsent Chat Text | Off | Also search when chat is already open, replacing any unsent text. That text can't be recovered. |
| Work When the Map Is Open | On | Off: shift-clicking map points links them as usual. |
| Return to the Game After Searching | Off | Switch back to Guild Wars 2 once the browser opens. |
| Key Press Delay (ms) | 60 | Raise it if the command is sometimes typed wrong. |
| Wait for Shift Release Before Sending | Off | Turn on if the command goes missing or the link gets replaced. |
| Show Hint While Holding Shift | On | Shows the hint next to the cursor. |

The hint can't tell whether the cursor is over an item; Blish HUD and the game don't expose that.

## Troubleshooting

- **The command is typed wrong or not sent:** raise Key Press Delay, or turn on Wait for Shift Release Before Sending.
- **Nothing happens:** check that Wiki Search is on (the icon isn't greyed out), and that chat was closed when you clicked, unless Overwrite Unsent Chat Text is on.

## Credits

Inspired by the Wiki Search feature of KenediaDev's TyrianQoL; written from scratch.

Found a bug or have an idea? [Open an issue on GitHub](https://github.com/mfdavies7/BlishHud-Modules/issues).
