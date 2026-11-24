using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;

namespace Bytex.TyriaSearch
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
        private readonly Action<Portal, bool> _ignore;
        private readonly Action<Portal, bool> _ignoreDestination;
        private readonly Action _skipReturn;
        private readonly Queue<Candidate> _queue = new Queue<Candidate>();

        private readonly Panel _panel;
        private readonly Label _origin, _destinationLabel, _kindLabel, _hint;
        private readonly TextBox _destination;
        private readonly Dropdown _kind;
        private readonly StandardButton _addButton, _discardButton, _ignoreButton, _ignoreDestinationButton, _skipButton;

        private readonly Func<Point?> _savedPosition;
        private readonly Action<Point> _savePosition;
        private Point? _dragOffset; // from the panel's corner to the mouse, while dragging

        private Candidate _shown;
        private string _returnPrompt;
        private bool _stale = true;

        /// <param name="add">Adds the reviewed portal; true for a return portal.</param>
        /// <param name="discard">The player didn't want the portal; true for a return portal.</param>
        /// <param name="ignore">The player never wants to be asked about this spot again; true for a return portal.</param>
        /// <param name="ignoreDestination">The player never wants to be asked about portals to where this one leads; true for a return portal.</param>
        /// <param name="skipReturn">The player won't go back for the return portal.</param>
        /// <param name="savedPosition">Where the player last put the panel, or null for the top centre.</param>
        /// <param name="savePosition">Called when the player has dragged the panel.</param>
        public PortalReview(Action<Portal, bool> add, Action<Portal, bool> discard, Action<Portal, bool> ignore,
            Action<Portal, bool> ignoreDestination, Action skipReturn,
            Func<Point?> savedPosition, Action<Point> savePosition)
        {
            _savedPosition = savedPosition;
            _savePosition = savePosition;
            _add = add;
            _discard = discard;
            _ignore = ignore;
            _ignoreDestination = ignoreDestination;
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
            _destinationLabel = new Label { Parent = _panel, Location = new Point(5, 32), Size = new Point(LabelWidth, 26), Text = "Leads To", VerticalAlignment = VerticalAlignment.Middle };
            _destination = new TextBox
            {
                Parent = _panel, Location = new Point(5 + LabelWidth, 32), Size = new Point(width - LabelWidth, 26),
                BasicTooltipText = "Where the portal leads, as the wiki names it: the search result and map label are named after it, and the wiki opens its page.",
            };
            _kindLabel = new Label { Parent = _panel, Location = new Point(5, 64), Size = new Point(LabelWidth, 26), Text = "Kind", VerticalAlignment = VerticalAlignment.Middle };
            _kind = new Dropdown { Parent = _panel, Location = new Point(5 + LabelWidth, 64), Width = width - LabelWidth, BasicTooltipText = "Which icon the portal gets on the world map." };
            foreach (var (_, label) in Portal.KindLabels) _kind.Items.Add(label);
            _hint = new Label
            {
                Parent = _panel, Location = new Point(5, 96), Size = new Point(width, 36), WrapText = true,
                Font = GameService.Content.DefaultFont12, TextColor = new Color(200, 200, 200),
            };
            // Add and Discard split the row between them.
            int halfWidth = (width - 10) / 2;
            _addButton = new StandardButton { Parent = _panel, Location = new Point(5, 136), Size = new Point(halfWidth, 26), Text = "Add" };
            _discardButton = new StandardButton { Parent = _panel, Location = new Point(5 + width - halfWidth, 136), Size = new Point(halfWidth, 26), Text = "Discard" };
            _ignoreButton = new StandardButton
            {
                Parent = _panel, Location = new Point(5, 168), Size = new Point(width, 26), Text = "Do Not Ask Again for This Location",
                BasicTooltipText = "Don't add the portal, and never ask about a portal here again. Saved to ignored-portals.json in the tyriasearch folder; Forget Ignored Locations in the settings clears them.",
            };
            _ignoreDestinationButton = new StandardButton
            {
                Parent = _panel, Location = new Point(5, 200), Size = new Point(width, 26), Text = "Do Not Ask Again for This Destination",
            };
            _skipButton = new StandardButton
            {
                Parent = _panel, Location = new Point(5, 136), Size = new Point(width, 26), Text = "Skip",
                BasicTooltipText = "Don't add the return portal.",
            };
            _addButton.Click += (_, __) => Close(Choice.Add);
            _discardButton.Click += (_, __) => Close(Choice.Discard);
            _ignoreButton.Click += (_, __) => Close(Choice.Ignore);
            _ignoreDestinationButton.Click += (_, __) => Close(Choice.IgnoreDestination);
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
            // Kept for later during loading screens and character select; it stays with the world map open.
            bool inGame = GameService.GameIntegration.Gw2Instance.IsInGame;
            _panel.Visible = inGame && (next != null || returnPrompt != null);
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
            _panel.Title = candidate.IsReturn ? "Tyria Search: Return Portal" : "Tyria Search: New Portal";
            _origin.Text = $"On {portal.Origin}";
            _destination.Text = portal.Destination;
            _kind.SelectedItem = Portal.KindLabel(portal.Kind);
            _ignoreDestinationButton.BasicTooltipText = $"Don't add the portal, and never ask about a portal to {portal.DestinationMap} again, from anywhere. "
                + "Saved to ignored-destinations.json in the tyriasearch folder; Forget Ignored Destinations in the settings clears them.";
            _hint.Text = candidate.IsReturn ? "This is the way back through the portal you just added."
                : returnPrompt != null ? $"After adding, go back through it to {returnPrompt} to add the return portal too."
                : "Tyria Search doesn't know this portal yet. Add it to the map and the search?";
            SetEditing(true);
        }

        private void ShowReturnPrompt(string map)
        {
            _panel.Title = "Tyria Search: Return Portal";
            _origin.Text = $"Go back through the portal to {map}";
            _hint.Text = "Tyria Search will ask about the portal on this side once you're back.";
            SetEditing(false);
        }

        private void SetEditing(bool editing)
        {
            _destinationLabel.Visible = _destination.Visible = _kindLabel.Visible = _kind.Visible = editing;
            _addButton.Visible = _discardButton.Visible = _ignoreButton.Visible = _ignoreDestinationButton.Visible = editing;
            _skipButton.Visible = !editing;
        }

        private enum Choice { Add, Discard, Ignore, IgnoreDestination }

        private void Close(Choice choice)
        {
            if (_queue.Count == 0) return;
            var candidate = _queue.Dequeue();
            var portal = candidate.Portal;
            if (choice == Choice.Discard)
            {
                _discard(portal, candidate.IsReturn);
            }
            else if (choice == Choice.IgnoreDestination)
            {
                _ignoreDestination(portal, candidate.IsReturn);
            }
            else
            {
                // Ignored spots keep the edits too, so the file says what the player took the portal to be.
                string destination = _destination.Text.Trim();
                if (destination.Length > 0) portal.Destination = destination;
                portal.Kind = Portal.KindFromLabel(_kind.SelectedItem);
                (choice == Choice.Add ? _add : _ignore)(portal, candidate.IsReturn);
            }
            _destination.UnsetFocus();
            _stale = true; // show the next, or the return prompt
        }

        public void Dispose() => _panel.Dispose();
    }
}
