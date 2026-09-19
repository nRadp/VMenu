using System;
using System.Collections.Generic;
using ExtrasensoryPerception.API;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ExtrasensoryPerception.Utils;

/// <summary>
/// Resolves ability-group PrefabGUID hashes to the same <see cref="Sprite"/> the spellbook/
/// hotbar uses, via <see cref="GameDataSystem.ManagedDataRegistry"/> →
/// <see cref="ManagedAbilityGroupData.Icon"/>. Drawn through <c>IconOverlay</c>.
/// </summary>
internal static class AbilityIconResolver
{
    private static readonly Dictionary<int, Sprite> Cache = new();
    // Keep sprites rooted so Il2CppInterop wrappers aren't GC'd between frames.
    private static readonly List<Sprite> RootedSprites = [];
    private static readonly HashSet<int> MissCache = new();

    internal static bool TryGetSprite(int abilityHash, out Sprite sprite)
    {
        sprite = null!;

        if (Cache.TryGetValue(abilityHash, out var cached))
        {
            if (IsAlive(cached))
            {
                sprite = cached;
                return true;
            }

            Cache.Remove(abilityHash);
            MissCache.Remove(abilityHash);
        }

        if (MissCache.Contains(abilityHash)) return false;

        if (!TryResolveFromRegistry(abilityHash, out sprite))
            return false;

        RootedSprites.Add(sprite);
        Cache[abilityHash] = sprite;
        return true;
    }

    internal static void Reset()
    {
        Cache.Clear();
        RootedSprites.Clear();
        MissCache.Clear();
    }

    private static bool TryResolveFromRegistry(int abilityHash, out Sprite sprite)
    {
        sprite = null!;
        try
        {
            var world = VWorld.Game;
            if (!world.IsCreated) return false;

            var gameData = world.GetExistingSystemManaged<GameDataSystem>();
            if (gameData == null) return false;

            var registry = gameData.ManagedDataRegistry;
            if (registry == null) return false;

            var guid = new PrefabGUID(abilityHash);
            var data = registry.GetOrDefault<ManagedAbilityGroupData>(guid, null);
            if (data == null)
            {
                // Confirmed registry miss — don't keep retrying every frame.
                MissCache.Add(abilityHash);
                return false;
            }

            if (data.Icon == null || !IsAlive(data.Icon))
            {
                MissCache.Add(abilityHash);
                return false;
            }

            sprite = data.Icon;
            Plugin.Logger.LogInfo($"AbilityIconResolver: hash={abilityHash} sprite='{sprite.name}'");
            return true;
        }
        catch (Exception e)
        {
            // World/systems may not be ready yet — leave MissCache alone so we retry.
            Plugin.Logger.LogWarning($"AbilityIconResolver: lookup failed for {abilityHash}: {e.Message}");
            return false;
        }
    }

    private static bool IsAlive(Object? obj)
    {
        if (obj == null) return false;
        try
        {
            _ = obj.name;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
