using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Settings;
using Blish_HUD.Settings.UI.Views;
using Microsoft.Xna.Framework;

namespace Bytex.ModuleShared
{
    /// <summary>A titled, collapsible group of settings, with optional buttons below them.</summary>
    internal sealed class SettingsSection
    {
        public string Title { get; }
        public IReadOnlyList<SettingEntry> Entries { get; }
        public (string text, string tooltip, Action click)[] Buttons { get; }
        /// <summary>Optional: adds controls of the module's own after the settings, before the buttons.</summary>
        public Action<FlowPanel> BuildExtra { get; set; }

        public SettingsSection(string title, IEnumerable<SettingEntry> entries, params (string text, string tooltip, Action click)[] buttons)
        {
            Title = title;
            Entries = entries.ToList();
            Buttons = buttons;
        }
    }

    /// <summary>
    /// The usual module settings panel, except whole-number sliders also get a box showing their value.
    /// The box can be typed into; the value is applied on Enter or when the box loses focus.
    /// Sliders defined with <see cref="SliderSettings.DefineSlider"/> also get a Default button.
    /// Blish HUD's own slider view can't be extended, so every setting is laid out here.
    /// Optional buttons go below the settings. Settings can instead be laid out in titled sections.
    /// </summary>
    internal class SettingsWithValuesView : View
    {
        private const int RowHeight = 27;
        private const int LabelWidth = 200;
        private const int TrackBarWidth = 142;
        private const int ValueBoxWidth = 60;
        private const int DefaultButtonWidth = 72;

        private readonly SettingCollection _settings;
        private readonly (string text, string tooltip, Action click)[] _buttons;
        private readonly SettingsSection[] _sections;
        private readonly List<Action> _unsubscribe = new List<Action>();

        /// <summary>
        /// Optional: whole-number settings with values shown, and typeable, as names ("Every Update", "Never"),
        /// usually the slider's ends. The value box is widened to fit them.
        /// </summary>
        public Dictionary<SettingEntry<int>, Dictionary<int, string>> NamedValues { get; } = new Dictionary<SettingEntry<int>, Dictionary<int, string>>();

        /// <summary>Every setting in the collection, in order, then the buttons.</summary>
        public SettingsWithValuesView(SettingCollection settings, params (string text, string tooltip, Action click)[] buttons)
        {
            _settings = settings;
            _buttons = buttons;
        }

        public SettingsWithValuesView(params SettingsSection[] sections)
        {
            _sections = sections;
        }

        protected override void Build(Container buildPanel)
        {
            var flow = new FlowPanel
            {
                Parent = buildPanel,
                Size = buildPanel.ContentRegion.Size,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0, 5),
                OuterControlPadding = new Vector2(5, 5),
                CanScroll = true,
            };

            if (_sections == null)
            {
                BuildEntries(flow, _settings.Entries, _buttons);
                return;
            }

            foreach (var section in _sections)
            {
                var group = new FlowPanel
                {
                    Parent = flow,
                    Title = section.Title,
                    Width = flow.ContentRegion.Width - 10,
                    HeightSizingMode = SizingMode.AutoSize,
                    FlowDirection = ControlFlowDirection.SingleTopToBottom,
                    ControlPadding = new Vector2(0, 5),
                    OuterControlPadding = new Vector2(5, 5),
                    ShowBorder = true,
                    CanCollapse = true,
                };
                BuildEntries(group, section.Entries, null);
                section.BuildExtra?.Invoke(group);
                BuildButtons(group, section.Buttons);
            }
        }

        private void BuildEntries(FlowPanel flow, IEnumerable<SettingEntry> entries, (string text, string tooltip, Action click)[] buttons)
        {
            foreach (var entry in entries.Where(e => e.SessionDefined))
            {
                // Hidden sub-collections hold values the module saves itself.
                if (entry is SettingEntry<SettingCollection> sub && !sub.Value.RenderInUi) continue;

                // Each row sits in its own holder so it can be rebuilt in place when the setting is disabled or enabled.
                var holder = new Panel
                {
                    Parent = flow,
                    WidthSizingMode = SizingMode.Fill,
                    HeightSizingMode = SizingMode.AutoSize,
                };
                // The row's own handlers, let go of when it's rebuilt, so they don't pile up while the view is open.
                Action unsubscribeRow = BuildRow(holder, entry, flow.ContentRegion.Width);

                void OnDisabledChanged(SettingEntry changed)
                {
                    if (changed != entry) return;
                    unsubscribeRow?.Invoke();
                    foreach (var child in holder.Children.ToList()) child.Dispose();
                    unsubscribeRow = BuildRow(holder, entry, flow.ContentRegion.Width);
                }

                SettingsUsedWhen.DisabledChanged += OnDisabledChanged;
                _unsubscribe.Add(() =>
                {
                    SettingsUsedWhen.DisabledChanged -= OnDisabledChanged;
                    unsubscribeRow?.Invoke();
                    unsubscribeRow = null;
                });
            }

            BuildButtons(flow, buttons);
        }

        /// <returns>Removes the row's handlers from the setting; null if it has none.</returns>
        private Action BuildRow(Container holder, SettingEntry entry, int width)
        {
            if (entry is SettingEntry<int> intEntry) return BuildIntRow(holder, intEntry);

            var view = SettingView.FromType(entry, width);
            if (view == null) return null;
            var container = new ViewContainer
            {
                Parent = holder,
                WidthSizingMode = SizingMode.Fill,
                HeightSizingMode = SizingMode.AutoSize,
            };
            container.Show(view);
            return null; // Blish HUD's own view lets go of the setting when it unloads
        }

        private static void BuildButtons(FlowPanel flow, (string text, string tooltip, Action click)[] buttons)
        {
            if (buttons == null) return;
            foreach (var (text, tooltip, click) in buttons)
            {
                var button = new StandardButton { Parent = flow, Text = text, BasicTooltipText = tooltip, Width = 160 };
                button.Click += (_, __) => click();
            }
        }

        private Action BuildIntRow(Container parent, SettingEntry<int> entry)
        {
            var range = entry.GetComplianceRequisite().OfType<IntRangeRangeComplianceRequisite>().FirstOrDefault();
            bool hasRange = range.MaxValue > range.MinValue;
            int min = hasRange ? range.MinValue : 0;
            int max = hasRange ? range.MaxValue : 100;

            var named = NamedValues.TryGetValue(entry, out var names) ? names : new Dictionary<int, string>();
            int extraBoxWidth = named.Count > 0 ? 40 : 0; // taken from the slider, so the row stays the same width
            string Text(int value) => named.TryGetValue(value, out string name) ? name : value.ToString();

            var row = new Panel { Parent = parent, Size = new Point(LabelWidth + TrackBarWidth + ValueBoxWidth + DefaultButtonWidth + 36, RowHeight) };
            string tooltip = entry.Description;
            // Named ends are left out of the number range: "Every Update, a value from 7 to 365, or Never".
            int low = named.ContainsKey(min) ? min + 1 : min, high = named.ContainsKey(max) ? max - 1 : max;
            var choices = new List<string>();
            if (named.TryGetValue(min, out string minName)) choices.Add(minName);
            choices.Add($"a value from {low} to {high}");
            choices.AddRange(named.Where(n => n.Key != min && n.Key != max).Select(n => $"{n.Value} ({n.Key})"));
            if (named.TryGetValue(max, out string maxName)) choices.Add(maxName);
            bool enabled = !SettingsUsedWhen.IsDisabled(entry);
            string typeHint = choices.Count == 1 ? $"Type {choices[0]}, then press Enter."
                : $"Type {string.Join(", ", choices.Take(choices.Count - 1))} or {choices.Last()}, then press Enter.";

            _ = new Label
            {
                Parent = row,
                Text = entry.DisplayName,
                BasicTooltipText = tooltip,
                Location = new Point(5, 0),
                Size = new Point(LabelWidth, RowHeight),
                VerticalAlignment = VerticalAlignment.Middle,
                TextColor = enabled ? Color.White : Control.StandardColors.DisabledText,
            };

            var trackBar = new TrackBar
            {
                Parent = row,
                MinValue = min,
                MaxValue = max,
                Value = Clamp(entry.Value, min, max),
                BasicTooltipText = tooltip,
                Location = new Point(LabelWidth + 10, (RowHeight - 16) / 2),
                Width = TrackBarWidth - extraBoxWidth,
                Enabled = enabled,
            };

            var valueBox = new TextBox
            {
                Parent = row,
                Text = Text(entry.Value),
                BasicTooltipText = $"{tooltip}\n\n{typeHint}",
                Location = new Point(trackBar.Right + 10, 0),
                Size = new Point(ValueBoxWidth + extraBoxWidth, RowHeight),
                HorizontalAlignment = HorizontalAlignment.Center,
                Enabled = enabled,
            };

            int? defaultValue = SliderSettings.DefaultOf(entry);
            StandardButton defaultButton = null;
            if (defaultValue != null)
            {
                defaultButton = new StandardButton
                {
                    Parent = row,
                    Text = "Default",
                    BasicTooltipText = $"Set it back to the default, {Text(defaultValue.Value)}.",
                    Location = new Point(valueBox.Right + 6, 0),
                    Size = new Point(DefaultButtonWidth, RowHeight - 1),
                    Enabled = enabled && entry.Value != defaultValue.Value,
                };
                defaultButton.Click += (_, __) => entry.Value = defaultValue.Value;
            }

            void ApplyTyped()
            {
                string text = valueBox.Text.Trim();
                var match = named.FirstOrDefault(n => string.Equals(text, n.Value, StringComparison.OrdinalIgnoreCase));
                if (match.Value != null) entry.Value = match.Key;
                else if (int.TryParse(text, out int typed)) entry.Value = Clamp(typed, min, max);
                valueBox.Text = Text(entry.Value); // also puts back the old value after invalid input
            }

            trackBar.ValueChanged += (_, e) =>
            {
                int value = (int)Math.Round(e.Value);
                if (value != entry.Value) entry.Value = value;
            };
            valueBox.EnterPressed += (_, __) => ApplyTyped();
            valueBox.InputFocusChanged += (_, e) => { if (!e.Value) ApplyTyped(); };

            void OnSettingChanged(object sender, ValueChangedEventArgs<int> e)
            {
                if ((int)Math.Round(trackBar.Value) != e.NewValue) trackBar.Value = Clamp(e.NewValue, min, max);
                if (!valueBox.Focused) valueBox.Text = Text(e.NewValue);
                if (defaultButton != null) defaultButton.Enabled = enabled && e.NewValue != defaultValue.Value;
            }

            entry.SettingChanged += OnSettingChanged;
            return () => entry.SettingChanged -= OnSettingChanged;
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        protected override void Unload()
        {
            // Settings outlive this view; don't let them keep its controls alive.
            foreach (var unsubscribe in _unsubscribe) unsubscribe();
            _unsubscribe.Clear();
        }
    }
}
