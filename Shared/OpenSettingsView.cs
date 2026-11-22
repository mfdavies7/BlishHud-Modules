using System;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework;

namespace mfdavies7.ModuleShared
{
    /// <summary>
    /// Blish HUD's settings page for the module: the settings are in the module's own window, so this is just a button,
    /// centred on the page, that opens it.
    /// </summary>
    internal sealed class OpenSettingsView : View
    {
        private readonly Action _open;
        private Container _panel;
        private StandardButton _button;

        public OpenSettingsView(Action open) => _open = open;

        protected override void Build(Container buildPanel)
        {
            _panel = buildPanel;
            _button = new StandardButton { Parent = buildPanel, Width = 180, Text = "Settings" };
            _button.Click += Button_Click;
            buildPanel.Resized += Panel_Resized;
            Center();
        }

        protected override void Unload()
        {
            if (_panel != null) _panel.Resized -= Panel_Resized;
            if (_button != null) _button.Click -= Button_Click;
            _panel = null;
            _button = null;
        }

        private void Button_Click(object sender, Blish_HUD.Input.MouseEventArgs e) => _open();

        private void Panel_Resized(object sender, ResizedEventArgs e) => Center();

        private void Center()
        {
            var region = _panel.ContentRegion;
            _button.Location = new Point((region.Width - _button.Width) / 2, (region.Height - _button.Height) / 2);
        }
    }
}
