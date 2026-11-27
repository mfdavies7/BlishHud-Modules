using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Controls.Extern;
using Blish_HUD.Input;
using Bytex.ModuleShared;
using Microsoft.Xna.Framework.Input;
using static Bytex.ModuleShared.ChatInput;
using Keyboard = Blish_HUD.Controls.Intern.Keyboard;
using Mouse = Blish_HUD.Controls.Intern.Mouse;
using MouseButton = Blish_HUD.Controls.Intern.MouseButton;

namespace Bytex.QuickItem
{
    /// <summary>The settings <see cref="ItemUser"/> reads, read again on every use.</summary>
    internal sealed class UseOptions
    {
        public Func<KeyBinding> InventoryKey;
        public Func<bool> CloseInventory;
        public Func<int> WaitForInventoryMs;
        public Func<double> Tolerance;
        public Func<int> KeyDelayMs;
        public Func<bool> SaveScreenshots;
        public string ScreenshotDirectory;
        /// <summary>Hides (true) or shows again (false) the bars, so they're neither in a screen copy nor under the click.</summary>
        public Action<bool> HideBars;
        /// <summary>Whether one of the bars is drawn over this screen position, where it would take the click.</summary>
        public Func<int, int, bool> BarAt;
        public Func<bool> ScrollInventory;
        public Func<int> ScrollNotches;
        /// <summary>The inventory window's emblem (the bag at its top left), to find the window by.</summary>
        public BgraImage InventoryEmblem;
        /// <summary>The middle of the inventory's Deposit All Materials button.</summary>
        public BgraImage DepositButton;
        /// <summary>Whether the player is in PvP or WvW, where Quick Item does nothing.</summary>
        public Func<bool> InPvp;
    }

    /// <summary>
    /// Where the inventory window is, from its emblem, in pixels from the top left of the game's client area. Only
    /// valid for a client area of the same size, since the game lays out its windows for that size.
    /// </summary>
    internal sealed class InventorySpot
    {
        // The emblem picture (cropped from inside the bag) is this share of a slot's size.
        private const double EmblemPerSlot = 52 / 67.0;
        // The first slot's centre from the emblem's centre, in slots. The window's top left is fixed to the emblem,
        // and the first column, a row below the search box, is in its scrolling grid at any window size.
        private const double FirstSlotRight = 1.2, FirstSlotDown = 2.91;

        /// <summary>The emblem's centre and size as drawn.</summary>
        public int EmblemX, EmblemY, EmblemSize;
        public int ClientWidth, ClientHeight;

        /// <summary>An inventory slot's size, in pixels.</summary>
        public int Slot => (int)Math.Round(EmblemSize / EmblemPerSlot);
        /// <summary>The first slot's centre: where the wheel scrolls the inventory.</summary>
        public int X => EmblemX + (int)Math.Round(Slot * FirstSlotRight);
        public int Y => EmblemY + (int)Math.Round(Slot * FirstSlotDown);

        /// <summary>The emblem's size for slots of about this size.</summary>
        public static int EmblemSizeFor(int slot) => (int)Math.Round(slot * EmblemPerSlot);

        // The Deposit All Materials button (the picture is its middle, cropped from inside) is this share of a slot,
        // and its centre is this many slots below the emblem's, in the row of icons at the window's top right. How far
        // right depends on the window's width.
        private const double DepositPerSlot = 20 / 67.0;
        private const double DepositDown = 0.93;

        /// <summary>The Deposit All Materials button's size as drawn.</summary>
        public int DepositSize => (int)Math.Round(Slot * DepositPerSlot);

        /// <summary>The strip of the screen to look for the Deposit All Materials button in: right of the emblem, around its row.</summary>
        public System.Drawing.Rectangle DepositStrip
        {
            get
            {
                int centre = EmblemY + (int)Math.Round(Slot * DepositDown);
                int left = Math.Max(0, EmblemX + Slot);
                int top = Math.Max(0, centre - Slot / 2);
                int bottom = Math.Min(ClientHeight, centre + Slot / 2);
                return new System.Drawing.Rectangle(left, top, Math.Max(0, ClientWidth - left), Math.Max(0, bottom - top));
            }
        }
    }

    /// <summary>
    /// Uses an item: opens the inventory if the item isn't on screen already, finds the item's icon in a screenshot,
    /// double-clicks it, puts the cursor back and closes the inventory again. One at a time.
    /// </summary>
    internal sealed class ItemUser
    {
        private static readonly Logger Logger = Logger.GetLogger<ItemUser>();

        private static readonly Keys[] Modifiers =
            { Keys.LeftShift, Keys.RightShift, Keys.LeftControl, Keys.RightControl, Keys.LeftAlt, Keys.RightAlt };

        // Inventory icon size in screen pixels at each interface size, before the game's DPI scaling.
        private static readonly int[] IconSizes = { 45, 50, 56, 62 }; // Small, Normal, Large, Larger

        private const int MaxScreenshots = 10;

        // Blish HUD's Mouse.RotateWheel takes the distance modulo a notch (120), so a whole notch would send nothing:
        // this is the most one call can send, and it's sent once for each notch.
        private const int WheelStep = 119;
        // Between the notches of one scroll step, about a game frame apart.
        private const int NotchGapMs = 20;
        // Scroll steps in one search, up and down together: the most a large inventory needs, and a limit on how far
        // the camera zooms if the wheel somehow misses the inventory.
        private const int MaxScrollSteps = 40;
        // The emblem scores 0.00 with the inventory open and about 0.1 while it fades in; nothing else scores below 0.3.
        private const double EmblemTolerance = 0.2;
        // The emblem's drawn size may be this share off the size worked out from the interface size.
        private const double EmblemSizeSpread = 0.15;
        // A pixel has changed if its colour moved this much (the three channels' differences added up), more than the
        // game world showing faintly through the inventory's background does.
        private const int PixelChange = 40;
        // The inventory moved if this share of the pixels around the spot changed. Scrolling changes most of them;
        // an animated slot (the glow on new items) only a few.
        private const double MovedShare = 0.04;

        // A fully drawn emblem scores 0.00; while the window fades in it scores about 1 at the same place.
        private const double EmblemDrawn = 0.03;
        // A match this close is certainly the item, so the search stops there (real matches score 0.00–0.02).
        private const double GoodEnough = 0.03;
        // The Deposit All Materials button scores 0.00 with the inventory open; the icons beside it 0.9, and nothing
        // else in its row below 0.4.
        private const double DepositTolerance = 0.15;
        // The finder needs pictures of at least this size; a smaller button is looked for in a doubled copy.
        private const int MinFindSize = 16;
        // The inventory is drawn about 0.45 s after the key; if it isn't at its last place by this time, it's elsewhere.
        private const int LastPlaceGraceMs = 600;
        // Between looks at the game while waiting for something to be drawn: a couple of game frames.
        private const int PollMs = 30;
        // The most looks while waiting for a scroll to finish; the game's smooth scroll takes a few frames.
        private const int MaxSettleLooks = 10;

        // After this many blank window captures in a row, stop trying them this session and copy the screen instead.
        private const int MaxWindowCaptureFailures = 3;
        private int _windowCaptureFailures;
        private bool _barsHidden;
        // Where the inventory window was last seen this session, checked first as it rarely moves; null if unknown.
        private InventorySpot _inventory;

        // Screenshots being saved, one after another.
        private readonly object _savingLock = new object();
        private Task _saving = Task.CompletedTask;
        // Where the player had the cursor before this use first moved it; null if it hasn't.
        private (int x, int y)? _playerCursor;
        // What the current use did and when, logged when it ends.
        private System.Text.StringBuilder _steps;
        private readonly Stopwatch _useWatch = new Stopwatch();

        private readonly IconCache _icons;
        private readonly UseOptions _options;
        private readonly CancellationToken _unloading;
        private int _busy;

        /// <summary>The item being used, for the bars to show; null when idle.</summary>
        public BarItem Current { get; private set; }

        public ItemUser(IconCache icons, UseOptions options, CancellationToken unloading)
        {
            _icons = icons;
            _options = options;
            _unloading = unloading;
        }

        /// <summary>Starts using the item unless another is still being used.</summary>
        /// <param name="fromHotkey">Waits for the hotkey's modifiers to be let go, since they'd change the clicks.</param>
        public void Use(BarItem item, bool fromHotkey)
        {
            if (item == null || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            Current = item;
            // Off Blish HUD's input thread: awaits would resume there, and if Blish HUD restarts that thread the use
            // never finishes and the module stays busy until reload.
            _ = Task.Run(() => UseAsync(item, fromHotkey));
        }

        private async Task UseAsync(BarItem item, bool fromHotkey)
        {
            _barsHidden = false;
            _steps = new System.Text.StringBuilder();
            _useWatch.Restart();
            string outcome = "done";
            try
            {
                EnsureAllowed();
                var gw2 = GameService.GameIntegration.Gw2Instance;
                if (!gw2.IsInGame) throw new AbortException("Only works in game.");
                // The inventory key would be typed into chat instead.
                if (GameService.Gw2Mumble.UI.IsTextInputFocused) throw new AbortException("Close chat first.");

                if (item.Action == BarItem.DepositMaterials)
                {
                    await DepositMaterialsAsync(fromHotkey);
                    return;
                }
                if (item.Action != null) throw new AbortException("This Quick Item version can't do that.");

                var icon = await _icons.GetAsync(item, _unloading);
                if (fromHotkey) await ReleaseModifiersAsync();

                await ChatActions.FocusGameAsync();

                var shot = Search(await CaptureAsync(), item, icon);
                bool opened = false;
                InventorySpot spot = null;
                if (shot.Found == null)
                {
                    // Pressing the inventory key would close an inventory that's open already, so the window is looked
                    // for everywhere unless it's at its last place: the player may have moved it.
                    spot = FindInventory(shot, search: true);
                    if (spot == null)
                    {
                        await PressInventoryKeyAsync();
                        opened = true;
                        (shot, spot) = await WaitForInventoryAsync(item, icon);
                    }
                }

                int scrolled = 0; // wheel steps from where the inventory was: negative up, positive down
                if (shot.Found == null && _options.ScrollInventory())
                {
                    spot = spot ?? FindInventory(shot, search: true);
                    // Not pressing the key again: without the window in sight there's no telling whether the inventory
                    // is open, and the key would toggle it.
                    if (spot == null) throw new AbortException("Couldn't find your inventory window. Check Inventory Key.");
                    (shot, scrolled) = await ScrollSearchAsync(item, icon, shot, spot);
                }

                if (shot.Found == null)
                {
                    await ScrollAsync(spot, -scrolled);
                    if (opened && spot != null && _options.CloseInventory()) await PressInventoryKeyAsync();
                    throw new AbortException(_options.ScrollInventory()
                        ? "Not in your inventory. If it is, raise Match Tolerance."
                        : "Not in view. Turn on Scroll the Inventory, or scroll to it.");
                }

                var (x, y) = shot.Found.Value;
                // The click goes to whatever is on top, so a bar over the item would take it.
                if (_options.BarAt(x, y)) await HideBarsAsync();
                await ClickAsync(x, y, clicks: 2);
                Step("double-clicked");
                if (opened && _options.CloseInventory()) await PressInventoryKeyAsync();
            }
            catch (AbortException ex)
            {
                outcome = ex.Message;
                Notify.Warning("Quick Item", ex.Message);
            }
            catch (OperationCanceledException) when (_unloading.IsCancellationRequested)
            {
                outcome = "stopped (module unloading)";
            }
            catch (Exception ex)
            {
                outcome = "failed";
                Logger.Warn(ex, $"Using {item.Name} failed.");
                Notify.Warning("Quick Item", "Something went wrong. See the Blish HUD log.");
            }
            finally
            {
                // Each step with its time, to see where a use spends it. In the log as Info while Save Search
                // Screenshots is on (it's for looking into searches), else only in Blish HUD's debug log.
                RestorePlayerCursor();
                string log = $"Used {item.Name} in {_useWatch.ElapsedMilliseconds} ms: {outcome}\n{_steps}";
                if (_options.SaveScreenshots()) Logger.Info(log.TrimEnd());
                else Logger.Debug(log.TrimEnd());
                _steps = null;
                if (_barsHidden) _options.HideBars(false);
                _barsHidden = false;
                Current = null;
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        /// <summary>
        /// Presses the inventory's Deposit All Materials button: opens the inventory if it isn't open, finds the button
        /// in the row of icons at the window's top right, clicks it, and closes the inventory again if it opened it.
        /// </summary>
        private async Task DepositMaterialsAsync(bool fromHotkey)
        {
            if (fromHotkey) await ReleaseModifiersAsync();
            await ChatActions.FocusGameAsync();

            var shot = await CaptureAsync();
            bool opened = false;
            var spot = FindInventory(shot, search: true);
            if (spot == null)
            {
                await PressInventoryKeyAsync();
                opened = true;
                (shot, spot) = await WaitForInventoryAsync(null, null);
            }
            // Not pressing the key again: without the window in sight there's no telling whether it's open.
            if (spot == null) throw new AbortException("Couldn't find your inventory window. Check Inventory Key.");

            var button = FindDepositButton(shot, spot);
            if (button == null)
            {
                if (opened && _options.CloseInventory()) await PressInventoryKeyAsync();
                throw new AbortException("Couldn't find Deposit All Materials.");
            }

            var (x, y) = button.Value;
            if (_options.BarAt(x, y)) await HideBarsAsync();
            await ClickAsync(x, y, clicks: 1);
            Step("clicked Deposit All Materials");
            if (opened && _options.CloseInventory()) await PressInventoryKeyAsync();
        }

        /// <summary>
        /// Where the Deposit All Materials button is (screen pixels), or null. It's looked for only in a strip right of
        /// the inventory's emblem, which is quick, and keeps the icons elsewhere on screen out of it.
        /// </summary>
        private (int x, int y)? FindDepositButton(Snapshot shot, InventorySpot spot)
        {
            var strip = spot.DepositStrip;
            int size = spot.DepositSize;
            if (strip.Width < size || strip.Height < size) return null;
            // A button drawn smaller than the finder can look for (a small interface at a low resolution) is looked for
            // in a copy of the strip at twice the size.
            int scale = size < MinFindSize ? 2 : 1;
            var image = Crop(shot.Screen, strip, scale);

            var watch = Stopwatch.StartNew();
            var match = IconFinder.Find(image, _options.DepositButton, size * scale, slotMask: false, sizeSpread: 0.1, goodEnough: GoodEnough);
            bool found = match != null && match.Value.Score <= DepositTolerance;
            Step($"looked for Deposit All Materials in {watch.ElapsedMilliseconds} ms: best {(match == null ? "none" : $"{match.Value.Score:F3} at {match.Value.X / scale + strip.X},{match.Value.Y / scale + strip.Y}")} (expected {size} px), {(found ? "found" : "not found")}");

            if (_options.SaveScreenshots())
            {
                var onScreen = match == null ? (IconMatch?)null
                    : new IconMatch(match.Value.X / scale + strip.X, match.Value.Y / scale + strip.Y, match.Value.Size / scale, match.Value.Score);
                SaveScreenshot(shot.Screen, onScreen, BarItem.DepositMaterialsName, found);
            }
            if (!found) return null;
            return (shot.Area.X + strip.X + match.Value.X / scale, shot.Area.Y + strip.Y + match.Value.Y / scale);
        }

        /// <summary>A part of an image, optionally enlarged by a whole number (each pixel repeated).</summary>
        private static BgraImage Crop(BgraImage image, System.Drawing.Rectangle part, int scale)
        {
            int width = part.Width * scale, height = part.Height * scale;
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int from = ((part.Y + y / scale) * image.Width + part.X) * 4;
                int to = y * width * 4;
                if (scale == 1)
                {
                    Buffer.BlockCopy(image.Pixels, from, pixels, to, width * 4);
                    continue;
                }
                for (int x = 0; x < width; x++)
                    Buffer.BlockCopy(image.Pixels, from + x / scale * 4, pixels, to + x * 4, 4);
            }
            return new BgraImage(width, height, pixels);
        }

        /// <summary>Stops a use as soon as the player is in PvP or WvW.</summary>
        private void EnsureAllowed()
        {
            if (_options.InPvp()) throw new AbortException("Off in PvP and WvW.");
        }

        /// <summary>A screenshot of the game, and where the item is in it (screen pixels), if it is.</summary>
        private sealed class Snapshot
        {
            public BgraImage Screen;
            public System.Drawing.Rectangle Area;
            public (int x, int y)? Found;
        }

        /// <summary>A screenshot of the game, not searched yet.</summary>
        private async Task<Snapshot> CaptureAsync()
        {
            EnsureAllowed();
            EnsureSafe(chatOpen: false);
            _unloading.ThrowIfCancellationRequested();
            var window = GameService.GameIntegration.Gw2Instance.Gw2WindowHandle;
            if (!ScreenGrab.TryGetClientArea(window, out var area)) throw new AbortException("Couldn't find the game window.");
            var watch = Stopwatch.StartNew();
            var screen = await CaptureScreenAsync(window, area);
            Step($"captured in {watch.ElapsedMilliseconds} ms");
            return new Snapshot { Screen = screen, Area = area };
        }

        /// <summary>Searches a screenshot for the item, filling in <see cref="Snapshot.Found"/>.</summary>
        private Snapshot Search(Snapshot shot, BarItem item, BgraImage icon)
        {
            int size = ExpectedIconSize(GameService.GameIntegration.Gw2Instance.Gw2WindowHandle);
            double tolerance = _options.Tolerance();
            var watch = Stopwatch.StartNew();
            // Anything this close is the item (real matches score 0.00–0.02, other icons 0.1 or more), so the other
            // candidates needn't be checked.
            var match = IconFinder.Find(shot.Screen, icon, size, goodEnough: Math.Min(tolerance, GoodEnough));
            bool found = match != null && match.Value.Score <= tolerance;
            Step($"searched in {watch.ElapsedMilliseconds} ms: best {(match == null ? "none" : $"{match.Value.Score:F3} at {match.Value.X},{match.Value.Y}, {match.Value.Size} px")} (expected {size} px), {(found ? "found" : "not found")}");

            if (_options.SaveScreenshots()) SaveScreenshot(shot.Screen, match, item.Name, found);
            shot.Found = found ? (shot.Area.X + match.Value.X, shot.Area.Y + match.Value.Y) : ((int, int)?)null;
            return shot;
        }

        /// <summary>
        /// Waits for the inventory window to open after the key, until it's fully drawn (its emblem matches exactly
        /// and the slots hold still between two looks), then searches it once. Each look only scores the emblem at
        /// its last place, which takes a millisecond; it's looked for everywhere only while that place isn't known.
        /// </summary>
        /// <param name="item">The item to search the last snapshot for; null to only wait for the window.</param>
        /// <returns>The last snapshot, searched, and the window's place, or null if it never appeared.</returns>
        private async Task<(Snapshot shot, InventorySpot spot)> WaitForInventoryAsync(BarItem item, BgraImage icon)
        {
            var watch = Stopwatch.StartNew();
            Snapshot previous = null;
            InventorySpot previousSpot = null;
            while (true)
            {
                await Task.Delay(PollMs);
                var shot = await CaptureAsync();
                // Checking only the last place takes a millisecond; but if the window hasn't shown up there by the
                // time it should have, the player moved it before closing it, so look everywhere.
                var spot = FindInventory(shot, search: _inventory == null || watch.ElapsedMilliseconds >= LastPlaceGraceMs);
                bool drawn = spot != null && IconFinder.ScoreAt(shot.Screen, _options.InventoryEmblem, spot.EmblemX, spot.EmblemY, spot.EmblemSize, slotMask: false) <= EmblemDrawn;
                if (drawn && previousSpot == spot && !Moved(previous, shot, spot))
                {
                    Step($"inventory drawn after {watch.ElapsedMilliseconds} ms");
                    return (item == null ? shot : Search(shot, item, icon), spot);
                }
                if (watch.ElapsedMilliseconds >= _options.WaitForInventoryMs())
                {
                    // It may have opened somewhere else than last time.
                    Step($"stopped waiting for the inventory after {watch.ElapsedMilliseconds} ms");
                    return (item == null ? shot : Search(shot, item, icon), spot ?? FindInventory(shot, search: true));
                }
                previous = shot;
                previousSpot = drawn ? spot : null;
            }
        }

        /// <summary>
        /// After a scroll, waits for the inventory to stop moving: the game scrolls smoothly over a few frames, and an
        /// item found mid-way would have moved on by the time it's clicked.
        /// </summary>
        /// <returns>The snapshot once still, and whether the inventory moved since <paramref name="before"/>.</returns>
        private async Task<(Snapshot shot, bool moved)> SettleAsync(Snapshot before, InventorySpot spot)
        {
            // Still once two looks in a row are the same.
            var shot = await CaptureAsync();
            for (int look = 0; look < MaxSettleLooks; look++)
            {
                await Task.Delay(PollMs);
                var next = await CaptureAsync();
                bool changed = Moved(shot, next, spot);
                shot = next;
                if (!changed) break;
            }
            // Only the still picture is compared: on the way there, the first slot's hover highlight (the cursor was
            // just over it) fades out, which isn't the inventory moving. At the top or bottom nothing else changes.
            return (shot, Moved(before, shot, spot));
        }

        /// <summary>Notes a step of the current use, with the time since it started, for the log.</summary>
        private void Step(string what) => _steps?.AppendLine($"  +{_useWatch.ElapsedMilliseconds,5} ms  {what}");

        /// <summary>
        /// Where the inventory window is in a screenshot, from its emblem, or null if it isn't open. Checks where it
        /// was last seen; with <paramref name="search"/>, then looks everywhere.
        /// </summary>
        private InventorySpot FindInventory(Snapshot shot, bool search)
        {
            var emblem = _options.InventoryEmblem;
            var known = _inventory;
            if (known != null && known.ClientWidth == shot.Area.Width && known.ClientHeight == shot.Area.Height
                && IconFinder.ScoreAt(shot.Screen, emblem, known.EmblemX, known.EmblemY, known.EmblemSize, slotMask: false) <= EmblemTolerance)
                return known;
            if (!search) return null;

            int expected = InventorySpot.EmblemSizeFor(ExpectedIconSize(GameService.GameIntegration.Gw2Instance.Gw2WindowHandle));
            var watch = Stopwatch.StartNew();
            var match = IconFinder.Find(shot.Screen, emblem, expected, slotMask: false, sizeSpread: EmblemSizeSpread, goodEnough: EmblemDrawn);
            Step($"looked for the inventory everywhere in {watch.ElapsedMilliseconds} ms: best {(match == null ? "none" : $"{match.Value.Score:F3} at {match.Value.X},{match.Value.Y}, {match.Value.Size} px")} (expected {expected} px)");
            if (match == null || match.Value.Score > EmblemTolerance) return null;
            return _inventory = new InventorySpot
            {
                EmblemX = match.Value.X, EmblemY = match.Value.Y, EmblemSize = match.Value.Size,
                ClientWidth = shot.Area.Width, ClientHeight = shot.Area.Height,
            };
        }

        /// <summary>
        /// Scrolls the inventory up to the top, then down to the bottom, searching after every step, until the item
        /// is found. The top and bottom are where a step no longer moves anything around the spot.
        /// </summary>
        /// <returns>The last snapshot, and how many steps the inventory ended up from where it started.</returns>
        private async Task<(Snapshot shot, int scrolled)> ScrollSearchAsync(BarItem item, BgraImage icon, Snapshot shot, InventorySpot spot)
        {
            int scrolled = 0, steps = 0;
            foreach (int direction in new[] { -1, 1 })
            {
                while (steps++ < MaxScrollSteps)
                {
                    await ScrollAsync(spot, direction, settleNext: true);
                    bool moved;
                    (shot, moved) = await SettleAsync(shot, spot);
                    Step($"scrolled {(direction < 0 ? "up" : "down")}{(moved ? "" : ", but nothing moved")}");
                    if (!moved) break; // the top (or bottom): what's in view was searched already
                    scrolled += direction;
                    if (Search(shot, item, icon).Found != null) return (shot, scrolled);
                }
            }

            if (steps > MaxScrollSteps) Logger.Info("Scrolling never reached the top and bottom of the inventory.");
            return (shot, scrolled);
        }

        /// <summary>
        /// Turns the mouse wheel over the spot by <paramref name="steps"/> steps (negative is up), with the cursor parked
        /// in a corner afterwards: an item's tooltip or hover highlight would change the next screenshot.
        /// </summary>
        /// <param name="settleNext">The caller waits for the inventory to settle itself (<see cref="SettleAsync"/>),
        /// so there's no need to wait a fixed time for it here.</param>
        private async Task ScrollAsync(InventorySpot spot, int steps, bool settleNext = false)
        {
            if (spot == null || steps == 0) return;
            var window = GameService.GameIntegration.Gw2Instance.Gw2WindowHandle;
            if (!ScreenGrab.TryGetClientArea(window, out var area)) return;
            int x = area.X + spot.X, y = area.Y + spot.Y;
            int delay = _options.KeyDelayMs();
            int distance = Math.Sign(steps) * -WheelStep; // a positive wheel turn scrolls up
            int notches = Math.Max(1, _options.ScrollNotches());

            SavePlayerCursor();
            try
            {
                for (int i = 0; i < Math.Abs(steps); i++)
                {
                    EnsureAllowed();
                    EnsureSafe(chatOpen: false);
                    Mouse.SetPosition(x, y, true);
                    await Task.Delay(delay); // GW2 must see the cursor over the inventory before the wheel turns
                    for (int notch = 0; notch < notches; notch++)
                    {
                        if (notch > 0) await Task.Delay(NotchGapMs);
                        EnsureSafe(chatOpen: false);
                        // Blish HUD may still have a control (a bar, say) as the one under the cursor, and its mouse
                        // hook would keep the wheel from the game. The next mouse move works it out again.
                        GameService.Input.Mouse.UnsetActiveControl();
                        // Sent as real input; the game ignores wheel messages posted to it.
                        Mouse.RotateWheel(distance, false, x, y, true);
                    }
                    await Task.Delay(delay);
                }
            }
            finally
            {
                ParkCursor(area);
            }
            // The tooltip and hover highlight go, and the scrolled inventory is drawn. When the caller waits for it to
            // settle, a shorter wait is enough to start looking.
            await Task.Delay(settleNext ? PollMs : delay);
        }

        /// <summary>
        /// Whether the inventory around the spot looks different between two screenshots. Only the spot's own column,
        /// three slots up and down, is compared: that's inside the inventory's grid wherever the spot is, while slots to
        /// the side could be past the window's edge, where the game world moves.
        /// </summary>
        private bool Moved(Snapshot before, Snapshot after, InventorySpot spot)
        {
            var a = before.Screen;
            var b = after.Screen;
            if (a.Width != b.Width || a.Height != b.Height) return true;
            int slot = spot.Slot;
            int left = Math.Max(0, spot.X - slot * 2 / 5), right = Math.Min(a.Width, spot.X + slot * 2 / 5);
            int top = Math.Max(0, spot.Y - slot * 3), bottom = Math.Min(a.Height, spot.Y + slot * 3);
            int changed = 0, count = 0;
            for (int y = top; y < bottom; y += 2)
            {
                // Not the slot the wheel is turned over: its hover highlight fades in and out with the cursor.
                if (Math.Abs(y - spot.Y) < slot * 3 / 5) continue;
                for (int x = left; x < right; x += 2)
                {
                    int i = (y * a.Width + x) * 4;
                    int difference = Math.Abs(a.Pixels[i] - b.Pixels[i]) + Math.Abs(a.Pixels[i + 1] - b.Pixels[i + 1]) + Math.Abs(a.Pixels[i + 2] - b.Pixels[i + 2]);
                    if (difference > PixelChange) changed++;
                    count++;
                }
            }
            return count == 0 || (double)changed / count >= MovedShare;
        }

        /// <summary>
        /// The game window's own picture, which has nothing of Blish HUD in it. If Windows gives back blank pictures
        /// for it, a copy of the screen instead, with the bars hidden so they can't be mistaken for the item.
        /// </summary>
        private async Task<BgraImage> CaptureScreenAsync(IntPtr window, System.Drawing.Rectangle area)
        {
            if (_windowCaptureFailures < MaxWindowCaptureFailures)
            {
                var own = ScreenGrab.CaptureWindow(window, area);
                if (own != null)
                {
                    _windowCaptureFailures = 0;
                    return own;
                }
                if (++_windowCaptureFailures == MaxWindowCaptureFailures)
                    Logger.Info("The game window's own picture keeps coming back blank; copying the screen instead from now on.");
            }

            await HideBarsAsync();
            return ScreenGrab.Capture(area);
        }

        private async Task HideBarsAsync()
        {
            if (_barsHidden) return;
            _options.HideBars(true);
            _barsHidden = true;
            await Task.Delay(50); // Blish HUD draws a frame or two without the bars
        }

        private static int ExpectedIconSize(IntPtr window)
        {
            int index = Math.Max(0, Math.Min(IconSizes.Length - 1, (int)GameService.Gw2Mumble.UI.UISize));
            return (int)Math.Round(IconSizes[index] * Dpi.GameScale(window));
        }

        /// <summary>
        /// Saves a searched screenshot in the background, one after another: encoding a PNG takes longer than the
        /// search, and the use shouldn't wait for it. Screenshots aren't changed after they're taken.
        /// </summary>
        private void SaveScreenshot(BgraImage screen, IconMatch? match, string what, bool found)
        {
            string safeName = string.Concat(what.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string name = $"{DateTime.Now:yyyy-MM-dd HH-mm-ss-fff} {safeName} {(found ? "found" : "not found")}.png";
            string directory = _options.ScreenshotDirectory;
            lock (_savingLock)
                _saving = _saving.ContinueWith(_ =>
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        ScreenGrab.SaveWithMatch(screen, match, Path.Combine(directory, name));
                        foreach (var old in new DirectoryInfo(directory).GetFiles("*.png")
                                     .OrderByDescending(f => f.Name).Skip(MaxScreenshots))
                            old.Delete();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Couldn't save the search screenshot.");
                        Blocked.Check(ex, directory);
                    }
                }, TaskScheduler.Default);
        }

        private async Task PressInventoryKeyAsync()
        {
            var binding = _options.InventoryKey();
            if (binding == null || binding.PrimaryKey == Keys.None)
                throw new AbortException("Set Inventory Key in the settings.");
            int delay = _options.KeyDelayMs();

            EnsureAllowed();
            EnsureSafe(chatOpen: false);
            var modifiers = ModifierKeysOf(binding.ModifierKeys);
            foreach (var modifier in modifiers) Keyboard.Press(modifier, true);
            try
            {
                // GW2 reads keys once per frame, so the modifiers must be down a moment before the key.
                if (modifiers.Length > 0) await Task.Delay(delay);
                EnsureSafe(chatOpen: false);
                Keyboard.Stroke((VirtualKeyShort)binding.PrimaryKey, true);
                Step("pressed Inventory Key");
                await Task.Delay(delay);
            }
            finally
            {
                // Never leave a simulated modifier held down system-wide.
                foreach (var modifier in modifiers) Keyboard.Release(modifier, true);
            }
        }

        private static VirtualKeyShort[] ModifierKeysOf(ModifierKeys keys)
        {
            var result = new System.Collections.Generic.List<VirtualKeyShort>();
            if (keys.HasFlag(ModifierKeys.Ctrl)) result.Add(VirtualKeyShort.LCONTROL);
            if (keys.HasFlag(ModifierKeys.Alt)) result.Add(VirtualKeyShort.LMENU);
            if (keys.HasFlag(ModifierKeys.Shift)) result.Add(VirtualKeyShort.LSHIFT);
            return result.ToArray();
        }

        /// <summary>Clicks (or double-clicks) at a screen position, then parks the cursor in a corner.</summary>
        private async Task ClickAsync(int x, int y, int clicks)
        {
            int delay = Math.Min(_options.KeyDelayMs(), 100); // both clicks must land within Windows' double-click time
            SavePlayerCursor();
            bool buttonDown = false;
            try
            {
                EnsureAllowed();
                EnsureSafe(chatOpen: false);
                // sendToSystem: screen coordinates, and real input, as GW2 needs the cursor over the item.
                Mouse.SetPosition(x, y, true);
                await Task.Delay(delay); // let GW2 see the cursor over the item first
                for (int click = 0; click < clicks; click++)
                {
                    EnsureAllowed();
                    EnsureSafe(chatOpen: false);
                    Mouse.Press(MouseButton.LEFT, x, y, true);
                    buttonDown = true;
                    await Task.Delay(delay); // GW2 reads input once per frame
                    Mouse.Release(MouseButton.LEFT, x, y, true);
                    buttonDown = false;
                    await Task.Delay(delay);
                }
            }
            finally
            {
                // Never leave the left button held down; off the item, so its tooltip doesn't cover the inventory.
                if (buttonDown) Mouse.Release(MouseButton.LEFT, x, y, true);
                if (ScreenGrab.TryGetClientArea(GameService.GameIntegration.Gw2Instance.Gw2WindowHandle, out var area)) ParkCursor(area);
            }
        }

        /// <summary>Remembers where the player had the cursor, the first time a use moves it.</summary>
        private void SavePlayerCursor()
        {
            if (_playerCursor != null) return;
            var position = Mouse.GetPosition();
            _playerCursor = (position.X, position.Y);
        }

        /// <summary>
        /// Moves the cursor to the game window's top right corner while it isn't needed: away from the inventory, so
        /// there's no tooltip or hover highlight in the screenshots, and clear of the game's own corner menus.
        /// </summary>
        private static void ParkCursor(System.Drawing.Rectangle area) =>
            Mouse.SetPosition(area.Right - 1, area.Top, true);

        /// <summary>Puts the cursor back where the player had it, once the use is over.</summary>
        private void RestorePlayerCursor()
        {
            if (_playerCursor is (int x, int y)) Mouse.SetPosition(x, y, true);
            _playerCursor = null;
        }

        /// <summary>A held modifier would turn the clicks and the inventory key into other shortcuts.</summary>
        private static async Task ReleaseModifiersAsync()
        {
            if (!await WaitAsync(() => !GameService.Input.Keyboard.KeysDown.Any(Modifiers.Contains), 2000))
                throw new AbortException("Let go of Shift, Ctrl and Alt first.");
        }
    }
}
