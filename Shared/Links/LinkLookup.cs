using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Blish_HUD;
using Gw2Sharp.ChatLinks;
using Gw2Sharp.WebApi;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// The language names are looked up in when chat gives a chat code rather than the name. Blish HUD Language follows
    /// Blish HUD's language setting; the game doesn't tell Blish HUD its own.
    /// </summary>
    public enum BackupLanguage
    {
        [Description("Blish HUD Language")] BlishHudLanguage,
        [Description("English")] English,
        [Description("German")] German,
        [Description("French")] French,
        [Description("Spanish")] Spanish,
        [Description("Chinese")] Chinese,
    }

    internal enum LinkKind { Item, Skin, Skill, Trait, Recipe, Outfit, Other }

    /// <summary>What a chat link points to, with its name from the API (null if it couldn't be looked up).</summary>
    internal sealed class LinkedThing
    {
        public string ChatCode { get; set; }
        public LinkKind Kind { get; set; }
        public int Id { get; set; }
        /// <summary>Items only: how many the link is for (1 for a single item).</summary>
        public int Quantity { get; set; } = 1;
        public string Name { get; set; }
        public string IconUrl { get; set; }
        /// <summary>Items only: Junk, Basic, Fine, Masterwork, Rare, Exotic, Ascended or Legendary.</summary>
        public string Rarity { get; set; }

        /// <summary>The game's colour for the item's rarity, as in its tooltips; the hint's gold for anything else.</summary>
        public Color NameColor => Rarity switch
        {
            "Junk" => new Color(0xAA, 0xAA, 0xAA),
            "Basic" => Color.White,
            "Fine" => new Color(0x62, 0xA4, 0xDA),
            "Masterwork" => new Color(0x1A, 0x93, 0x06),
            "Rare" => new Color(0xFC, 0xD0, 0x0B),
            "Exotic" => new Color(0xFF, 0xA4, 0x05),
            "Ascended" => new Color(0xFB, 0x3E, 0x8D),
            "Legendary" => new Color(0xA0, 0x2E, 0xF7),
            _ => ContentService.Colors.Chardonnay,
        };
    }

    /// <summary>
    /// Looks up what a chat link (as GW2 copies it out of chat, "[&amp;AgH1WQAA]") points to, through the GW2 API. One request
    /// per link, kept for the session, so it stays far inside the API's rate limit.
    /// </summary>
    internal sealed class LinkLookup : IDisposable
    {
        private static readonly Logger Log = Logger.GetLogger(typeof(LinkLookup));

        private readonly HttpClient _http;
        private readonly ConcurrentDictionary<string, LinkedThing> _cache = new ConcurrentDictionary<string, LinkedThing>();

        /// <param name="product">The module's name without spaces and its version, for the User-Agent ("CopyItemName/1.0.0").</param>
        public LinkLookup(string product)
        {
            _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{product} (Blish HUD module; +https://github.com/mfdavies7/BlishHud-Modules)");
        }

        /// <summary>The API's code for the language; Blish HUD Language follows Blish HUD's language setting.</summary>
        public static string LanguageCode(BackupLanguage language)
        {
            if (language == BackupLanguage.BlishHudLanguage)
            {
                language = GameService.Overlay.UserLocale.Value switch
                {
                    Locale.German => BackupLanguage.German,
                    Locale.French => BackupLanguage.French,
                    Locale.Spanish => BackupLanguage.Spanish,
                    Locale.Chinese => BackupLanguage.Chinese,
                    _ => BackupLanguage.English,
                };
            }
            return language switch
            {
                BackupLanguage.German => "de",
                BackupLanguage.French => "fr",
                BackupLanguage.Spanish => "es",
                BackupLanguage.Chinese => "zh",
                _ => "en",
            };
        }

        /// <summary>What the chat code is, without looking anything up. Null if it isn't a chat code.</summary>
        public static LinkedThing Parse(string chatCode)
        {
            if (chatCode == null || !Gw2ChatLink.TryParse(chatCode, out IGw2ChatLink link)) return null;
            var thing = new LinkedThing { ChatCode = chatCode, Kind = LinkKind.Other };
            switch (link)
            {
                case ItemChatLink item:
                    thing.Kind = LinkKind.Item;
                    thing.Id = item.ItemId;
                    thing.Quantity = Math.Max(1, (int)item.Quantity);
                    break;
                case SkinChatLink skin: thing.Kind = LinkKind.Skin; thing.Id = skin.SkinId; break;
                case SkillChatLink skill: thing.Kind = LinkKind.Skill; thing.Id = skill.SkillId; break;
                case TraitChatLink trait: thing.Kind = LinkKind.Trait; thing.Id = trait.TraitId; break;
                case RecipeChatLink recipe: thing.Kind = LinkKind.Recipe; thing.Id = recipe.RecipeId; break;
                case OutfitChatLink outfit: thing.Kind = LinkKind.Outfit; thing.Id = outfit.OutfitId; break;
            }
            return thing;
        }

        /// <summary>
        /// The chat code with its name filled in, in the language. Recipes are named after the item they make. Throws
        /// <see cref="AbortException"/> if the name couldn't be looked up.
        /// </summary>
        public async Task<LinkedThing> LookUpAsync(LinkedThing thing, BackupLanguage language)
        {
            if (thing.Kind == LinkKind.Other) return thing;
            string lang = LanguageCode(language);
            string key = $"{thing.Kind}:{thing.Id}:{lang}";
            if (_cache.TryGetValue(key, out var known)) return WithQuantity(known, thing);

            var found = new LinkedThing { ChatCode = thing.ChatCode, Kind = thing.Kind, Id = thing.Id };
            try
            {
                JObject json;
                switch (thing.Kind)
                {
                    case LinkKind.Recipe:
                        var recipe = await GetAsync($"recipes/{thing.Id}", lang);
                        int output = (int?)recipe["output_item_id"] ?? 0;
                        json = await GetAsync($"items/{output}", lang);
                        break;
                    default:
                        json = await GetAsync($"{Endpoint(thing.Kind)}/{thing.Id}", lang);
                        break;
                }
                found.Name = (string)json["name"];
                found.IconUrl = (string)json["icon"];
                found.Rarity = (string)json["rarity"];
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404"))
            {
                throw new AbortException("The API doesn't know that one.");
            }
            catch (Exception ex) when (!(ex is AbortException))
            {
                Log.Warn(ex, $"Couldn't look up {thing.Kind} {thing.Id}.");
                Blocked.Check(ex);
                throw new AbortException("Couldn't look up its name. Try again.");
            }

            if (string.IsNullOrWhiteSpace(found.Name)) throw new AbortException("It has no name in the API.");
            _cache[key] = found;
            return WithQuantity(found, thing);
        }

        private static LinkedThing WithQuantity(LinkedThing named, LinkedThing linked) => new LinkedThing
        {
            ChatCode = linked.ChatCode,
            Kind = named.Kind,
            Id = named.Id,
            Quantity = linked.Quantity,
            Name = named.Name,
            IconUrl = named.IconUrl,
            Rarity = named.Rarity,
        };

        private static string Endpoint(LinkKind kind) => kind switch
        {
            LinkKind.Item => "items",
            LinkKind.Skin => "skins",
            LinkKind.Skill => "skills",
            LinkKind.Trait => "traits",
            LinkKind.Outfit => "outfits",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        private async Task<JObject> GetAsync(string path, string lang)
        {
            using (var response = await _http.GetAsync($"https://api.guildwars2.com/v2/{path}?lang={lang}"))
            {
                if ((int)response.StatusCode == 404) throw new HttpRequestException("404");
                response.EnsureSuccessStatusCode();
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
