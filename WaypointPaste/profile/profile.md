# Waypoint Paste

Press a hotkey, or click the waypoint icon at the top left, to whisper a waypoint's chat link to your own character. The link shows up in chat, ready to click to travel or to share with your squad.

## Features

- **One key to your favourite waypoint:** save its chat code once and paste it whenever you need it.
- **Use what you copied:** if your clipboard holds a waypoint, point of interest or vista code, that one is pasted instead.
- **Auto-click the link:** optionally click the link in chat for you, then put your cursor back where it was. A crosshair shows where it will click, so you can line it up.
- **Back to /s:** optionally switch chat back to /s afterwards, so your next message isn't whispered to yourself.
- **Stops safely:** if you alt-tab, close chat or hit a loading screen partway through, it stops and tells you why instead of typing into the wrong place.
- **Your clipboard is left alone:** the whisper is typed through the clipboard, and whatever you had copied is put back afterwards.

## Getting Started

1. Shift-click a waypoint on the world map to put its chat code in chat, and copy it (or keep the default, `[&BCAJAAA=]`).
2. Open the settings by left-clicking the Waypoint Paste icon, and paste the code into **Waypoint Chat Code**.
3. Set **Paste Waypoint Hotkey**, or right-click the icon.

To have the link clicked for you: turn on **Auto-Click Chat Link**, send a whisper once, hover over its link in chat and press **Set Click Position Hotkey**. Set it again if you move or resize chat.

## Settings

| Setting | Default | What it does |
| --- | --- | --- |
| Register Corner Icon | On | Shows the Waypoint Paste icon at the top left. |
| Corner Icon Left / Right Click Action | Open Settings / Paste and Click Waypoint | Open Settings, Paste Waypoint, Paste and Click Waypoint (clicks only with Auto-Click Chat Link on) or Nothing. |
| Paste Waypoint Hotkey | Unset | Whispers the waypoint. Works in game with chat closed. |
| Waypoint Chat Code | `[&BCAJAAA=]` | The waypoint to paste. |
| Use Copied Waypoint First | On | Paste a waypoint, point of interest or vista code from your clipboard instead. |
| Overwrite Unsent Chat Text | Off | Delete text you typed but didn't send. Off: stop and leave it. |
| Switch to /s After Whisper | Off | Switch chat back to /s afterwards. |
| Auto-Click Chat Link | Off | Click the link in chat after the whisper is sent. |
| Set Click Position Hotkey | Unset | Saves the spot under the cursor as the link's position. |
| Click Position X / Y | 0, 0 (not set) | Where the link is clicked; type to fine-tune. |
| Show Click Position | Off | Always mark the click position with a crosshair. |
| Wait Before Clicking Link (ms) | 500 | Raise it if the click happens before the whisper shows up. |
| Key Press Delay (ms) | 60 | Raise it if the whisper is sometimes typed wrong. |

## Credits

Inspired by the Waypoint Paste feature of KenediaDev's TyrianQoL; written from scratch.

Found a bug or have an idea? [Open an issue on GitHub](https://github.com/mfdavies7/BlishHud-Modules/issues).
