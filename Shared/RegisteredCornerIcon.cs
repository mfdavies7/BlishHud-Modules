using System;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework.Graphics;

namespace mfdavies7.ModuleShared
{
    /// <summary>
    /// The module's corner icon, registered with Blish HUD only while Register Corner Icon is on (as Event Table does):
    /// turning it off removes the icon rather than hiding it, and disables the click action settings.
    /// </summary>
    internal sealed class RegisteredCornerIcon : IDisposable
    {
        private readonly SettingEntry<bool> _register;
        private readonly SettingEntry[] _clickSettings;
        private readonly Texture2D _texture;
        private readonly Action _leftClick;
        private readonly Action _rightClick;
        private readonly bool _rightClickOnRelease;

        /// <summary>The icon while it's registered; null otherwise.</summary>
        public CornerIcon Icon { get; private set; }

        /// <param name="rightClickOnRelease">Run the right-click action when the button is released rather than pressed.</param>
        /// <param name="clickSettings">Settings that only matter with the icon registered; disabled while it isn't.</param>
        public RegisteredCornerIcon(SettingEntry<bool> register, Texture2D texture, Action leftClick, Action rightClick,
            bool rightClickOnRelease, params SettingEntry[] clickSettings)
        {
            _register = register;
            _texture = texture;
            _leftClick = leftClick;
            _rightClick = rightClick;
            _rightClickOnRelease = rightClickOnRelease;
            _clickSettings = clickSettings;

            _register.SettingChanged += Register_SettingChanged;
            Apply(_register.Value);
        }

        private void Register_SettingChanged(object sender, ValueChangedEventArgs<bool> e) => Apply(e.NewValue);

        private void Apply(bool register)
        {
            foreach (var setting in _clickSettings) setting.SetDisabled(!register);

            if (register && Icon == null)
            {
                Icon = new CornerIcon { Icon = _texture }; // no name, so no tooltip
                Icon.Click += Icon_Click;
                if (_rightClickOnRelease) Icon.RightMouseButtonReleased += Icon_RightClick;
                else Icon.RightMouseButtonPressed += Icon_RightClick;
            }
            else if (!register && Icon != null)
            {
                RemoveIcon();
            }
        }

        private void RemoveIcon()
        {
            Icon.Click -= Icon_Click;
            Icon.RightMouseButtonReleased -= Icon_RightClick;
            Icon.RightMouseButtonPressed -= Icon_RightClick;
            Icon.Dispose();
            Icon = null;
        }

        private void Icon_Click(object sender, MouseEventArgs e) => _leftClick();

        private void Icon_RightClick(object sender, MouseEventArgs e) => _rightClick();

        public void Dispose()
        {
            _register.SettingChanged -= Register_SettingChanged;
            if (Icon != null) RemoveIcon();
        }
    }
}
