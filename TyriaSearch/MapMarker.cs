using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bytex.ModuleShared;

namespace Bytex.TyriaSearch
{
    /// <summary>
    /// Marks search results on the world map while the map is open, and on the minimap (compass) while it's closed
    /// if <see cref="ShowOnMinimap"/> is on. Covers the whole screen but never takes input: clicks and hovering reach
    /// the game's own map icons underneath. Right-clicking a marker opens a small menu to remove it
    /// (<see cref="RemoveRequested"/>).
    /// </summary>
    internal sealed class MapMarker : Control
    {
        private const int IconSize = 32;
        private const int EdgeMargin = 24;
        private const int HitMargin = 4;

        private readonly Func<int, int> _continentOf;
        private readonly Func<SearchEntry, AsyncTexture2D> _iconFor;
        private readonly Func<SearchEntry, Texture2D> _placeholderFor;
        // Each target's icon, kept while it's marked (see where it's drawn).
        private readonly Dictionary<SearchEntry, AsyncTexture2D> _icons = new Dictionary<SearchEntry, AsyncTexture2D>();
        private readonly AsyncTexture2D _waypointIcon = GameService.Content.GetRenderServiceTexture(Gw2Data.WaypointIcon);

        public List<SearchItem> Targets { get; } = new List<SearchItem>();

        /// <summary>Remove Icon was chosen in a marker's right-click menu.</summary>
        public event Action<SearchItem> RemoveRequested;

        // Where each marker was last drawn, in drawing order, and when; hit areas follow them.
        private readonly List<(Rectangle area, SearchItem target)> _drawn = new List<(Rectangle, SearchItem)>();
        private DateTime _paintedUtc;
        // Not drawn for this long (Blish HUD hidden, or loading): the hit areas go too.
        private static readonly TimeSpan PaintTimeout = TimeSpan.FromMilliseconds(250);
        private readonly List<HitArea> _hitAreas = new List<HitArea>();

        private readonly ContextMenuStrip _menu;
        private SearchItem _menuTarget;

        /// <summary>Portals to draw on the world map.</summary>
        public IReadOnlyList<Portal> Portals = Array.Empty<Portal>();
        public bool ShowPortals;
        public bool ShowOnMinimap;
        /// <summary>Write each marker's distance from the player under it, on the world map and on the minimap.</summary>
        public bool ShowDistanceOnMap, ShowDistanceOnMinimap;

        // Continent coordinates are 24 game units (inches, what skill ranges are measured in) apiece.
        private const double UnitsPerContinentUnit = 24;
        private static readonly Color DistanceColor = new Color(235, 235, 235);

        // Minimap icons are smaller, and edge-pinned markers sit this far inside the compass.
        private const float MinimapIconScale = 0.65f;
        private const int MinimapEdgeMargin = 10;

        /// <summary>Where a map is drawn and how continent positions land on it.</summary>
        private struct View
        {
            public Rectangle Bounds; // UI units
            public double UnitsPerUiPixel;
            public double Rotation; // radians, minimap rotation mode only
            public bool Minimap;
            public int EdgeMargin;
            public float IconScale;
        }

        private const int PortalSize = 24;
        private static readonly Color PortalLabelColor = new Color(200, 230, 255);
        private readonly Dictionary<PortalKind, AsyncTexture2D> _portalIcons = ((PortalKind[])Enum.GetValues(typeof(PortalKind)))
            .ToDictionary(k => k, k => AsyncTexture2D.FromAssetId(Portal.IconAssetId(k)));
        // Labels only from about zone zoom; zoomed out, 200 names overlap into a smear. Continent units per UI pixel.
        private const double PortalLabelMaxScale = 8;

        /// <param name="continentOf">Continent of a map id: Tyria (1) or the Mists (2).</param>
        /// <param name="placeholderFor">Bundled icon drawn while the result's own icon downloads.</param>
        public MapMarker(Func<int, int> continentOf, Func<SearchEntry, AsyncTexture2D> iconFor, Func<SearchEntry, Texture2D> placeholderFor)
        {
            _continentOf = continentOf;
            _iconFor = iconFor;
            _placeholderFor = placeholderFor;

            _menu = new ContextMenuStrip();
            var remove = _menu.AddMenuItem("Remove Icon");
            remove.BasicTooltipText = "Remove this marker from the map.";
            remove.Click += (_, __) =>
            {
                var target = _menuTarget;
                CloseMenu();
                if (target != null && Targets.Contains(target)) RemoveRequested?.Invoke(target);
            };

            // Any other input closes the menu.
            GameService.Input.Mouse.LeftMouseButtonPressed += Mouse_ButtonPressed;
            GameService.Input.Mouse.RightMouseButtonPressed += Mouse_ButtonPressed;
            GameService.Input.Mouse.MouseWheelScrolled += Mouse_MouseWheelScrolled;
            GameService.Input.Keyboard.KeyPressed += Keyboard_KeyPressed;
        }

        protected override CaptureType CapturesInput() => CaptureType.None;

        private void ShowMenu(SearchItem target)
        {
            _menuTarget = target;
            _menu.Show(GameService.Input.Mouse.Position); // its top left corner at the cursor, so it opens under it
        }

        private void CloseMenu()
        {
            _menuTarget = null;
            if (_menu.Visible) _menu.Hide();
        }

        private void Mouse_ButtonPressed(object sender, MouseEventArgs e)
        {
            if (_menu.Visible && !_menu.AbsoluteBounds.Contains(GameService.Input.Mouse.Position)) CloseMenu();
        }

        private void Mouse_MouseWheelScrolled(object sender, MouseEventArgs e) => CloseMenu();
        private void Keyboard_KeyPressed(object sender, KeyboardEventArgs e) => CloseMenu();

        /// <summary>Keeps a hit area over each marker drawn last frame, and closes the menu once its marker is gone.</summary>
        public override void DoUpdate(GameTime gameTime)
        {
            base.DoUpdate(gameTime);
            bool painted = Visible && DateTime.UtcNow - _paintedUtc < PaintTimeout;
            int count = painted ? _drawn.Count : 0;
            while (_hitAreas.Count < count) _hitAreas.Add(new HitArea(ShowMenu) { Parent = Parent, Visible = false });
            for (int i = 0; i < _hitAreas.Count; i++)
            {
                var hit = _hitAreas[i];
                if (i < count)
                {
                    // Drawn later, on top: a higher z-index, so overlapping markers get the one you can see.
                    (hit.Target, hit.Location, hit.Size, hit.ZIndex) = (_drawn[i].target, _drawn[i].area.Location, _drawn[i].area.Size, ZIndex + 1 + i);
                    hit.Visible = true;
                }
                else if (hit.Visible)
                {
                    hit.Visible = false;
                    hit.Target = null;
                }
            }
            if (_menu.Visible && (_menuTarget == null || !_drawn.Any(d => d.target == _menuTarget) || !painted)) CloseMenu();
        }

        protected override void DisposeControl()
        {
            GameService.Input.Mouse.LeftMouseButtonPressed -= Mouse_ButtonPressed;
            GameService.Input.Mouse.RightMouseButtonPressed -= Mouse_ButtonPressed;
            GameService.Input.Mouse.MouseWheelScrolled -= Mouse_MouseWheelScrolled;
            GameService.Input.Keyboard.KeyPressed -= Keyboard_KeyPressed;
            foreach (var hit in _hitAreas) hit.Dispose();
            _menu.Dispose();
            base.DisposeControl();
        }

        /// <summary>
        /// An invisible control over one marker that notices right-clicks without blocking anything: the game still gets
        /// every click, and hovering, so its own icons underneath work as usual.
        /// </summary>
        private sealed class HitArea : Control
        {
            private readonly Action<SearchItem> _rightClicked;
            public SearchItem Target;

            public HitArea(Action<SearchItem> rightClicked) => _rightClicked = rightClicked;

            protected override CaptureType CapturesInput() => CaptureType.DoNotBlock;

            // On release, once the press has closed any menu already open.
            protected override void OnRightMouseButtonReleased(MouseEventArgs e)
            {
                base.OnRightMouseButtonReleased(e);
                if (Target != null) _rightClicked(Target);
            }

            protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds) { }
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            _drawn.Clear();
            _paintedUtc = DateTime.UtcNow;
            // Let go of icons for targets that were removed.
            if (_icons.Count > Targets.Count)
                foreach (var gone in _icons.Keys.Where(e => !Targets.Any(t => t.Entry == e)).ToList()) _icons.Remove(gone);
            var ui = GameService.Gw2Mumble.UI;
            // On a loading screen Mumble stops updating, so IsMapOpen stays true from before it.
            if (ui.MapScale <= 0 || !GameService.GameIntegration.Gw2Instance.IsInGame) return;
            double unitsPerUiPixel = ui.MapScale * Minimap.PixelsPerUnit();
            View view;
            if (ui.IsMapOpen)
                view = new View { Bounds = new Rectangle(0, 0, Width, Height), UnitsPerUiPixel = unitsPerUiPixel, EdgeMargin = EdgeMargin, IconScale = CloseZoomScale(ui.MapScale) };
            else if (ShowOnMinimap && (Targets.Count > 0 || ShowPortals))
                view = new View
                {
                    Bounds = Minimap.Bounds(),
                    UnitsPerUiPixel = unitsPerUiPixel,
                    Rotation = ui.IsCompassRotationEnabled ? ui.CompassRotation : 0,
                    Minimap = true,
                    EdgeMargin = MinimapEdgeMargin,
                    IconScale = MinimapIconScale * CloseZoomScale(ui.MapScale),
                };
            else return;
            if (view.Bounds.Width <= 0 || view.Bounds.Height <= 0) return;

            if (ShowPortals) PaintPortals(spriteBatch, view);
            foreach (var target in Targets) PaintMarker(spriteBatch, target, view);
        }

        // The game draws its map icons bigger over the closest few zoom levels. Measured against its world map waypoints
        // (MapScale is continent units per screen pixel): ~1.3x ours at 0.86 and 1.4 (the two closest), ~1.2x at 1.8,
        // and the same as ours from the next level out. So: full growth up to CloseZoomFull, easing on a log scale (so
        // the zoom animation doesn't jump) to normal size at CloseZoomEnd. The minimap uses the same curve, since
        // MapScale means the same there.
        private const double CloseZoomFull = 1.45;
        private const double CloseZoomEnd = 2.3;
        private const float CloseZoomGrowth = 1.3f;

        private static float CloseZoomScale(double mapScale)
        {
            double t = Math.Log(CloseZoomEnd / mapScale) / Math.Log(CloseZoomEnd / CloseZoomFull);
            return 1 + (CloseZoomGrowth - 1) * MathHelper.Clamp((float)t, 0, 1);
        }


        private void PaintPortals(SpriteBatch spriteBatch, View view)
        {
            int continent = _continentOf(GameService.Gw2Mumble.CurrentMap.Id);
            bool labels = !view.Minimap && view.UnitsPerUiPixel <= PortalLabelMaxScale;
            int size = (int)(PortalSize * view.IconScale);
            foreach (var portal in Portals)
            {
                if (portal.Continent != continent) continue;
                var position = ToScreen(portal.X, portal.Y, view);
                if (!view.Bounds.Contains(position)) continue;

                var rect = new Rectangle((int)position.X - size / 2, (int)position.Y - size / 2, size, size);
                var icon = _portalIcons[portal.Kind];
                if (icon.HasTexture) spriteBatch.DrawOnCtrl(this, icon, rect);
                if (labels)
                    spriteBatch.DrawStringOnCtrl(this, "To " + portal.Destination, GameService.Content.DefaultFont14,
                        new Rectangle(rect.X - 100, rect.Bottom, 200 + size, 18), PortalLabelColor,
                        false, true, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }

        /// <summary>
        /// Mumble gives the map's centre in continent units and its scale in continent units per screen pixel;
        /// Blish HUD draws in its own UI units, which are screen pixels divided by its UI scale.
        /// With the minimap's rotation mode on, it turns with the camera around its centre.
        /// </summary>
        private static Vector2 ToScreen(float x, float y, View view)
        {
            var ui = GameService.Gw2Mumble.UI;
            var offset = new Vector2(
                (float)((x - ui.MapCenter.X) / view.UnitsPerUiPixel),
                (float)((y - ui.MapCenter.Y) / view.UnitsPerUiPixel));
            if (view.Rotation != 0) offset = Vector2.Transform(offset, Matrix.CreateRotationZ((float)view.Rotation));
            return new Vector2(view.Bounds.Center.X, view.Bounds.Center.Y) + offset;
        }

        private void PaintMarker(SpriteBatch spriteBatch, SearchItem target, View view)
        {
            var entry = target.Entry;
            if (entry.X == null || entry.Y == null) return;
            // The map shows the continent the player is on.
            if (_continentOf(entry.MapId) != _continentOf(GameService.Gw2Mumble.CurrentMap.Id)) return;

            var position = ToScreen(entry.X.Value, entry.Y.Value, view);

            // Off screen: pin to the nearest edge so the marker still shows which way to go.
            var bounds = view.Bounds;
            var pinned = new Vector2(
                MathHelper.Clamp(position.X, bounds.Left + view.EdgeMargin, bounds.Right - view.EdgeMargin),
                MathHelper.Clamp(position.Y, bounds.Top + view.EdgeMargin, bounds.Bottom - view.EdgeMargin));
            bool offScreen = pinned != position;

            // Pulse so it stands out against the map's own icons.
            double seconds = GameService.Overlay.CurrentGameTime?.TotalGameTime.TotalSeconds ?? 0;
            float pulse = 0.65f + 0.35f * (float)Math.Sin(seconds * 4);
            int size = (int)(IconSize * view.IconScale * (offScreen ? 0.8f : 1f + 0.15f * pulse));

            // Kept, not asked for each frame: Blish HUD holds render service icons weakly, so a collected one comes back as a
            // fresh transparent texture until it reloads, and the marker would flash. Asked again if its download failed.
            if (!_icons.TryGetValue(entry, out var icon) || TyriaSearchModule.IsErrorTexture(icon)) _icons[entry] = icon = _iconFor(entry);
            // Not HasTexture: a downloading texture is a transparent pixel until it arrives, and Blish HUD's error texture if it fails.
            if (!TyriaSearchModule.IsLoaded(icon)) icon = _placeholderFor(entry) ?? _waypointIcon;

            var iconRect = new Rectangle((int)pinned.X - size / 2, (int)pinned.Y - size / 2, size, size);
            var backing = Inflate(iconRect, view.Minimap ? 2 : 4);
            // A little bigger than the marker, which pulses, so a right-click near its edge still counts.
            _drawn.Add((Inflate(backing, HitMargin), target));
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, backing, Color.Black * 0.5f);
            if (icon.HasTexture) spriteBatch.DrawOnCtrl(this, icon, iconRect, Color.White * (offScreen ? 0.8f : 1f));

            // Steady, unlike the name: a number that pulses is hard to read.
            string distance = (view.Minimap ? ShowDistanceOnMinimap : ShowDistanceOnMap) ? Distance(entry) : null;
            if (view.Minimap) // no room for names on the minimap
            {
                if (distance != null)
                    spriteBatch.DrawStringOnCtrl(this, distance, GameService.Content.DefaultFont12,
                        new Rectangle((int)pinned.X - 60, backing.Bottom + 1, 120, 14), DistanceColor,
                        false, true, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
                return;
            }

            string label = entry.Name + (entry.Approximate ? " (approximate)" : "") + (offScreen ? " (off screen)" : "");
            spriteBatch.DrawStringOnCtrl(this, label, GameService.Content.DefaultFont16,
                new Rectangle((int)pinned.X - 150, iconRect.Bottom + 4, 300, 20), new Color(255, 230, 120) * pulse,
                false, true, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
            if (distance != null)
                spriteBatch.DrawStringOnCtrl(this, distance, GameService.Content.DefaultFont14,
                    new Rectangle((int)pinned.X - 150, iconRect.Bottom + 24, 300, 18), DistanceColor,
                    false, true, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        /// <summary>
        /// Straight-line distance from the player in game units, e.g. "2,450 away"; "~" in front when the marker's
        /// position is approximate. Null while Mumble has no position.
        /// </summary>
        private static string Distance(SearchEntry entry)
        {
            var mumble = GameService.Gw2Mumble;
            if (!mumble.IsAvailable) return null;
            var player = mumble.RawClient.PlayerLocationMap; // continent coordinates
            double dx = entry.X.Value - player.X, dy = entry.Y.Value - player.Y;
            double units = Math.Sqrt(dx * dx + dy * dy) * UnitsPerContinentUnit;
            return (entry.Approximate ? "~" : "") + units.ToString("N0") + " away";
        }

        private static Rectangle Inflate(Rectangle rect, int by) =>
            new Rectangle(rect.X - by, rect.Y - by, rect.Width + by * 2, rect.Height + by * 2);
    }
}
