using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;

namespace TyriaSearch
{
    /// <summary>
    /// The panel at the top of the screen that asks about portals <see cref="PortalLearner"/> noticed, one at a time: the
    /// destination and kind can be changed before adding. With none waiting, it asks the player to go back through the last
    /// one to add the return portal too. Dragged by any part that isn't a button or box; the position is saved.
    /// </summary>
    internal sealed class PortalReview : IDisposable
    {
        private const int PanelWidth = 380;
        private const int LabelWidth = 70;

        private sealed class Candidate
        {
            public Portal Portal;
            public bool IsReturn;
        }

        private readonly Action<Portal, bool> _add;
        private readonly Action<Portal, bool> _discard;
        private readonly Action _skipReturn;
        private readonly Queue<Candidate> _queue = new Queue<Candidate>();

        private readonly Panel _panel;
        private readonly Label _origin, _destinationLabel, _kindLabel, _hint;
        private readonly TextBox _destination;
        private readonly Dropdown _kind;
        private readonly StandardButton _addButton, _discardButton, _skipButton;

        private readonly Func<Point?> _savedPosition;
        private readonly Action<Point> _savePosition;
        private Point? _dragOffset; // from the panel's corner to the mouse, while dragging

        private Candidate _shown;
        private string _returnPrompt;
        private bool _stale = true;

        /// <param name="add">Adds the reviewed portal; true for a return portal.</param>
        /// <param name="discard">The player didn't want the portal; true for a return portal.</param>
        /// <param name="skipReturn">The player won't go back for the return portal.</param>
        /// <param name="savedPosition">Where the player last put the panel, or null for the top centre.</param>
        /// <param name="savePosition">Called when the player has dragged the panel.</param>
        public PortalReview(Action<Portal, bool> add, Action<Portal, bool> discard, Action skipReturn,
            Func<Point?> savedPosition, Action<Point> savePosition)
        {
            _savedPosition = savedPosition;
            _savePosition = savePosition;
            _add = add;
            _discard = discard;
            _skipReturn = skipReturn;

            _panel = new Panel
            {
                Parent = GameService.Graphics.SpriteScreen,
                Width = PanelWidth,
                HeightSizingMode = SizingMode.AutoSize,
                ShowBorder = true,
                ShowTint = true,
                Title = "Tyria Search",
                Visible = false,
            };
            int width = PanelWidth - 20;

            _origin = new Label { Parent = _panel, Location = new Point(5, 4), Size = new Point(width, 20) };
            _destinationLabel = new Label { Parent = _panel, Location = new Point(5, 32), Size = new Point(LabelWidth, 26), Text = "Leads to", VerticalAlignment = VerticalAlignment.Middle };
            _destination = new TextBox
            {
                Parent = _panel, Location = new Point(5 + LabelWidth, 32), Size = new Point(width - LabelWidth, 26),
                BasicTooltipText = "Where the portal leads, as the wiki names it: the search and map label say \"Portal to\" this, and the wiki opens its page.",
            };
            _kindLabel = new Label { Parent = _panel, Location = new Point(5, 64), Size = new Point(LabelWidth, 26), Text = "Kind", VerticalAlignment = VerticalAlignment.Middle };
            _kind = new Dropdown { Parent = _panel, Location = new Point(5 + LabelWidth, 64), Width = width - LabelWidth, BasicTooltipText = "Picks the map icon." };
            foreach (var (_, label) in Portal.KindLabels) _kind.Items.Add(label);
            _hint = new Label
            {
                Parent = _panel, Location = new Point(5, 96), Size = new Point(width, 36), WrapText = true,
                Font = GameService.Content.DefaultFont12, TextColor = new Color(200, 200, 200),
            };
            _addButton = new StandardButton { Parent = _panel, Location = new Point(5, 136), Size = new Point(110, 26), Text = "Add" };
            _discardButton = new StandardButton { Parent = _panel, Location = new Point(125, 136), Size = new Point(110, 26), Text = "Discard" };
            _skipButton = new StandardButton
            {
                Parent = _panel, Location = new Point(5, 136), Size = new Point(110, 26), Text = "Skip",
                BasicTooltipText = "Don't add the return portal.",
            };
            _addButton.Click += (_, __) => Close(add: true);
            _discardButton.Click += (_, __) => Close(add: false);
            _skipButton.Click += (_, __) => _skipReturn();
            _panel.LeftMouseButtonPressed += (_, __) => _dragOffset = GameService.Input.Mouse.Position - _panel.Location;
        }

        /// <summary>Waiting for review, so the same portal taken again isn't noticed twice.</summary>
        public IEnumerable<Portal> Pending => _queue.Select(c => c.Portal);

        public void Enqueue(Portal portal, bool isReturn) => _queue.Enqueue(new Candidate { Portal = portal, IsReturn = isReturn });

        /// <summary>Call every frame.</summary>
        /// <param name="returnPrompt">The map to go back to for a return portal, or null if none is expected.</param>
        public void Update(string returnPrompt)
        {
            var next = _queue.Count > 0 ? _queue.Peek() : null;
            if (_stale || next != _shown || returnPrompt != _returnPrompt)
            {
                _stale = false;
                _shown = next;
                _returnPrompt = returnPrompt;
                if (next != null) ShowCandidate(next, returnPrompt);
                else if (returnPrompt != null) ShowReturnPrompt(returnPrompt);
            }
            _panel.Visible = next != null || returnPrompt != null;
            Place();
        }

        private void Place()
        {
            var screen = GameService.Graphics.SpriteScreen.Size;
            Point location;
            if (_dragOffset.HasValue)
            {
                location = GameService.Input.Mouse.Position - _dragOffset.Value;
                if (GameService.Input.Mouse.State.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Released)
                {
                    _dragOffset = null;
                    _savePosition(Clamp(location, screen));
                }
            }
            else location = _savedPosition() ?? new Point((screen.X - PanelWidth) / 2, 60);
            _panel.Location = Clamp(location, screen);
        }

        // Keeps the whole panel on screen, so it can't be lost after a resolution or UI size change.
        private Point Clamp(Point location, Point screen) => new Point(
            MathHelper.Clamp(location.X, 0, Math.Max(0, screen.X - _panel.Width)),
            MathHelper.Clamp(location.Y, 0, Math.Max(0, screen.Y - _panel.Height)));

        private void ShowCandidate(Candidate candidate, string returnPrompt)
        {
            var portal = candidate.Portal;
            _panel.Title = candidate.IsReturn ? "Tyria Search: return portal" : "Tyria Search: new portal";
            _origin.Text = $"On {portal.Origin}";
            _destination.Text = portal.Destination;
            _kind.SelectedItem = Portal.KindLabel(portal.Kind);
            _hint.Text = candidate.IsReturn ? "The way back from the portal you just added."
                : returnPrompt != null ? $"After adding, go back through it to {returnPrompt} to add the return portal too."
                : "Not one Tyria Search knows. Add it to the map and the search?";
            SetEditing(true);
        }

        private void ShowReturnPrompt(string map)
        {
            _panel.Title = "Tyria Search: return portal";
            _origin.Text = $"Go back through the portal to {map}";
            _hint.Text = "Tyria Search will ask about the portal on this side once you're back.";
            SetEditing(false);
        }

        private void SetEditing(bool editing)
        {
            _destinationLabel.Visible = _destination.Visible = _kindLabel.Visible = _kind.Visible = editing;
            _addButton.Visible = _discardButton.Visible = editing;
            _skipButton.Visible = !editing;
        }

        private void Close(bool add)
        {
            if (_queue.Count == 0) return;
            var candidate = _queue.Dequeue();
            var portal = candidate.Portal;
            if (add)
            {
                string destination = _destination.Text.Trim();
                if (destination.Length > 0) portal.Destination = destination;
                portal.Kind = Portal.KindFromLabel(_kind.SelectedItem);
                _add(portal, candidate.IsReturn);
            }
            else
            {
                _discard(portal, candidate.IsReturn);
            }
            _destination.UnsetFocus();
            _stale = true; // show the next, or the return prompt
        }

        public void Dispose() => _panel.Dispose();
    }
}
