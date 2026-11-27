# Waypoint Paste

Press a hotkey, or click the waypoint icon at the top left, to whisper a waypoint's chat link to your own character. The link shows up in chat, ready to click to travel or to share.

Inspired by the Waypoint Paste feature of KenediaDev's TyrianQoL.

## How to Use

1. Shift-click a waypoint on the world map to put its chat code in chat, and copy it (or keep the default, `[&BCAJAAA=]`).
2. Left-click the Waypoint Paste icon to open the settings, and paste the code into **Waypoint Chat Code**.
3. Set **Paste Waypoint Hotkey**, or right-click the icon. Close chat first.

If you've copied a waypoint, point of interest or vista code, that one is whispered instead.

To have the link clicked for you, turn on **Auto-Click Chat Link**, send a whisper once, hover over its link in chat and press **Set Click Position Hotkey**. Set it again if you move or resize chat.

Chat stays on the whisper channel afterwards, so a message you type next also goes to yourself, unless Switch to /s After Whisper is on.

## Settings

Left-click the Waypoint Paste icon at the top left to open the settings, or press **Settings** on the module's page in Blish HUD's settings. Each slider has a box to type its value in, and a **Default** button (greyed out at the default) that puts it back.

- **Register Corner Icon** (on) – show the Waypoint Paste icon at the top left. Its tooltip shows the Paste Waypoint Hotkey in square brackets after its name while one is set.
- **Corner Icon Left Click Action** (Open Settings) and **Corner Icon Right Click Action** (Paste and Click Waypoint) – Open Settings, Paste Waypoint, Paste and Click Waypoint (clicks the link only while Auto-Click Chat Link is on) or Nothing.
- **Paste Waypoint Hotkey** (unset) – whispers the waypoint. Works in game with chat closed.
- **Waypoint Chat Code** (`[&BCAJAAA=]`) – the waypoint to whisper. Shift-click a waypoint on the map to get its code.
- **Use Copied Waypoint First** (on) – if you've copied a waypoint, point of interest or vista code, whisper that instead.
- **Overwrite Unsent Chat Text** (off) – if chat holds text you typed but didn't send, delete it and send the whisper. That text can't be recovered. Off: stop and leave your text in chat.
- **Switch to /s After Whisper** (off) – switch chat back to /s afterwards, so your next message isn't whispered to yourself.
- **Auto-Click Chat Link** (off) – click the link in chat once the whisper is sent, then put the cursor back.
- **Set Click Position Hotkey** (unset) – hover over the whisper's link in chat and press this to save where to click.
- **Click Position X / Y** (0, 0: not set) – where the link is clicked; type to fine-tune.
- **Show Click Position** (off) – always mark the click position with a crosshair, to check it's over the link.
- **Wait Before Clicking Link (ms)** (500, 20–3000) – raise it if the click happens before the whisper shows up in chat.
- **Key Press Delay (ms)** (60, 20–250) – raise it if the whisper is sometimes typed wrong.

## Install

Build `bytex.waypointpaste_<version>.bhm` (see the [main README](../README.md#build)) and copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).

## Legal

Waypoint Paste is an unofficial fan project. It is not affiliated with, endorsed by or sponsored by ArenaNet or NCSOFT.

© ArenaNet LLC. All rights reserved. NCSOFT, ArenaNet, Guild Wars, Guild Wars 2, GW2, Heart of Thorns, Path of Fire, End of Dragons, Secrets of the Obscure, Janthir Wilds, Visions of Eternity, and all associated logos, designs, and composite marks are trademarks or registered trademarks of NCSOFT Corporation. All other trademarks are the property of their respective owners.
