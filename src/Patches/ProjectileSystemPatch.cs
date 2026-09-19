using System.Collections.Generic;
using ExtrasensoryPerception.API;
using ExtrasensoryPerception.ESP;
using ExtrasensoryPerception.Utils;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace ExtrasensoryPerception.Patches;

[HarmonyPatch(typeof(ProjectileSystem_Spawn_Client), nameof(ProjectileSystem_Spawn_Client.OnUpdate))]
public class ProjectileSystemPatch
{
    private static readonly Dictionary<PrefabGUID, float> RegisteredSpeeds = [];
    internal static float ProjectileSpeed => RegisteredSpeeds.GetValueOrDefault(AbilityCastStartedPatch.LastCast, 28f);

    [HarmonyPostfix]
    private static void Postfix(ProjectileSystem_Spawn_Client __instance)
    {
        var localCharacter = EntityList.LocalCharacter;
        __instance._MainQuery1.ForEach(entity =>
        {
            if (!entity.TryGetComponent<EntityOwner>(out var ownerComp)) return;
            var owner = ownerComp.Owner;

            if (entity.IsAlly(localCharacter) && (owner == Entity.Null || owner == localCharacter))
            {
                if (!RegisteredSpeeds.ContainsKey(AbilityCastStartedPatch.LastCast))
                    RegisteredSpeeds.Add(AbilityCastStartedPatch.LastCast, entity.Read<Projectile>().Speed);
                return;
            }

            // Fallback for weapon skills whose AbilityCastStarted may not replicate (e.g. Twinblade Javelin).
            if (owner != Entity.Null && owner.Exists() && owner.IsPlayer() && owner != localCharacter)
            {
                var spawnGuid = entity.GetPrefabGuid();
                EnemyCooldownTracker.OnWeaponProjectileObserved(owner, spawnGuid);
                if (Config.Extras.AutoCounter.Enabled || Config.Extras.AutoCounterDebugLog.Value)
                    AutoCounter.OnEnemyCommitSpawnObserved(owner, spawnGuid);
            }
        });
    }
}

[HarmonyPatch(typeof(AbilityCastStarted_SetupAbilityTargetSystem_Shared), nameof(AbilityCastStarted_SetupAbilityTargetSystem_Shared.OnUpdate))]
public class AbilityCastStartedPatch
{
    internal static PrefabGUID LastCast;

    [HarmonyPostfix]
    private static void Postfix(AbilityCastStarted_SetupAbilityTargetSystem_Shared __instance)
    {
        var logMode = Config.Extras.LogAbilityCasts.Value;
        var localCharacter = EntityList.LocalCharacter;

        __instance._Query.ForEach(entity =>
        {
            if (!entity.TryGetComponent<AbilityCastStartedEvent>(out var castEvent)) return;

            var abilityGuid = castEvent.Ability.GetPrefabGuid();
            var abilityGroupGuid = castEvent.AbilityGroup.GetPrefabGuid();
            var isLocal = castEvent.Character == localCharacter;

            if (isLocal)
            {
                LastCast = abilityGuid;
                AutoCounter.OnLocalProtectiveCastStarted(abilityGroupGuid);
            }
            else if (castEvent.Character.IsPlayer())
            {
                EnemyCooldownTracker.OnAbilityCastObserved(
                    castEvent.Character, abilityGroupGuid, abilityGuid,
                    AbilityRunScriptsShared.GetSlotIndex(VWorld.EntityManager, castEvent.Character, castEvent.AbilityGroup));
                AutoCounter.OnEnemyAbilityCastObserved(castEvent.Character, abilityGroupGuid, abilityGuid, castEvent.Ability);
            }

            if (logMode <= 0) return;
            if (logMode == 1 && !isLocal) return;

            var abilityName = VWorld.PrefabLookupMap.GetName(abilityGuid);
            var abilityGroupName = VWorld.PrefabLookupMap.GetName(abilityGroupGuid);
            var slotIndex = AbilityRunScriptsShared.GetSlotIndex(VWorld.EntityManager, castEvent.Character, castEvent.AbilityGroup);
            var casterName = castEvent.Character.TryGetComponent<PlayerCharacter>(out var playerCharacter)
                ? playerCharacter.Name.ToString()
                : castEvent.Character.GetName();
            var origin = isLocal ? "LOCAL" : (castEvent.Character.IsPlayer() ? "PLAYER" : "NPC");

            Plugin.Logger.LogInfo(
                $"AbilityCast [{origin}] caster='{casterName}' ability='{abilityName}' guidHash={abilityGuid.GuidHash} " +
                $"group='{abilityGroupName}' groupHash={abilityGroupGuid.GuidHash} slot={slotIndex}");
        });
    }
}
