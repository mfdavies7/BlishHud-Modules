# Wiki Search (Blish HUD module)

Shift + left-click an item in Guild Wars 2 to open its wiki page.

GW2 puts the item's chat link in the chat box; the module adds `/wiki ` in front of it and presses Enter, so the game opens the wiki in your browser.
Inspired by the Wiki Search feature of KenediaDev's TyrianQoL; written from scratch.

## Settings

- **Wiki language** – Game Language, English, German, French or Spanish.
- **Toggle hotkey** – switch the Enabled setting on or off.
- **Enabled** (on) – turn it off to shift-click items into chat normally.
- **Work when chat is already open** (off) – when off, shift-click links items into your message as usual. When on, everything in the chat box is searched.
- **Work when the map is open** (on) – when off, shift-clicking map points links them as usual.
- **Return to the game after searching** (off) – switch back to GW2 once the browser opens.
- **Restore clipboard** (on) – put back what you had copied; the module pastes `/wiki ` through the clipboard.
- **Key press delay** (40 ms, 1–250) – raise it if the command is sometimes typed wrong.
- **Wait for Shift release before sending** (off) – only type `/wiki` after you let go of Shift. Turn on if the command goes missing or the link gets replaced.
- **Show hint while holding Shift** (on) – shown only when a click would search: enabled, in game, game focused, and chat or map closed unless the settings above allow them.

The hint can't tell whether the cursor is over an item; Blish HUD and GW2 don't expose that.

## Build

Requires the .NET SDK.

```
dotnet build WikiSearch -c Release
```

The module is written to `WikiSearch/bin/Release/WikiSearch.bhm`. Copy it into Blish HUD's `modules` folder (`Documents/Guild Wars 2/addons/blishhud/modules`).

# Waypoint Paste (Blish HUD module)

Press a hotkey to whisper a waypoint chat link to your own character. The link shows up in chat, ready to click or share. Afterwards chat is switched back to `/s`, so your next message isn't whispered to yourself.
Inspired by the Waypoint Paste feature of KenediaDev's TyrianQoL; written from scratch.

## Settings

- **Paste waypoint hotkey** – unset by default. Only works in game with chat closed; with chat open it asks you to close it first. It stops and tells you why if you alt-tab, chat closes, or a loading screen starts partway through.
- **Waypoint chat code** (`[&BCAJAAA=]`) – shift-click a waypoint on the map to get its code. Invalid codes show a warning instead of sending.
- **Use copied waypoint first** (off) – if your clipboard holds a waypoint, point of interest or vista code, paste that instead.
- **Restore clipboard** (on) – put back what you had copied; the module types through the clipboard.
- **Overwrite unsent chat text** (off) – if chat still holds text you typed but didn't send, delete it and paste the waypoint. Off: stop and leave your text in chat.
- **Auto Click Chat Link** (off) – after the whisper is sent, click its link in chat for you. The cursor goes back to where it was.
- **Set click position hotkey** – send a whisper, hover over its link in chat, press this to save the spot. Set it again if you move or resize chat.
- **Click position X / Y** – screen position of the link, filled in by the hotkey; type to fine-tune. 0, 0 means not set.
- **Wait before clicking link** (500 ms, 0–3000) – raise it if the click sometimes happens before the whisper shows up in chat.
- **Key press delay** (40 ms, 1–250) – raise it if the whisper is sometimes typed wrong.

```
dotnet build WaypointPaste -c Release
```

## Credits

The hint icon (`WikiSearch/ref/textures/WikiSearch.png`) is from [KenediaDev's TyrianQoL](https://github.com/KenediaDev/Kenedia.BlishHUD).
