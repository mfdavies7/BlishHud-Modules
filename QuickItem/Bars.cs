using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blish_HUD;
using Bytex.ModuleShared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bytex.QuickItem
{
    /// <summary>An item on a bar: what it is (from the API) and its optional hotkey. Or a built-in action instead.</summary>
    public sealed class BarItem
    {
        /// <summary><see cref="Action"/> for pressing the inventory's Deposit All Materials button.</summary>
        public const string DepositMaterials = "deposit-materials";
        public const string DepositMaterialsName = "Deposit All Materials";

        [JsonProperty("id")] public int ItemId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>The icon's render service URL; its file name is the icon's asset id. Null for an action.</summary>
        [JsonProperty("icon")] public string Icon { get; set; }
        [JsonProperty("rarity")] public string Rarity { get; set; }
        /// <summary>A built-in action (<see cref="DepositMaterials"/>) instead of an item; null for an item.</summary>
        [JsonProperty("action", NullValueHandling = NullValueHandling.Ignore)] public string Action { get; set; }
        /// <summary>The hotkey's key (Microsoft.Xna.Framework.Input.Keys), 0 for none.</summary>
        [JsonProperty("key")] public int Key { get; set; }
        /// <summary>The hotkey's modifiers (Microsoft.Xna.Framework.Input.ModifierKeys).</summary>
        [JsonProperty("modifiers")] public int Modifiers { get; set; }

        /// <summary>The icon's asset id, or 0 if the URL doesn't end in one.</summary>
        [JsonIgnore]
        public int AssetId
        {
            get
            {
                string file = Icon == null ? null : Path.GetFileNameWithoutExtension(new Uri(Icon).AbsolutePath);
                return int.TryParse(file, out int id) ? id : 0;
            }
        }
    }

    /// <summary>A grid of items placed on screen.</summary>
    public sealed class Bar
    {
        public const int MinIconSize = 24, MaxIconSize = 80, IconSizeStep = 4, MaxColumns = 20;

        [JsonProperty("id")] public Guid Id { get; set; } = Guid.NewGuid();
        /// <summary>The layout the bar belongs to; null when it's in every layout.</summary>
        [JsonProperty("layout")] public Guid? Layout { get; set; }
        /// <summary>Top left, in Blish HUD's UI units.</summary>
        [JsonProperty("x")] public int X { get; set; }
        [JsonProperty("y")] public int Y { get; set; }
        [JsonProperty("columns")] public int Columns { get; set; } = 10;
        [JsonProperty("iconSize")] public int IconSize { get; set; } = 44;
        /// <summary>Shown only while this character is played; null on every character.</summary>
        [JsonProperty("character")] public string Character { get; set; }
        [JsonProperty("items")] public List<BarItem> Items { get; set; } = new List<BarItem>();

        /// <summary>A copy with a new id, its items copied too.</summary>
        public Bar Copy()
        {
            var copy = JsonConvert.DeserializeObject<Bar>(JsonConvert.SerializeObject(this));
            copy.Id = Guid.NewGuid();
            return copy;
        }
    }

    /// <summary>
    /// A named set of bars. One layout is shown at a time: the one chosen last, unless one is set to be used on the
    /// character being played, or on its profession.
    /// </summary>
    public sealed class Layout
    {
        [JsonProperty("id")] public Guid Id { get; set; } = Guid.NewGuid();
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>The key of the hotkey that switches to the layout (Microsoft.Xna.Framework.Input.Keys), 0 for none.</summary>
        [JsonProperty("key")] public int Key { get; set; }
        [JsonProperty("modifiers")] public int Modifiers { get; set; }
        /// <summary>Characters the layout is used on whenever they're played.</summary>
        [JsonProperty("characters")] public List<string> Characters { get; set; } = new List<string>();
        /// <summary>Professions (Gw2Sharp ProfessionType names) the layout is used as, unless a character's layout applies.</summary>
        [JsonProperty("professions")] public List<string> Professions { get; set; } = new List<string>();
    }

    /// <summary>The layouts and bars, saved as bars.json in the module's directory.</summary>
    public sealed class BarStore
    {
        private static readonly Logger Logger = Logger.GetLogger<BarStore>();
        private const int FileVersion = 2;

        private readonly string _path;

        public List<Layout> Layouts { get; private set; } = new List<Layout>();
        public List<Bar> Bars { get; private set; } = new List<Bar>();
        /// <summary>The layout chosen last, shown unless one set for the character or profession applies.</summary>
        public Guid ChosenLayout { get; set; }

        /// <summary>True when there was no saved file: the player hasn't set anything up yet.</summary>
        public bool IsNew { get; private set; }

        /// <summary>Raised after any change, once it's saved.</summary>
        public event Action Changed;

        private readonly FileWriter _writer;

        public BarStore(string directory)
        {
            _path = Path.Combine(directory, "bars.json");
            _writer = new FileWriter(_path);
        }

        private sealed class SavedFile
        {
            [JsonProperty("version")] public int Version { get; set; }
            [JsonProperty("chosenLayout")] public Guid ChosenLayout { get; set; }
            [JsonProperty("layouts")] public List<Layout> Layouts { get; set; }
            [JsonProperty("bars")] public List<Bar> Bars { get; set; }
        }

        public void Load()
        {
            if (!File.Exists(_path))
            {
                IsNew = true;
                Tidy();
                return;
            }
            bool migrated = false;
            try
            {
                var json = JToken.Parse(File.ReadAllText(_path));
                if (json is JArray)
                {
                    // Version 1 was just the bars. They become the first layout's.
                    Bars = json.ToObject<List<Bar>>() ?? new List<Bar>();
                    migrated = true;
                }
                else
                {
                    var saved = json.ToObject<SavedFile>();
                    Bars = saved?.Bars ?? new List<Bar>();
                    Layouts = saved?.Layouts ?? new List<Layout>();
                    ChosenLayout = saved?.ChosenLayout ?? Guid.Empty;
                }
            }
            catch (Exception ex)
            {
                // Keep the broken file rather than overwrite it on the next save.
                Logger.Warn(ex, "Couldn't read bars.json; starting with no bars. The file was copied to bars.json.bad.");
                try { File.Copy(_path, _path + ".bad", true); } catch (Exception) { }
                Bars = new List<Bar>();
                Layouts = new List<Layout>();
            }

            Tidy();
            if (migrated)
            {
                foreach (var bar in Bars) bar.Layout = Layouts[0].Id;
                Logger.Info("Moved the bars into the first layout.");
                Save();
            }
        }

        /// <summary>Puts right what an older or hand-edited file may have wrong; there's always at least one layout.</summary>
        private void Tidy()
        {
            Layouts = Layouts.Where(l => l != null).ToList();
            if (Layouts.Count == 0) Layouts.Add(new Layout { Name = "Main" });
            foreach (var layout in Layouts)
            {
                if (string.IsNullOrWhiteSpace(layout.Name)) layout.Name = "Layout";
                layout.Characters = layout.Characters?.Where(c => !string.IsNullOrEmpty(c)).ToList() ?? new List<string>();
                layout.Professions = layout.Professions?.Where(p => !string.IsNullOrEmpty(p)).ToList() ?? new List<string>();
            }
            if (Layouts.All(l => l.Id != ChosenLayout)) ChosenLayout = Layouts[0].Id;

            Bars = Bars.Where(b => b != null).ToList();
            foreach (var bar in Bars)
            {
                bar.Items = bar.Items?.Where(i => i != null).ToList() ?? new List<BarItem>();
                bar.Columns = Math.Max(1, Math.Min(Bar.MaxColumns, bar.Columns));
                bar.IconSize = Math.Max(Bar.MinIconSize, Math.Min(Bar.MaxIconSize, bar.IconSize));
                // A bar of a layout that's gone would never be seen again.
                if (bar.Layout != null && Layouts.All(l => l.Id != bar.Layout)) bar.Layout = ChosenLayout;
            }
        }

        public Layout LayoutById(Guid id) => Layouts.FirstOrDefault(l => l.Id == id);

        /// <summary>Whether the bar is shown in the layout: its own, or every layout's.</summary>
        public static bool InLayout(Bar bar, Layout layout) => bar.Layout == null || bar.Layout == layout?.Id;

        /// <summary>Saves, then tells the bars to redraw. Call after changing any layout, bar or item.</summary>
        public void Save()
        {
            try
            {
                var file = new SavedFile { Version = FileVersion, ChosenLayout = ChosenLayout, Layouts = Layouts, Bars = Bars };
                // Written in the background: this is called from clicks and drags.
                _writer.Write(JsonConvert.SerializeObject(file, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Couldn't save bars.json.");
            }
            Changed?.Invoke();
        }

        /// <summary>Writes a save still waiting, for Unload.</summary>
        public void Flush() => _writer.Flush();
    }
}
