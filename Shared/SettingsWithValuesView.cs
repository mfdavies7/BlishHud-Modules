using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Settings;
using Blish_HUD.Settings.UI.Views;
using Microsoft.Xna.Framework;

namespace ModuleShared
{
    /// <summary>
    /// The usual module settings panel, except whole-number sliders also get a box showing their value.
    /// The box can be typed into; the value is applied on Enter or when the box loses focus.
    /// Blish HUD's own slider view can't be extended, so every setting is laid out here.
    /// </summary>
    internal class SettingsWithValuesView : View
    {
        private const int RowHeight = 27;
        private const int LabelWidth = 200;
        private const int TrackBarWidth = 220;
        private const int ValueBoxWidth = 60;

        private readonly SettingCollection _settings;
        private readonly List<Action> _unsubscribe = new List<Action>();

        public SettingsWithValuesView(SettingCollection settings)
        {
            _settings = settings;
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

            foreach (var entry in _settings.Entries.Where(e => e.SessionDefined))
            {
                if (entry is SettingEntry<int> intEntry)
                {
                    BuildIntRow(flow, intEntry);
                    continue;
                }

                // Hidden sub-collections hold values the module saves itself.
                if (entry is SettingEntry<SettingCollection> sub && !sub.Value.RenderInUi) continue;

                var view = SettingView.FromType(entry, flow.ContentRegion.Width);
                if (view == null) continue;
                var container = new ViewContainer
                {
                    Parent = flow,
                    WidthSizingMode = SizingMode.Fill,
                    HeightSizingMode = SizingMode.AutoSize,
                };
                container.Show(view);
            }
        }

        private void BuildIntRow(Container parent, SettingEntry<int> entry)
        {
            var range = entry.GetComplianceRequisite().OfType<IntRangeRangeComplianceRequisite>().FirstOrDefault();
            bool hasRange = range.MaxValue > range.MinValue;
            int min = hasRange ? range.MinValue : 0;
            int max = hasRange ? range.MaxValue : 100;

            var row = new Panel { Parent = parent, Size = new Point(LabelWidth + TrackBarWidth + ValueBoxWidth + 30, RowHeight) };
            string tooltip = entry.Description;

            _ = new Label
            {
                Parent = row,
                Text = entry.DisplayName,
                BasicTooltipText = tooltip,
                Location = new Point(5, 0),
                Size = new Point(LabelWidth, RowHeight),
                VerticalAlignment = VerticalAlignment.Middle,
            };

            var trackBar = new TrackBar
            {
                Parent = row,
                MinValue = min,
                MaxValue = max,
                Value = Clamp(entry.Value, min, max),
                BasicTooltipText = tooltip,
                Location = new Point(LabelWidth + 10, (RowHeight - 16) / 2),
                Width = TrackBarWidth,
            };

            var valueBox = new TextBox
            {
                Parent = row,
                Text = entry.Value.ToString(),
                BasicTooltipText = $"{tooltip}\n\nType a value from {min} to {max}, then press Enter.",
                Location = new Point(trackBar.Right + 10, 0),
                Size = new Point(ValueBoxWidth, RowHeight),
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            void ApplyTyped()
            {
                if (int.TryParse(valueBox.Text.Trim(), out int typed)) entry.Value = Clamp(typed, min, max);
                valueBox.Text = entry.Value.ToString(); // also puts back the old value after invalid input
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
                if (!valueBox.Focused) valueBox.Text = e.NewValue.ToString();
            }

            entry.SettingChanged += OnSettingChanged;
            _unsubscribe.Add(() => entry.SettingChanged -= OnSettingChanged);
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
