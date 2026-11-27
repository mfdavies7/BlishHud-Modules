using System;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// The module's corner icon, registered with Blish HUD only while Register Corner Icon is on (as Event Table does):
    /// turning it off removes the icon rather than hiding it, and disables the click action settings.
    /// </summary>
    internal sealed class RegisteredCornerIcon : IDisposable
    {
        private readonly SettingEntry<bool> _register;
        private readonly SettingEntry[] _clickSettings;
        private Texture2D _texture;
        private Texture2D _hoverTexture;
        private readonly Action _leftClick;
        private readonly Action _rightClick;
        private readonly bool _rightClickOnRelease;
        private readonly string _name;
        private readonly int _priority;
        // The hotkey shown in the tooltip (see ShowKeybind), and the binding whose changes are being watched.
        private SettingEntry<KeyBinding> _keySetting;
        private KeyBinding _watchedBinding;

        /// <summary>The icon while it's registered; null otherwise.</summary>
        public CornerIcon Icon { get; private set; }

        /// <summary>
        /// Changes the icon's picture and its hover picture (drawn bigger and brighter, as Blish HUD's own logo / logo-big);
        /// kept while unregistered, so the icon comes back with the latest ones.
        /// </summary>
        public void SetTextures(Texture2D texture, Texture2D hoverTexture)
        {
            _texture = texture;
            _hoverTexture = hoverTexture;
            if (Icon == null) return;
            Icon.Icon = texture;
            Icon.HoverIcon = hoverTexture;
        }

        /// <summary>
        /// Shows a hotkey setting in the icon's tooltip, after the name in square brackets ("Wiki Search [Ctrl+W]") while
        /// it's bound, kept up to date as it changes. It goes in IconName, since Blish HUD sets the tooltip from that as the
        /// mouse moves; Blish Icon Manager knows a module's only icon by its module, so a changing name keeps its place.
        /// </summary>
        public void ShowKeybind(SettingEntry<KeyBinding> setting)
        {
            _keySetting = setting;
            setting.SettingChanged += KeySetting_Changed;
            WatchBinding(setting.Value);
            UpdateIconName();
        }

        private void KeySetting_Changed(object sender, ValueChangedEventArgs<KeyBinding> e)
        {
            WatchBinding(e.NewValue);
            UpdateIconName();
        }

        // Blish HUD's keybinding box changes the binding in place, raising BindingChanged rather than SettingChanged.
        private void WatchBinding(KeyBinding binding)
        {
            if (_watchedBinding != null) _watchedBinding.BindingChanged -= Binding_Changed;
            _watchedBinding = binding;
            if (binding != null) binding.BindingChanged += Binding_Changed;
        }

        private void Binding_Changed(object sender, EventArgs e) => UpdateIconName();

        private string IconName()
        {
            var binding = _keySetting?.Value;
            if (binding == null || binding.PrimaryKey == Keys.None) return _name;
            return $"{_name} [{binding.GetBindingDisplayText()}]";
        }

        private void UpdateIconName()
        {
            if (Icon == null) return;
            Icon.IconName = IconName();
            // Blish HUD copies IconName into the tooltip text on a mouse move only while the icon has no Tooltip, and
            // reading Tooltip makes one from the text, so once it has text it keeps the old name: set here (unless the
            // loading message is showing, which Blish HUD puts back to the name itself).
            string loading = Icon.LoadingMessage;
            if (string.IsNullOrEmpty(loading) || Icon.BasicTooltipText != loading) Icon.BasicTooltipText = Icon.IconName;
        }

        /// <param name="manifest">The module's manifest: its name is the icon's tooltip, and its namespace sets the icon's priority.</param>
        /// <param name="hoverTexture">The picture shown while hovered: the icon's, bigger and brighter.</param>
        /// <param name="rightClickOnRelease">Run the right-click action when the button is released rather than pressed.</param>
        /// <param name="clickSettings">Settings that only matter with the icon registered; disabled while it isn't.</param>
        public RegisteredCornerIcon(Manifest manifest, SettingEntry<bool> register, Texture2D texture, Texture2D hoverTexture, Action leftClick, Action rightClick,
            bool rightClickOnRelease, params SettingEntry[] clickSettings)
        {
            _name = manifest.Name;
            _priority = PriorityFor(manifest.Namespace);
            _register = register;
            _texture = texture;
            _hoverTexture = hoverTexture;
            _leftClick = leftClick;
            _rightClick = rightClick;
            _rightClickOnRelease = rightClickOnRelease;
            _clickSettings = clickSettings;

            _register.SettingChanged += Register_SettingChanged;
            Apply(_register.Value);
        }

        /// <summary>
        /// The same priority every session, from the module's namespace. Without one, Blish HUD uses the picture's hash code,
        /// which changes each session, so modules that remember icons by priority (Bag of Holding) lose track of them.
        /// Kept within ±1,000,000,000, clear of the values Blish HUD (int.MaxValue for its own icon) and Bag of Holding
        /// (its own icon, and int.MinValue + 1 for icons in the bag) use. Higher priorities sit further left in the row.
        /// </summary>
        public static int PriorityFor(string ns)
        {
            uint hash = 2166136261; // FNV-1a, unlike string.GetHashCode the same on every run
            foreach (char c in ns.ToLowerInvariant())
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)(hash % 2000000001) - 1000000000;
        }

        private void Register_SettingChanged(object sender, ValueChangedEventArgs<bool> e) => Apply(e.NewValue);

        private void Apply(bool register)
        {
            foreach (var setting in _clickSettings) SettingsUsedWhen.SetDisabled(setting, !register);

            if (register && Icon == null)
            {
                Icon = new CornerIcon { Icon = _texture, HoverIcon = _hoverTexture, IconName = IconName(), Priority = _priority };
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
            if (_keySetting != null) _keySetting.SettingChanged -= KeySetting_Changed;
            WatchBinding(null);
            if (Icon != null) RemoveIcon();
        }
    }
}
