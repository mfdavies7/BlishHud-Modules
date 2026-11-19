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
    /// Marks search results on the world map while the map is open. Covers the whole screen but never takes clicks.
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
            if (!ui.IsMapOpen || ui.MapScale <= 0) return;
            if (ShowPortals) PaintPortals(spriteBatch);
            foreach (var target in Targets) PaintMarker(spriteBatch, target.Entry);
        }

        private void PaintPortals(SpriteBatch spriteBatch)
        {
            var ui = GameService.Gw2Mumble.UI;
            int continent = _continentOf(GameService.Gw2Mumble.CurrentMap.Id);
            double unitsPerUiPixel = ui.MapScale * GameService.Graphics.UIScaleMultiplier;
            bool labels = unitsPerUiPixel <= PortalLabelMaxScale;
            foreach (var portal in Portals)
            {
                if (portal.Continent != continent) continue;
                var position = ToScreen(portal.X, portal.Y);
                if (position.X < 0 || position.Y < 0 || position.X > Width || position.Y > Height) continue;

                var rect = new Rectangle((int)position.X - PortalSize / 2, (int)position.Y - PortalSize / 2, PortalSize, PortalSize);
                var icon = _portalIcons[portal.Kind];
                if (icon.HasTexture) spriteBatch.DrawOnCtrl(this, icon, rect);
                if (labels)
                    spriteBatch.DrawStringOnCtrl(this, "To " + portal.Destination, GameService.Content.DefaultFont14,
                        new Rectangle(rect.X - 100, rect.Bottom, 200 + PortalSize, 18), PortalLabelColor,
                        false, true, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }

        /// <summary>
        /// Mumble gives the map's centre in continent units and its scale in continent units per screen pixel;
        /// Blish HUD draws in its own UI units, which are screen pixels divided by its UI scale.
        /// </summary>
        private Vector2 ToScreen(float x, float y)
        {
            var ui = GameService.Gw2Mumble.UI;
            double unitsPerUiPixel = ui.MapScale * GameService.Graphics.UIScaleMultiplier;
            return new Vector2(
                Width / 2f + (float)((x - ui.MapCenter.X) / unitsPerUiPixel),
                Height / 2f + (float)((y - ui.MapCenter.Y) / unitsPerUiPixel));
        }

        private void PaintMarker(SpriteBatch spriteBatch, SearchEntry entry)
        {
            if (entry.X == null || entry.Y == null) return;
            // The map shows the continent the player is on.
            if (_continentOf(entry.MapId) != _continentOf(GameService.Gw2Mumble.CurrentMap.Id)) return;

            var position = ToScreen(entry.X.Value, entry.Y.Value);

            // Off screen: pin to the nearest edge so the marker still shows which way to go.
            var pinned = new Vector2(
                MathHelper.Clamp(position.X, EdgeMargin, Width - EdgeMargin),
                MathHelper.Clamp(position.Y, EdgeMargin, Height - EdgeMargin));
            bool offScreen = pinned != position;

            // Pulse so it stands out against the map's own icons.
            double seconds = GameService.Overlay.CurrentGameTime?.TotalGameTime.TotalSeconds ?? 0;
            float pulse = 0.65f + 0.35f * (float)Math.Sin(seconds * 4);
            int size = (int)(IconSize * (offScreen ? 0.8f : 1f + 0.15f * pulse));

            var icon = _iconFor(entry);
            if (icon == null || !icon.HasTexture) icon = GameService.Content.GetRenderServiceTexture(Gw2Data.WaypointIcon);

            var iconRect = new Rectangle((int)pinned.X - size / 2, (int)pinned.Y - size / 2, size, size);
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, Inflate(iconRect, 4), Color.Black * 0.5f);
            if (icon.HasTexture) spriteBatch.DrawOnCtrl(this, icon, iconRect, Color.White * (offScreen ? 0.8f : 1f));

            string label = entry.Name + (entry.Approximate ? " (approximate)" : "") + (offScreen ? " (off screen)" : "");
            spriteBatch.DrawStringOnCtrl(this, label, GameService.Content.DefaultFont16,
                new Rectangle((int)pinned.X - 150, iconRect.Bottom + 4, 300, 20), new Color(255, 230, 120) * pulse,
                false, true, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        private static Rectangle Inflate(Rectangle rect, int by) =>
            new Rectangle(rect.X - by, rect.Y - by, rect.Width + by * 2, rect.Height + by * 2);
    }
}
