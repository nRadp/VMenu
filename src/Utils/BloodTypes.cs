using System;
using System.Collections.Generic;
using ExtrasensoryPerception.API;
using Stunlock.Core;

namespace ExtrasensoryPerception.Utils;

internal static class BloodTypes
{
    private static readonly Dictionary<PrefabGUID, string> NameCache = [];

    internal static readonly string[] FilterOptions =
    [
        "All",
        "Brute",
        "Warrior",
        "Scholar",
        "Rogue",
        "Worker",
        "Creature",
        "Draculin",
        "Mutant"
    ];

    internal static string GetName(PrefabGUID prefabGuid)
    {
        if (NameCache.TryGetValue(prefabGuid, out var cachedName)) return cachedName;

        var bloodTypeName = VWorld.PrefabLookupMap.GetName(prefabGuid).Replace("BloodType_", "");
        NameCache[prefabGuid] = bloodTypeName;
        return bloodTypeName;
    }

    internal static bool MatchesFilter(string bloodTypeName, int filterIndex)
    {
        return filterIndex <= 0 ||
               filterIndex >= FilterOptions.Length ||
               string.Equals(FilterOptions[filterIndex], bloodTypeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compare blood quality against a UI percent threshold. Sliders are whole-number
    /// percents but can store floats like 90.4 while displaying "90" — round both sides.
    /// </summary>
    internal static bool MeetsQualityThreshold(float bloodQuality, float minimumQualityPercent) =>
        UnityEngine.Mathf.RoundToInt(bloodQuality) >= UnityEngine.Mathf.RoundToInt(minimumQualityPercent);
}
