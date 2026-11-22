using System;
using System.ComponentModel;
using System.Linq;
using Blish_HUD.Settings;

namespace mfdavies7.ModuleShared
{
    /// <summary>
    /// Greys out settings that only matter while a condition holds, e.g.
    /// <c>new SettingsUsedWhen(() => _x.Value, deps...).On(_x)</c>. Dispose it in the module's Unload.
    /// </summary>
    internal sealed class SettingsUsedWhen : IDisposable
    {
        /// <summary>
        /// Raised when <see cref="SetDisabled"/> changes a setting's disabled state. Blish HUD only reads that state when a
        /// setting's view is built, so <see cref="SettingsWithValuesView"/> rebuilds the setting's row on this.
        /// </summary>
        public static event Action<SettingEntry> DisabledChanged;

        /// <summary>Disables or enables a setting in the UI, updating any open <see cref="SettingsWithValuesView"/>.</summary>
        public static void SetDisabled(SettingEntry entry, bool disabled)
        {
            if (IsDisabled(entry) == disabled) return;
            entry.SetDisabled(disabled);
            DisabledChanged?.Invoke(entry);
        }

        public static bool IsDisabled(SettingEntry entry) =>
            entry.GetComplianceRequisite().OfType<SettingDisabledComplianceRequisite>().Any(r => r.Disabled);

        private readonly Func<bool> _condition;
        private readonly SettingEntry[] _dependents;
        private SettingEntry[] _triggers = new SettingEntry[0];

        /// <param name="condition">While this is false, <paramref name="dependents"/> are disabled.</param>
        public SettingsUsedWhen(Func<bool> condition, params SettingEntry[] dependents)
        {
            _condition = condition;
            _dependents = dependents;
        }

        /// <summary>Re-checks the condition whenever any of these settings changes (and once now).</summary>
        public SettingsUsedWhen On(params SettingEntry[] triggers)
        {
            foreach (var trigger in _triggers) trigger.PropertyChanged -= Trigger_PropertyChanged;
            _triggers = triggers;
            foreach (var trigger in _triggers) trigger.PropertyChanged += Trigger_PropertyChanged;
            Apply();
            return this;
        }

        private void Trigger_PropertyChanged(object sender, PropertyChangedEventArgs e) => Apply();

        private void Apply()
        {
            bool used = _condition();
            foreach (var dependent in _dependents) SetDisabled(dependent, !used);
        }

        public void Dispose()
        {
            foreach (var trigger in _triggers) trigger.PropertyChanged -= Trigger_PropertyChanged;
            _triggers = new SettingEntry[0];
        }
    }
}
