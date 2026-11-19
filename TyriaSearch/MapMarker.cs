using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TyriaSearch
{
    /// <summary>
    /// Marks search results on the world map while the map is open, and on the minimap (compass) while it's closed
    /// if <see cref="ShowOnMinimap"/> is on. Covers the whole screen but never takes clicks.
    /// </summary>
    internal sealed class MapMarker : Control
    {
        private const int IconSize = 32;
        private const int EdgeMargin = 24;

        private readonly Func<int, int> _continentOf;
        private readonly Func<SearchEntry, AsyncTexture2D> _iconFor;

        public List<SearchItem> Targets { get; } = new List<SearchItem>();

        /// <summary>Portals to draw on the world map.</summary>
        public IReadOnlyList<Portal> Portals = Array.Empty<Portal>();
        public bool ShowPortals;
        public bool ShowOnMinimap;

        // Minimap icons are smaller, and edge-pinned markers sit this far inside the compass.
        private const float MinimapIconScale = 0.65f;
        private const int MinimapEdgeMargin = 10;
        // Gap between the bottom of the screen and a bottom-right compass, in screen pixels.
        private const int MinimapBottomGap = 36;

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
        public MapMarker(Func<int, int> continentOf, Func<SearchEntry, AsyncTexture2D> iconFor)
        {
            _continentOf = continentOf;
            _iconFor = iconFor;
        }

        protected override CaptureType CapturesInput() => CaptureType.None;

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            var ui = GameService.Gw2Mumble.UI;
            if (ui.MapScale <= 0) return;
            double unitsPerUiPixel = ui.MapScale * GameService.Graphics.UIScaleMultiplier;
            View view;
            if (ui.IsMapOpen)
                view = new View { Bounds = new Rectangle(0, 0, Width, Height), UnitsPerUiPixel = unitsPerUiPixel, EdgeMargin = EdgeMargin, IconScale = CloseZoomScale(ui.MapScale) };
            else if (ShowOnMinimap && GameService.GameIntegration.Gw2Instance.IsInGame && (Targets.Count > 0 || ShowPortals))
                view = new View
                {
                    Bounds = MinimapBounds(),
                    UnitsPerUiPixel = unitsPerUiPixel,
                    Rotation = ui.IsCompassRotationEnabled ? ui.CompassRotation : 0,
                    Minimap = true,
                    EdgeMargin = MinimapEdgeMargin,
                    IconScale = MinimapIconScale * CloseZoomScale(ui.MapScale),
                };
            else return;
            if (view.Bounds.Width <= 0 || view.Bounds.Height <= 0) return;

            if (ShowPortals) PaintPortals(spriteBatch, view);
            foreach (var target in Targets) PaintMarker(spriteBatch, target.Entry, view);
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

        /// <summary>
        /// The compass in UI units. Mumble gives its size in screen pixels; it sits at the right edge, either at the
        /// top or just above the bottom.
        /// </summary>
        private static Rectangle MinimapBounds()
        {
            var ui = GameService.Gw2Mumble.UI;
            var screen = GameService.Graphics.SpriteScreen;
            float scale = GameService.Graphics.UIScaleMultiplier;
            int width = (int)(ui.CompassSize.Width / scale);
            int height = (int)(ui.CompassSize.Height / scale);
            int y = ui.IsCompassTopRight ? 0 : screen.Height - height - (int)(MinimapBottomGap / scale);
            return new Rectangle(screen.Width - width, y, width, height);
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

        private void PaintMarker(SpriteBatch spriteBatch, SearchEntry entry, View view)
        {
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

            var icon = _iconFor(entry);
            if (icon == null || !icon.HasTexture) icon = GameService.Content.GetRenderServiceTexture(Gw2Data.WaypointIcon);

            var iconRect = new Rectangle((int)pinned.X - size / 2, (int)pinned.Y - size / 2, size, size);
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, Inflate(iconRect, view.Minimap ? 2 : 4), Color.Black * 0.5f);
            if (icon.HasTexture) spriteBatch.DrawOnCtrl(this, icon, iconRect, Color.White * (offScreen ? 0.8f : 1f));
            if (view.Minimap) return; // no room for names on the minimap

            string label = entry.Name + (entry.Approximate ? " (approximate)" : "") + (offScreen ? " (off screen)" : "");
            spriteBatch.DrawStringOnCtrl(this, label, GameService.Content.DefaultFont16,
                new Rectangle((int)pinned.X - 150, iconRect.Bottom + 4, 300, 20), new Color(255, 230, 120) * pulse,
                false, true, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        private static Rectangle Inflate(Rectangle rect, int by) =>
            new Rectangle(rect.X - by, rect.Y - by, rect.Width + by * 2, rect.Height + by * 2);
    }
}
