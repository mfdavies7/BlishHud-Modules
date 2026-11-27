using System;
using System.Runtime.CompilerServices;
using Blish_HUD.Settings;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// Whole-number slider settings that remember their default, which Blish HUD doesn't keep, so
    /// <see cref="SettingsWithValuesView"/> can give them a Default button.
    /// </summary>
    internal static class SliderSettings
    {
        private static readonly ConditionalWeakTable<SettingEntry<int>, object> Defaults = new ConditionalWeakTable<SettingEntry<int>, object>();

        /// <summary>Defines a whole-number setting shown as a slider from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public static SettingEntry<int> DefineSlider(this SettingCollection settings, string key, int defaultValue,
            Func<string> displayName, Func<string> description, int min, int max)
        {
            var entry = settings.DefineSetting(key, defaultValue, displayName, description);
            entry.SetRange(min, max);
            Defaults.Remove(entry);
            Defaults.Add(entry, defaultValue);
            return entry;
        }

        /// <summary>The default the setting was defined with, or null if it wasn't defined by <see cref="DefineSlider"/>.</summary>
        public static int? DefaultOf(SettingEntry<int> entry) => Defaults.TryGetValue(entry, out object value) ? (int)value : (int?)null;
    }
}
