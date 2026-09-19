using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExtrasensoryPerception.Utils.Prefabs;

// Modified version of:
// https://github.com/CrimsonMods/VAMP/blob/master/Utilities/ItemUtil.cs
// by SkyTech6
public static class Items
{
    private static readonly Regex CamelCaseRegex = new("(?<=[a-z]|[0-9])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex TierRegex = new(@"_?T\d{2}", RegexOptions.Compiled);
    private static readonly Dictionary<string, string> CleanNameCache = new(StringComparer.Ordinal);

    public static string CleanName(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        if (CleanNameCache.TryGetValue(input, out var cachedName)) return cachedName;

        try
        {
            string? result;

            // Check for Nether Shards first
            var netherShardName = GetNetherShardName(input);
            if (netherShardName != null)
            {
                CleanNameCache[input] = netherShardName;
                return netherShardName;
            }

            // Check for Gems
            var gemName = GetGemName(input);
            if (gemName != null)
            {
                CleanNameCache[input] = gemName;
                return gemName;
            }

            // Check for known prefixes
            string? transformationKey = null;
            Func<string, string>? transformation = null;
            foreach (var candidate in PrefabTransformations)
            {
                if (!input.Contains(candidate.Key)) continue;

                transformationKey = candidate.Key;
                transformation = candidate.Value;
                break;
            }

            if (transformation is not null && transformationKey is not null)
            {
                try
                {
                    result = transformation(input);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"Failed to transform item name '{input}' using transformation '{transformationKey}': {ex.Message}");
                    // Fallback to simple replacement
                    result = input.Replace(transformationKey, "").Replace("_", " ");
                }
            }
            else
            {
                // Default fallback — join non-empty segments after the first underscore
                var segments = input.Split('_');
                if (segments.Length > 1)
                {
                    var sb = new System.Text.StringBuilder();
                    for (var i = 1; i < segments.Length; i++)
                    {
                        if (string.IsNullOrEmpty(segments[i])) continue;
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(segments[i]);
                    }
                    result = sb.Length > 0 ? sb.ToString() : input;
                }
                else
                {
                    result = input;
                }
            }

            var cleanName = CamelCaseRegex.Replace(result ?? throw new InvalidOperationException(), " $1").Trim();
            CleanNameCache[input] = cleanName;
            return cleanName;
        }
        catch (Exception)
        {
            CleanNameCache[input] = input;
            return input;
        }
    }

    private static readonly Dictionary<string, Func<string, string>> PrefabTransformations = new()
    {
        ["Item_Building_Plants_"] = s => s.Replace("Item_Building_Plants_", "")
            .Replace("_", " "),

        ["Item_Building_Sapling_"] = s => s.Replace("Item_Building_Sapling_", "")
            .Replace("_Seed", "")
            .Replace("_", " ") + " Sapling",

        ["Item_Building"] = s => s.Replace("Item_Building", "")
            .Replace("_", " "),

        ["Item_Consumable_Eat_"] = s => s.Replace("Item_Consumable_Eat_", "")
            .Replace("_", " "),

        ["Item_Consumable_Heart_"] = s => s.Replace("Item_Consumable_Heart_", "")
            .Split('_')
            .Last() + " Heart",

        ["Item_Consumable_"] = s => s.Replace("Item_Consumable_", "")
            .Replace("_", " "),

        ["Item_Cloak_"] = s => TierRegex.Replace(s.Replace("Item_Cloak_", ""), "")
            .Replace("_", " ") + " Cloak",

        ["Item_Elixir_"] = s => TierRegex.Replace(s.Replace("Item_Elixir_", ""), "")
            .Split("_")
            .Aggregate((type, _) => $"Elixir of the {type}"),

        ["Item_Headgear_"] = s => s.Replace("Item_Headgear_", "")
            .Replace("_", " "),

        ["Item_Ingredient_Mineral_"] = s => s.Replace("Item_Ingredient_Mineral_", "")
            .Replace("_", " "),

        ["Item_Ingredient_Coin_"] = s => s.Replace("Item_Ingredient_Coin_", "")
            .Replace("_", " ") + " Coin"
            .Replace("Royal", "Goldsun"),
        
        ["Item_Ingredient_Fish_"] = s => TierRegex.Replace(s.Replace("Item_Ingredient_Fish_", ""), ""),

        ["Item_Ingredient_Plant_"] = s => s.Replace("Item_Ingredient_Plant_", "")
            .Replace("_", " "),

        ["Item_Ingredient_Research_"] = s => s.Replace("Item_Ingredient_Research_", "")
            .Replace("_", " "),

        ["Item_Ingredient_Thread_"] = s => s.Replace("Item_Ingredient_Thread_", "")
            .Replace("_", " ") + " Thread",

        ["Item_Ingredient_Wood_"] = s => s.Replace("Item_Ingredient_Wood_", "").Replace("Standard", "") + " Wood",

        ["Item_Ingredient_"] = s => s.Replace("Item_Ingredient_", "")
            .Replace("_", " "),

        ["Item_Jewel_"] = s => s.Replace("Item_Jewel_", "").Split("_")
            .Aggregate((type, tier) => $"{type} Jewel {tier}"),

        ["Item_MagicSource_"] = GetMagicSourceName,

        ["Item_Weapon_"] = GetWeaponName
    };

    private static string? GetNetherShardName(string? input)
    {
        return input switch
        {
            not null when input.Contains("NetherShard_T01") => "Stygian Shard",
            not null when input.Contains("NetherShard_T02") => "Greater Stygian Shard",
            not null when input.Contains("NetherShard_T03") => "Primal Stygian Shard",
            _ => null
        };
    }

    private static string? GetGemName(string? input)
    {
        if (input == null || !input.Contains("Gem")) return null;
        var parts = input.Split('_');
        if (parts is { Length: <= 3 }) return null;

        var gemType = parts[3];

        return input switch
        {
            not null when input.Contains("_T01") => "Crude " + gemType,
            not null when input.Contains("_T02") => "Regular " + gemType,
            not null when input.Contains("_T03") => "Flawless " + gemType,
            not null when input.Contains("_T04") => "Perfect " + gemType,
            _ => null
        };
    }

    private static string GetWeaponName(string input)
    {
        if (!input.Contains("Unique"))
        {
            var parts = TierRegex.Replace(input.Replace("Item_Weapon_", ""), "").Split('_');
            Array.Reverse(parts);
            return string.Join(" ", parts);
        }

        var prefix = input.Contains("Shattered") ? "[Shard] " : "";
        return input switch
        {
            not null when input.Contains("Axe") => prefix + "The Red Twins",
            not null when input.Contains("Claws") => prefix + "Talons of the Lich Beast",
            not null when input.Contains("Crossbow") => prefix + "The Siren's Wail",
            not null when input.Contains("Daggers") => prefix + "The Fate Dancers",
            not null when input.Contains("GreatSword") => prefix + "Apocalypse",
            not null when input.Contains("Longbow") => prefix + "Oaksong",
            not null when input.Contains("Mace") => prefix + "Hand of Winter",
            not null when input.Contains("Pistols") => prefix + "The Endbringers",
            not null when input.Contains("Reaper_") => prefix + "Mortira's Lament",
            not null when input.Contains("Slashers_Unique_T08_Variation01") => prefix + "Cloud Dancers",
            not null when input.Contains("Slashers_Unique_T08_Variation02") => prefix + "Wings of the Fallen",
            not null when input.Contains("Spear") => prefix + "The Thousand Storms",
            not null when input.Contains("Sword") => prefix + "The Gravecaller",
            not null when input.Contains("TwinBlades") => prefix + "The Wraithblades",
            not null when input.Contains("Whip") => prefix + "The Morning Star",
            _ => "Unknown"
        };
    }

    private static string GetMagicSourceName(string input)
    {
        if (input.Contains("SoulShard"))
            return $"Soul Shard of {input.Replace("Item_MagicSource_SoulShard_", "").Replace("Manticore", "the Winged Horror").Replace("Monster", "the Monster")}";

        return input switch
        {
            not null when input.Contains("Duskwatcher") => "Ring of the Duskwatcher",
            not null when input.Contains("EmberChain") => "Ring of the Dawnrunner",
            not null when input.Contains("FrozenEye") => "Ring of the Warlock",
            not null when input.Contains("MistSignet") => "Ring of the Spellweaver",
            not null when input.Contains("RubyRing") => "Ring of the Warrior",
            not null when input.Contains("SorcererRing") => "Ring of the Sorcerer",
            not null when input.Contains("Relic") => "Scourgestone Pendant",
            not null when input.Contains("AmethystPendant") => "Pendant of the Sorcerer",
            not null when input.Contains("EmeraldNecklace") => "Pendant of the Dawnrunner",
            not null when input.Contains("MistStoneNecklace") => "Pendant of the Spellweaver",
            not null when input.Contains("RubyPendant") => "Pendant of the Warrior",
            not null when input.Contains("SapphirePendant") => "Pendant of the Warlock",
            not null when input.Contains("TopazAmulet") => "Pendant of the Duskwatcher",
            not null when input.Contains("BloodwineAmulet") => "Blood Merlot Amulet",
            not null when input.Contains("Blood") => "Amulet of the Crimson Commander",
            not null when input.Contains("Chaos") => "Amulet of the Wicked Prophet",
            not null when input.Contains("Frost") => "Amulet of the Arch-Warlock",
            not null when input.Contains("Illusion") => "Amulet of the Master Spellweaver",
            not null when input.Contains("Storm") => "Amulet of the Blademaster",
            not null when input.Contains("Unholy") => "Amulet of the Unyielding Charger",
            not null => TierRegex.Replace(input.Replace("Item_MagicSource_General_", ""), ""),
            _ => "Unknown"
        };
    }
}
