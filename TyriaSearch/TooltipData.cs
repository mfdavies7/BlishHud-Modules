using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace mfdavies7.TyriaSearch
{
    // What result tooltips show of skills, traits and items, as the API gives it. Fetched live (see GameTooltips),
    // so none of this is in the cache files. Only the fields a tooltip shows; Json.NET skips the rest.

    public sealed class ApiSkill
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("icon")] public string Icon { get; set; }
        [JsonProperty("facts")] public List<ApiFact> Facts { get; set; }
        /// <summary>Revenant energy.</summary>
        [JsonProperty("cost")] public int? Cost { get; set; }
        /// <summary>Thief initiative.</summary>
        [JsonProperty("initiative")] public int? Initiative { get; set; }
    }

    public sealed class ApiTrait
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("icon")] public string Icon { get; set; }
        [JsonProperty("facts")] public List<ApiFact> Facts { get; set; }
    }

    /// <summary>One line of a skill or trait tooltip; which fields are set depends on <see cref="Type"/>.</summary>
    public sealed class ApiFact
    {
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("icon")] public string Icon { get; set; }
        /// <summary>A number, or true for StunBreak and Unblockable.</summary>
        [JsonProperty("value")] public JToken RawValue { get; set; }
        [JsonIgnore] public double? Value => RawValue?.Type == JTokenType.Integer || RawValue?.Type == JTokenType.Float ? RawValue.Value<double>() : (double?)null;
        [JsonProperty("target")] public string Target { get; set; }
        /// <summary>BuffConversion: the attribute converted from.</summary>
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("duration")] public double? Duration { get; set; }
        [JsonProperty("apply_count")] public int? ApplyCount { get; set; }
        [JsonProperty("hit_count")] public int? HitCount { get; set; }
        [JsonProperty("dmg_multiplier")] public double? DamageMultiplier { get; set; }
        [JsonProperty("distance")] public double? Distance { get; set; }
        [JsonProperty("percent")] public double? Percent { get; set; }
        [JsonProperty("field_type")] public string FieldType { get; set; }
        [JsonProperty("finisher_type")] public string FinisherType { get; set; }
        /// <summary>PrefixedBuff only: the buff shown before this one.</summary>
        [JsonProperty("prefix")] public ApiFact Prefix { get; set; }
    }

    public sealed class ApiItem
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("level")] public int Level { get; set; }
        [JsonProperty("rarity")] public string Rarity { get; set; }
        [JsonProperty("vendor_value")] public int VendorValue { get; set; }
        [JsonProperty("icon")] public string Icon { get; set; }
        [JsonProperty("flags")] public List<string> Flags { get; set; }
        [JsonProperty("restrictions")] public List<string> Restrictions { get; set; }
        [JsonProperty("details")] public ApiItemDetails Details { get; set; }
    }

    /// <summary>Type-specific item fields; which are set depends on the item's type.</summary>
    public sealed class ApiItemDetails
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("weight_class")] public string WeightClass { get; set; }
        [JsonProperty("defense")] public int Defense { get; set; }
        [JsonProperty("min_power")] public int MinPower { get; set; }
        [JsonProperty("max_power")] public int MaxPower { get; set; }
        [JsonProperty("infusion_slots")] public List<ApiInfusionSlot> InfusionSlots { get; set; }
        [JsonProperty("infix_upgrade")] public ApiInfixUpgrade InfixUpgrade { get; set; }
        [JsonProperty("suffix_item_id")] public int? SuffixItemId { get; set; }
        /// <summary>A string in the API, and empty when there's none.</summary>
        [JsonProperty("secondary_suffix_item_id")] public string SecondarySuffixItemId { get; set; }
        [JsonProperty("stat_choices")] public List<int> StatChoices { get; set; }
        /// <summary>Rune bonuses, by number of runes worn.</summary>
        [JsonProperty("bonuses")] public List<string> Bonuses { get; set; }
        // Consumables: the effect they give.
        [JsonProperty("duration_ms")] public long? DurationMs { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("icon")] public string Icon { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        /// <summary>Bags.</summary>
        [JsonProperty("size")] public int Size { get; set; }
        /// <summary>Salvage kits.</summary>
        [JsonProperty("charges")] public int Charges { get; set; }
    }

    public sealed class ApiInfusionSlot
    {
        [JsonProperty("flags")] public List<string> Flags { get; set; }
        [JsonProperty("item_id")] public int? ItemId { get; set; }
    }

    public sealed class ApiInfixUpgrade
    {
        [JsonProperty("attributes")] public List<ApiAttribute> Attributes { get; set; }
        [JsonProperty("buff")] public ApiBuff Buff { get; set; }
    }

    public sealed class ApiAttribute
    {
        [JsonProperty("attribute")] public string Attribute { get; set; }
        [JsonProperty("modifier")] public int Modifier { get; set; }
    }

    public sealed class ApiBuff
    {
        [JsonProperty("description")] public string Description { get; set; }
    }
}
