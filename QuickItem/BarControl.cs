using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended.BitmapFonts;

namespace Bytex.QuickItem
{
    /// <summary>What a bar needs from the module.</summary>
    internal interface IBarHost
    {
        bool Editing { get; }
        bool DoubleClickToUse { get; }
        bool ShowHotkeys { get; }
        /// <summary>The item being used right now, drawn dimmed.</summary>
        BarItem Busy { get; }
        AsyncTexture2D IconOf(BarItem item);
        int NumberOf(Bar bar);
        string CurrentCharacter { get; }
        void Use(BarItem item);
        /// <summary>Opens the item picker; <paramref name="replacing"/> is null to add a new item.</summary>
        void ChooseItem(Bar bar, BarItem replacing);
        void SetHotkey(BarItem item);
        void ClearHotkey(BarItem item);
        void Save();
        void DeleteBar(Bar bar);
        void SetEditing(bool editing);
        /// <summary>The layout shown now.</summary>
        Layout CurrentLayout { get; }
        IReadOnlyList<Layout> Layouts { get; }
        /// <summary>Moves the bar to a layout; null puts it in every layout.</summary>
        void SetBarLayout(Bar bar, Layout layout);
        /// <summary>Puts a copy of the bar, with its items and hotkeys, in another layout.</summary>
        void CopyBarTo(Bar bar, Layout layout);
        /// <summary>A menu entry for each layout, to switch to it.</summary>
        IEnumerable<ContextMenuStripItem> LayoutMenuItems();
    }

    /// <summary>
    /// One bar on screen. Click (or double-click) an item to use it. While editing, drag the header to move the bar,
    /// drag items to reorder them, click + to add one, and right-click for the rest.
    /// </summary>
    internal sealed class BarControl : Control
    {
        private const int Spacing = 4;
        private const int HeaderHeight = 20;
        private const int DragThreshold = 5;

        private static readonly Dictionary<string, Color> RarityColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["Junk"] = new Color(0xAA, 0xAA, 0xAA),
            ["Basic"] = new Color(0xFF, 0xFF, 0xFF),
            ["Fine"] = new Color(0x62, 0xA4, 0xDA),
            ["Masterwork"] = new Color(0x1A, 0x93, 0x06),
            ["Rare"] = new Color(0xFC, 0xD0, 0x0B),
            ["Exotic"] = new Color(0xFF, 0xA4, 0x05),
            ["Ascended"] = new Color(0xFB, 0x3E, 0x8D),
            ["Legendary"] = new Color(0x97, 0x4E, 0xFF),
        };

        /// <summary>The colour the game uses for a rarity's name, or <paramref name="fallback"/>.</summary>
        public static Color RarityColor(string rarity, Color fallback) =>
            rarity != null && RarityColors.TryGetValue(rarity, out var color) ? color : fallback;

        private readonly IBarHost _host;
        private readonly ContextMenuStrip _menu;
        public Bar Bar { get; }

        // Dragging: the bar by its header, or an item to a new place.
        private Point _pressAt;
        private Point _barAtPress;
        private bool _pressedHeader;
        private int _pressedSlot = -1;
        private bool _dragging;
        private int _menuSlot = -1;

        public BarControl(Bar bar, IBarHost host)
        {
            Bar = bar;
            _host = host;
            _menu = new ContextMenuStrip(MenuItems);
            Layout();
        }

        private bool Editing => _host.Editing;
        private int SlotCount => Bar.Items.Count + (Editing ? 1 : 0); // the + slot while editing
        private int Columns => Math.Max(1, Math.Min(Bar.Columns, SlotCount));
        private int Rows => (SlotCount + Columns - 1) / Columns;
        private int HeaderTop => Editing ? HeaderHeight : 0;

        /// <summary>Puts the control where the bar is saved, sized to its items. Call after any change to the bar.</summary>
        public void Layout()
        {
            int pitch = Bar.IconSize + Spacing;
            int width = Math.Max(Columns * pitch - Spacing, Editing ? 150 : 0);
            Location = new Point(Bar.X, Bar.Y - HeaderTop);
            Size = new Point(width, HeaderTop + Math.Max(0, Rows * pitch - Spacing));
        }

        private Rectangle SlotBounds(int slot)
        {
            int pitch = Bar.IconSize + Spacing;
            return new Rectangle(slot % Columns * pitch, HeaderTop + slot / Columns * pitch, Bar.IconSize, Bar.IconSize);
        }

        /// <summary>The slot under a point relative to the control, or -1; the + slot is the last one.</summary>
        private int SlotAt(Point point)
        {
            for (int slot = 0; slot < SlotCount; slot++)
                if (SlotBounds(slot).Contains(point)) return slot;
            return -1;
        }

        /// <summary>The nearest slot for a dragged item, among the item slots.</summary>
        private int DropSlotAt(Point point)
        {
            int pitch = Bar.IconSize + Spacing;
            int column = Math.Max(0, Math.Min(Columns - 1, point.X / pitch));
            int row = Math.Max(0, (point.Y - HeaderTop) / pitch);
            return Math.Max(0, Math.Min(Bar.Items.Count - 1, row * Columns + column));
        }

        private BarItem ItemAt(int slot) => slot >= 0 && slot < Bar.Items.Count ? Bar.Items[slot] : null;

        protected override void OnMouseMoved(MouseEventArgs e)
        {
            base.OnMouseMoved(e);
            BasicTooltipText = TooltipFor(SlotAt(RelativeMousePosition));
        }

        private string TooltipFor(int slot)
        {
            var item = ItemAt(slot);
            if (Editing)
            {
                if (slot == Bar.Items.Count) return "Add an item.";
                if (item != null) return $"{item.Name}\nDrag to move it, click to change it, right-click for more.";
                return "Drag to move the bar. Right-click for its options.";
            }
            if (item == null) return null;
            string use = _host.DoubleClickToUse ? "Double-click to use." : "Click to use.";
            string hotkey = item.Key != 0 ? $"\nHotkey: {HotkeyText(item, full: true)}" : "";
            return $"{item.Name}\n{use}{hotkey}";
        }

        protected override void OnLeftMouseButtonPressed(MouseEventArgs e)
        {
            base.OnLeftMouseButtonPressed(e);
            if (!Editing) return;
            var at = RelativeMousePosition;
            _pressAt = GameService.Input.Mouse.Position;
            _barAtPress = new Point(Bar.X, Bar.Y);
            _pressedSlot = ItemAt(SlotAt(at)) != null ? SlotAt(at) : -1;
            _pressedHeader = _pressedSlot < 0 && SlotAt(at) < 0;
            _dragging = false;
            GameService.Input.Mouse.LeftMouseButtonReleased -= Mouse_LeftMouseButtonReleased;
            GameService.Input.Mouse.LeftMouseButtonReleased += Mouse_LeftMouseButtonReleased;
        }

        /// <summary>Follows the mouse while dragging; called every frame by the module.</summary>
        public void UpdateDrag()
        {
            if (!_pressedHeader && _pressedSlot < 0) return;
            var moved = GameService.Input.Mouse.Position - _pressAt;
            if (!_dragging && Math.Abs(moved.X) + Math.Abs(moved.Y) < DragThreshold) return;
            _dragging = true;

            if (_pressedHeader)
            {
                var screen = GameService.Graphics.SpriteScreen.Size;
                Bar.X = Math.Max(0, Math.Min(screen.X - Width, _barAtPress.X + moved.X));
                Bar.Y = Math.Max(HeaderTop, Math.Min(screen.Y - Height + HeaderTop, _barAtPress.Y + moved.Y));
                Layout();
            }
        }

        private void Mouse_LeftMouseButtonReleased(object sender, MouseEventArgs e)
        {
            GameService.Input.Mouse.LeftMouseButtonReleased -= Mouse_LeftMouseButtonReleased;
            bool changed = false;
            if (_dragging && _pressedHeader) changed = true;
            else if (_dragging && _pressedSlot >= 0)
            {
                int target = DropSlotAt(RelativeMousePosition);
                if (target != _pressedSlot)
                {
                    var item = Bar.Items[_pressedSlot];
                    Bar.Items.RemoveAt(_pressedSlot);
                    Bar.Items.Insert(target, item);
                    changed = true;
                }
            }

            if (_dragging) _dragEndedUtc = DateTime.UtcNow;
            _pressedHeader = false;
            _pressedSlot = -1;
            _dragging = false;
            if (changed) _host.Save();
        }

        // A drag ends with the button released over the control, which Blish HUD may also report as a click, before or
        // after the release above; neither order may count as one.
        private DateTime _dragEndedUtc;
        private bool JustDragged => _dragging || DateTime.UtcNow - _dragEndedUtc < TimeSpan.FromMilliseconds(150);

        protected override void OnClick(MouseEventArgs e)
        {
            base.OnClick(e);
            if (JustDragged) return;

            int slot = SlotAt(RelativeMousePosition);
            if (Editing)
            {
                if (slot == Bar.Items.Count) _host.ChooseItem(Bar, null);
                else if (ItemAt(slot) is BarItem toChange) _host.ChooseItem(Bar, toChange);
                return;
            }

            var item = ItemAt(slot);
            if (item == null) return;
            // A double-click also arrives as a single click first; only one of the two counts.
            if (e.IsDoubleClick == _host.DoubleClickToUse) _host.Use(item);
        }

        protected override void OnRightMouseButtonReleased(MouseEventArgs e)
        {
            base.OnRightMouseButtonReleased(e);
            _menuSlot = SlotAt(RelativeMousePosition);
            _menu.Show(GameService.Input.Mouse.Position);
        }

        private IEnumerable<ContextMenuStripItem> MenuItems()
        {
            ContextMenuStripItem Add(string text, Action action, string tooltip)
            {
                var menuItem = new ContextMenuStripItem(text) { BasicTooltipText = tooltip };
                menuItem.Click += (_, __) => action();
                return menuItem;
            }

            var item = ItemAt(_menuSlot);
            if (item != null)
            {
                if (!Editing) yield return Add("Use", () => _host.Use(item), "Use the item now.");
                yield return Add("Change Item", () => _host.ChooseItem(Bar, item), "Choose another item for this slot. Its hotkey stays.");
                yield return Add("Set Hotkey", () => _host.SetHotkey(item), "Choose a key that uses the item, even while the bars are hidden.");
                if (item.Key != 0) yield return Add("Clear Hotkey", () => _host.ClearHotkey(item), "Remove the item's hotkey.");
                yield return Add("Remove Item", () => { Bar.Items.Remove(item); _host.Save(); }, "Take the item off the bar. Nothing happens to it in game.");
            }

            var others = _host.Layouts.Where(l => l != _host.CurrentLayout).ToList();
            ContextMenuStripItem LayoutsSubmenu(string text, string tooltip, Func<IEnumerable<ContextMenuStripItem>> entries) =>
                new ContextMenuStripItem(text) { BasicTooltipText = tooltip, Submenu = new ContextMenuStrip(entries) };

            if (!Editing)
            {
                if (_host.Layouts.Count > 1)
                    yield return LayoutsSubmenu("Switch Layout", "Show another layout's bars.", _host.LayoutMenuItems);
                yield return Add("Edit Bars", () => _host.SetEditing(true), "Move, arrange and add items to the bars.");
                yield break;
            }

            yield return Add("Add Item", () => _host.ChooseItem(Bar, null), "Add an item at the end of the bar.");
            if (Bar.Columns < Bar.MaxColumns && Bar.Columns < Math.Max(1, Bar.Items.Count))
                yield return Add("More Columns", () => { Bar.Columns++; _host.Save(); }, "Make the bar one item wider.");
            // Columns beyond the number of items aren't shown, so count from what's on screen.
            int shownColumns = Math.Min(Bar.Columns, Math.Max(1, Bar.Items.Count));
            if (shownColumns > 1)
                yield return Add("Fewer Columns", () => { Bar.Columns = shownColumns - 1; _host.Save(); }, "Make the bar one item narrower.");
            if (Bar.IconSize < Bar.MaxIconSize)
                yield return Add("Larger Icons", () => { Bar.IconSize += Bar.IconSizeStep; _host.Save(); }, "Make the bar's icons bigger.");
            if (Bar.IconSize > Bar.MinIconSize)
                yield return Add("Smaller Icons", () => { Bar.IconSize -= Bar.IconSizeStep; _host.Save(); }, "Make the bar's icons smaller.");

            var current = _host.CurrentLayout;
            if (Bar.Layout != null)
                yield return Add("Show in All Layouts", () => _host.SetBarLayout(Bar, null), $"Show the bar whichever layout is in use. It's only in {current.Name} now.");
            else
                yield return Add($"Show in {current.Name} Only", () => _host.SetBarLayout(Bar, current), "Show the bar only while this layout is in use. It's in every layout now.");
            if (others.Count > 0)
            {
                if (Bar.Layout != null)
                    yield return LayoutsSubmenu("Move to Layout", "Take the bar out of this layout and put it in another.",
                        () => others.Select(l => Add(l.Name, () => _host.SetBarLayout(Bar, l), $"Move the bar to {l.Name}.")));
                yield return LayoutsSubmenu("Copy to Layout", "Put a copy of the bar, with its items and hotkeys, in another layout.",
                    () => others.Select(l => Add(l.Name, () => _host.CopyBarTo(Bar, l), $"Copy the bar to {l.Name}.")));
            }

            string character = _host.CurrentCharacter;
            if (Bar.Character != null)
                yield return Add("Show on Every Character", () => { Bar.Character = null; _host.Save(); }, $"Show the bar whichever character you play. It's shown only on {Bar.Character} now.");
            else if (!string.IsNullOrEmpty(character))
                yield return Add("Show on This Character Only", () => { Bar.Character = character; _host.Save(); }, $"Show the bar only while you play {character}.");

            string delete = Bar.Items.Count == 0 ? "Delete Bar" : $"Delete Bar and Its {Bar.Items.Count} {(Bar.Items.Count == 1 ? "Item" : "Items")}";
            yield return Add(delete, () => _host.DeleteBar(Bar), "Remove the bar from the screen. Nothing happens to the items in game.");
            yield return Add("Done Editing", () => _host.SetEditing(false), "Lock the bars, so clicking an item uses it.");
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            var pixel = ContentService.Textures.Pixel;
            if (Editing)
            {
                spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(0, 0, Width, Height), Color.Black * 0.45f);
                spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(0, 0, Width, HeaderHeight - 2), Color.Black * 0.6f);
                string title = $"Bar {_host.NumberOf(Bar)}" + (Bar.Layout == null ? " · All Layouts" : "")
                               + (Bar.Character != null ? $" · {Bar.Character}" : "");
                spriteBatch.DrawStringOnCtrl(this, title, GameService.Content.DefaultFont12,
                    new Rectangle(4, 0, Width - 8, HeaderHeight - 2), Color.White, false, HorizontalAlignment.Left, VerticalAlignment.Middle);
                DrawFrame(spriteBatch, new Rectangle(0, 0, Width, Height), new Color(0xC9, 0xB8, 0x7F), 1);
            }

            var mouse = RelativeMousePosition;
            int hovered = MouseOver ? SlotAt(mouse) : -1;
            for (int slot = 0; slot < SlotCount; slot++)
            {
                var rect = SlotBounds(slot);
                var item = ItemAt(slot);
                if (item == null)
                {
                    // The + slot.
                    spriteBatch.DrawOnCtrl(this, pixel, rect, Color.Black * (slot == hovered ? 0.8f : 0.6f));
                    DrawFrame(spriteBatch, rect, Color.White * 0.5f, 1);
                    spriteBatch.DrawStringOnCtrl(this, "+", GameService.Content.DefaultFont32, rect, Color.White * 0.8f,
                        false, HorizontalAlignment.Center, VerticalAlignment.Middle);
                    continue;
                }

                // Left empty where the dragged item came from; the item follows the mouse instead.
                bool lifted = _dragging && slot == _pressedSlot;
                if (lifted)
                {
                    DrawFrame(spriteBatch, rect, Color.White * 0.4f, 1);
                    continue;
                }

                DrawItem(spriteBatch, item, rect, slot == hovered && !_dragging);
            }

            if (_dragging && ItemAt(_pressedSlot) is BarItem dragged)
            {
                var drop = SlotBounds(DropSlotAt(mouse));
                DrawFrame(spriteBatch, drop, Color.White, 2);
                var size = Bar.IconSize;
                DrawItem(spriteBatch, dragged, new Rectangle(mouse.X - size / 2, mouse.Y - size / 2, size, size), false);
            }
        }

        private void DrawItem(SpriteBatch spriteBatch, BarItem item, Rectangle rect, bool hovered)
        {
            var pixel = ContentService.Textures.Pixel;
            bool busy = _host.Busy == item;
            spriteBatch.DrawOnCtrl(this, pixel, rect, Color.Black * 0.6f);
            var icon = _host.IconOf(item);
            if (icon != null) spriteBatch.DrawOnCtrl(this, icon, rect, busy ? Color.Gray : Color.White);
            var rarity = RarityColor(item.Rarity, Color.Black);
            DrawFrame(spriteBatch, rect, rarity * 0.9f, Math.Max(1, Bar.IconSize / 24));
            if (hovered) spriteBatch.DrawOnCtrl(this, pixel, rect, Color.White * 0.15f);

            if (_host.ShowHotkeys && item.Key != 0)
            {
                var font = Bar.IconSize >= 40 ? GameService.Content.DefaultFont14 : GameService.Content.DefaultFont12;
                var textRect = new Rectangle(rect.X + 3, rect.Y + 1, rect.Width - 5, rect.Height - 2);
                spriteBatch.DrawStringOnCtrl(this, HotkeyText(item, full: false), font, textRect, Color.White,
                    false, true, 1, HorizontalAlignment.Left, VerticalAlignment.Bottom);
            }
        }

        private void DrawFrame(SpriteBatch spriteBatch, Rectangle rect, Color color, int thickness)
        {
            var pixel = ContentService.Textures.Pixel;
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
        }

        /// <summary>The hotkey as text: short for the icon ("C+1"), or <paramref name="full"/> for the tooltip ("Ctrl + 1").</summary>
        public static string HotkeyText(BarItem item, bool full) => HotkeyText(item.Modifiers, item.Key, full);

        /// <summary>A hotkey (Keys and ModifierKeys as saved) as text, as <see cref="HotkeyText(BarItem, bool)"/>.</summary>
        public static string HotkeyText(int modifierKeys, int key, bool full)
        {
            var modifiers = (ModifierKeys)modifierKeys;
            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Ctrl)) parts.Add(full ? "Ctrl" : "C");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add(full ? "Alt" : "A");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add(full ? "Shift" : "S");
            parts.Add(KeyText((Keys)key));
            return string.Join(full ? " + " : "+", parts);
        }

        private static string KeyText(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)(key - Keys.D0)).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num" + (int)(key - Keys.NumPad0);
            switch (key)
            {
                case Keys.OemTilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.OemPlus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemComma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemPipe: return "\\";
                default: return key.ToString();
            }
        }

        protected override void DisposeControl()
        {
            GameService.Input.Mouse.LeftMouseButtonReleased -= Mouse_LeftMouseButtonReleased;
            _menu.Dispose();
            base.DisposeControl();
        }
    }
}
