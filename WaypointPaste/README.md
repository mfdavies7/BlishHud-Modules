# Waypoint Paste

Press a hotkey, or click the waypoint icon at the top left, to whisper a waypoint chat link to your own character. The link shows up in chat, ready to click or share. Unless Switch to /s After Whisper is on, chat stays on the whisper channel afterwards, so a message you type next is also whispered to yourself.
Inspired by the Waypoint Paste feature of KenediaDev's TyrianQoL; written from scratch.

When this module types into chat, it does so through the clipboard, then puts back whatever you had copied.

## Settings

The settings are in their own window. Click the Waypoint Paste icon at the top left to open or close it (by default; see Corner Icon Left Click Action), or press **Settings** on the module's page in Blish HUD's settings.

- **Register Corner Icon** (on) – register the Waypoint Paste icon at the top left. Off: the icon is removed and the two click action settings are disabled.
- **Corner Icon Left Click Action** (Open Settings) and **Corner Icon Right Click Action** (Paste and Click Waypoint) – Open Settings opens or closes the settings window; Paste Waypoint whispers the waypoint like the hotkey, without clicking the link; Paste and Click Waypoint also clicks the link, but only when Auto-Click Chat Link is on; Nothing ignores the click.
- **Paste Waypoint Hotkey** – unset by default. Only works in game with chat closed; with chat open it asks you to close it first. It stops and tells you why if you alt-tab, chat closes, or a loading screen starts partway through.
- **Waypoint Chat Code** (`[&BCAJAAA=]`) – shift-click a waypoint on the map to get its code. Invalid codes show a warning instead of sending.
- **Use Copied Waypoint First** (on) – if your clipboard holds a waypoint, point of interest or vista code, paste that instead.
- **Overwrite Unsent Chat Text** (off) – if chat still holds text you typed but didn't send, delete it and send the whisper. The deleted text can't be recovered. Off: stop and leave your text in chat.
- **Switch to /s After Whisper** (off) – switch chat back to /s after the whisper, so your next message isn't whispered to yourself. With Auto-Click Chat Link, this happens before the link is clicked.
- **Auto-Click Chat Link** (off) – after the whisper is sent, click its link in chat for you. The cursor goes back to where it was.
- **Set Click Position Hotkey** (unset) – send a whisper, hover over its link in chat, press this to save the spot. Set it again if you move or resize chat.
- **Click Position X / Y** – screen position of the link, filled in by the hotkey; type to fine-tune. 0, 0 means not set.
- **Show Click Position** (off) – mark the saved click position on screen with a crosshair, to check it's over the chat link. Off: it's still shown for a few seconds after Set Click Position Hotkey is pressed or the position is changed.
- **Wait Before Clicking Link (ms)** (500, 20–3000) – raise it if the click sometimes happens before the whisper shows up in chat.
- **Key Press Delay (ms)** (60, 20–250) – raise it if the whisper is sometimes typed wrong.

The icon (`ref/textures/WaypointPaste.png`) is the game's map waypoint icon (`map_waypoint` from the GW2 API), recoloured to match Blish HUD's corner icons.

## Install

Build `bytex.bh.WaypointPaste.bhm` (see the [main README](../README.md#build)) and copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).
