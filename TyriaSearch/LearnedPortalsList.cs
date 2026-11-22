using System;
using System.Linq;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;

namespace mfdavies7.TyriaSearch
{
    /// <summary>
    /// The learned portals in the settings window, each with its destination and kind to edit and a Delete button.
    /// Edits are saved as they're made: a destination on Enter or when the box loses focus, a kind when it's picked.
    /// </summary>
    internal sealed class LearnedPortalsList
    {
        private const int RowHeight = 62;
        private const int DeleteWidth = 80;
        private const int KindWidth = 160;

        private readonly PortalLearner _learner;
        private readonly FlowPanel _list;

        private LearnedPortalsList(Container parent, PortalLearner learner)
        {
            _learner = learner;
            _list = new FlowPanel
            {
                Parent = parent,
                Width = parent.ContentRegion.Width - 10,
                HeightSizingMode = SizingMode.AutoSize,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0, 4),
            };
            Fill();
            _learner.Changed += Learner_Changed;
            _list.Disposed += (_, __) => _learner.Changed -= Learner_Changed;
        }

        public static void Build(Container parent, PortalLearner learner) => _ = new LearnedPortalsList(parent, learner);

        // Edits leave the rows as they are; only adding or removing portals redraws them.
        private void Learner_Changed(bool listChanged)
        {
            if (listChanged) Fill();
        }

        private void Fill()
        {
            foreach (var child in _list.Children.ToArray()) child.Dispose();
            int width = _list.Width;

            _ = new Label
            {
                Parent = _list,
                Size = new Point(width, 20),
                Text = _learner.Learned.Count == 0 ? "No learned portals yet." : $"Learned Portals ({_learner.Learned.Count})",
            };
            foreach (var portal in _learner.Learned) AddRow(portal, width);
        }

        private void AddRow(Portal portal, int width)
        {
            var row = new Panel { Parent = _list, Size = new Point(width, RowHeight), ShowTint = true };

            _ = new Label
            {
                Parent = row,
                Location = new Point(5, 2),
                Size = new Point(width - DeleteWidth - 15, 26),
                Text = $"On {portal.Origin ?? $"map {portal.MapId}"} at {portal.X:0}, {portal.Y:0}",
                VerticalAlignment = VerticalAlignment.Middle,
            };
            var delete = new StandardButton
            {
                Parent = row,
                Location = new Point(width - DeleteWidth - 5, 2),
                Size = new Point(DeleteWidth, 26),
                Text = "Delete",
                BasicTooltipText = "Remove this portal from the map and the search.",
            };
            delete.Click += (_, __) => _learner.Remove(portal);

            var destination = new TextBox
            {
                Parent = row,
                Location = new Point(5, 32),
                Size = new Point(width - KindWidth - 15, 26),
                Text = portal.Destination,
                BasicTooltipText = "Where the portal leads, as the wiki names it. Press Enter to save.",
            };
            void ApplyDestination()
            {
                string text = destination.Text.Trim();
                if (text.Length == 0) destination.Text = portal.Destination; // a portal must lead somewhere
                else if (text != portal.Destination)
                {
                    portal.Destination = text;
                    _learner.Edited();
                }
            }
            destination.EnterPressed += (_, __) => ApplyDestination();
            destination.InputFocusChanged += (_, e) => { if (!e.Value) ApplyDestination(); };

            var kind = new Dropdown
            {
                Parent = row,
                Location = new Point(width - KindWidth - 5, 32),
                Width = KindWidth,
                BasicTooltipText = "Which icon the portal gets on the world map.",
            };
            foreach (var (_, label) in Portal.KindLabels) kind.Items.Add(label);
            kind.SelectedItem = Portal.KindLabel(portal.Kind);
            kind.ValueChanged += (_, __) =>
            {
                var picked = Portal.KindFromLabel(kind.SelectedItem);
                if (picked == portal.Kind) return;
                portal.Kind = picked;
                _learner.Edited();
            };
        }
    }
}
