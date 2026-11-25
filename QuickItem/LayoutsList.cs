using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;

namespace Bytex.QuickItem
{
    /// <summary>What the layout list needs from the module.</summary>
    internal interface ILayoutHost
    {
        IReadOnlyList<Layout> Layouts { get; }
        /// <summary>The layout shown now.</summary>
        Layout CurrentLayout { get; }
        /// <summary>The character being played; null or empty at character select.</summary>
        string CurrentCharacter { get; }
        /// <summary>The character's profession ("Necromancer"); null when unknown.</summary>
        string CurrentProfession { get; }
        /// <summary>How many bars are in the layout itself (not counting the bars shown in every layout).</summary>
        int BarCount(Layout layout);
        void SwitchLayout(Layout layout);
        void RenameLayout(Layout layout, string name);
        void SetLayoutHotkey(Layout layout);
        void ClearLayoutHotkey(Layout layout);
        void SetCharacterRule(Layout layout, bool on);
        void SetProfessionRule(Layout layout, bool on);
        void DuplicateLayout(Layout layout);
        void DeleteLayout(Layout layout);
        /// <summary>Raised when layouts are added, removed, switched or their rules change; not on renaming.</summary>
        event Action LayoutsChanged;
    }

    /// <summary>
    /// The layouts in the settings window, each with its name to edit, its hotkey, when it's used automatically, and
    /// buttons to switch to, duplicate or delete it. A name is saved on Enter or when the box loses focus.
    /// </summary>
    internal sealed class LayoutsList
    {
        private const int ButtonWidth = 100;
        private const int LineHeight = 26;

        private readonly ILayoutHost _host;
        private readonly FlowPanel _list;

        private LayoutsList(Container parent, ILayoutHost host)
        {
            _host = host;
            _list = new FlowPanel
            {
                Parent = parent,
                Width = parent.ContentRegion.Width - 10,
                HeightSizingMode = SizingMode.AutoSize,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0, 4),
            };
            Fill();
            _host.LayoutsChanged += Fill;
            _list.Disposed += (_, __) => _host.LayoutsChanged -= Fill;
        }

        public static void Build(Container parent, ILayoutHost host) => _ = new LayoutsList(parent, host);

        private void Fill()
        {
            foreach (var child in _list.Children.ToArray()) child.Dispose();
            foreach (var layout in _host.Layouts) AddRow(layout, _list.Width);
        }

        private void AddRow(Layout layout, int width)
        {
            bool current = layout == _host.CurrentLayout;
            var row = new Panel { Parent = _list, Width = width, ShowTint = true };
            int right = width - 5;

            // Name, and switch to or delete it.
            var name = new TextBox
            {
                Parent = row,
                Location = new Point(5, 4),
                Size = new Point(width - ButtonWidth * 2 - 20, LineHeight),
                Text = layout.Name,
                BasicTooltipText = "The layout's name. Press Enter to save.",
            };
            void ApplyName()
            {
                string text = name.Text.Trim();
                if (text.Length == 0) name.Text = layout.Name; // a layout must have a name
                else if (text != layout.Name) _host.RenameLayout(layout, text);
            }
            name.EnterPressed += (_, __) => ApplyName();
            name.InputFocusChanged += (_, e) => { if (!e.Value) ApplyName(); };

            var use = new StandardButton
            {
                Parent = row,
                Location = new Point(right - ButtonWidth * 2 - 5, 4),
                Size = new Point(ButtonWidth, LineHeight),
                Text = current ? "In Use" : "Switch To",
                Enabled = !current,
                BasicTooltipText = current ? "These are the bars on screen now." : "Show this layout's bars instead.",
            };
            use.Click += (_, __) => _host.SwitchLayout(layout);

            int bars = _host.BarCount(layout);
            bool onlyOne = _host.Layouts.Count <= 1;
            var delete = new StandardButton
            {
                Parent = row,
                Location = new Point(right - ButtonWidth, 4),
                Size = new Point(ButtonWidth, LineHeight),
                Text = "Delete",
                Enabled = !onlyOne,
                BasicTooltipText = onlyOne ? "There must be at least one layout." : "Delete the layout and its bars. Bars shown in every layout stay.",
            };
            bool armed = false;
            delete.Click += (_, __) =>
            {
                // Bars are lost with it, so the first click only asks.
                if (armed || bars == 0)
                {
                    _host.DeleteLayout(layout);
                    return;
                }
                armed = true;
                delete.Text = "Confirm";
                delete.BasicTooltipText = $"Click again to delete {layout.Name} and its {bars} {(bars == 1 ? "bar" : "bars")}.";
            };

            // Hotkey, and duplicate.
            int y = 4 + LineHeight + 4;
            string keyText = layout.Key != 0 ? BarControl.HotkeyText(layout.Modifiers, layout.Key, full: true) : "None";
            var hotkey = new StandardButton
            {
                Parent = row,
                Location = new Point(5, y),
                Size = new Point(200, LineHeight),
                Text = $"Hotkey: {keyText}",
                BasicTooltipText = "Choose a key that switches to this layout. Kept from the game, so pick one you don't need for anything else.",
            };
            hotkey.Click += (_, __) => _host.SetLayoutHotkey(layout);
            if (layout.Key != 0)
            {
                var clear = new StandardButton
                {
                    Parent = row,
                    Location = new Point(210, y),
                    Size = new Point(70, LineHeight),
                    Text = "Clear",
                    BasicTooltipText = "Remove the layout's hotkey.",
                };
                clear.Click += (_, __) => _host.ClearLayoutHotkey(layout);
            }
            var duplicate = new StandardButton
            {
                Parent = row,
                Location = new Point(right - ButtonWidth, y),
                Size = new Point(ButtonWidth, LineHeight),
                Text = "Duplicate",
                BasicTooltipText = "Make a copy of the layout and its bars, and switch to it.",
            };
            duplicate.Click += (_, __) => _host.DuplicateLayout(layout);

            // When it's used by itself: on this character, or as this profession.
            y += LineHeight + 6;
            string character = _host.CurrentCharacter;
            string profession = _host.CurrentProfession;
            if (!string.IsNullOrEmpty(character))
            {
                var onCharacter = new Checkbox
                {
                    Parent = row,
                    Location = new Point(5, y),
                    Text = $"Use on {character}",
                    Checked = layout.Characters.Contains(character),
                    BasicTooltipText = $"Switch to this layout whenever you play {character}. A character has one layout; this takes precedence over a profession's.",
                };
                onCharacter.CheckedChanged += (_, e) => _host.SetCharacterRule(layout, e.Checked);
                if (!string.IsNullOrEmpty(profession))
                {
                    var asProfession = new Checkbox
                    {
                        Parent = row,
                        Location = new Point(width / 2, y),
                        Text = $"Use as Any {profession}",
                        Checked = layout.Professions.Contains(profession),
                        BasicTooltipText = $"Switch to this layout whenever you play a {profession}, unless that character has a layout of its own.",
                    };
                    asProfession.CheckedChanged += (_, e) => _host.SetProfessionRule(layout, e.Checked);
                }
                y += 24;
            }

            var summary = new Label
            {
                Parent = row,
                Location = new Point(5, y),
                Width = width - 10,
                AutoSizeHeight = true,
                WrapText = true,
                TextColor = Color.LightGray,
                Text = Summary(layout, bars, string.IsNullOrEmpty(character)),
            };
            row.Height = summary.Bottom + 6;
        }

        private static string Summary(Layout layout, int bars, bool atCharacterSelect)
        {
            var parts = new List<string> { bars == 0 ? "No bars of its own yet." : $"{bars} {(bars == 1 ? "bar" : "bars")} of its own." };
            var rules = new List<string>();
            if (layout.Characters.Count > 0) rules.Add("on " + JoinNames(layout.Characters));
            if (layout.Professions.Count > 0) rules.Add("as any " + JoinNames(layout.Professions));
            parts.Add(rules.Count > 0 ? $"Used by itself {string.Join(", and ", rules)}." : "Used when you switch to it.");
            if (atCharacterSelect) parts.Add("Log in to a character to choose when it's used.");
            return string.Join(" ", parts);
        }

        private static string JoinNames(List<string> names) =>
            names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names.Last();
    }
}
