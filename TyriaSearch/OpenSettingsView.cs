using System;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework;

namespace mfdavies7.TyriaSearch
{
    /// <summary>Blish HUD's settings page for the module: the settings are in the module's own window, which this opens.</summary>
    internal sealed class OpenSettingsView : View
    {
        private readonly Action _open;

        public OpenSettingsView(Action open) => _open = open;

        protected override void Build(Container buildPanel)
        {
            _ = new Label
            {
                Parent = buildPanel,
                Location = new Point(10, 10),
                Size = new Point(buildPanel.ContentRegion.Width - 20, 40),
                WrapText = true,
                Text = "Tyria Search's settings are in their own window. You can also open it by clicking the Tyria Search icon at the top left of the screen.",
            };
            var button = new StandardButton { Parent = buildPanel, Location = new Point(10, 56), Width = 180, Text = "Open Settings Window" };
            button.Click += (_, __) => _open();
        }
    }
}
