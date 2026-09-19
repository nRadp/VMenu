using ExtrasensoryPerception.API;
using ExtrasensoryPerception.ESP;
using ExtrasensoryPerception.Utils;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace ExtrasensoryPerception.Patches;

/// <summary>
/// Observes cast windup commit (<see cref="AbilityPreCastFinishedEvent"/>) and interrupt
/// (<see cref="AbilityInterruptedEvent"/> / <see cref="AbilityPreCastEndedEvent.WasInterrupted"/>)
/// so AutoCounter can wait until Sword E (and similar) actually leave the weapon — and skip
/// presses when the cast is cancelled mid-windup.
/// </summary>
[HarmonyPatch(typeof(AbilityModifyRotationDuringCastSystem_Shared),
    nameof(AbilityModifyRotationDuringCastSystem_Shared.OnUpdate))]
public static class AbilityCastCommitPatch
{
    [HarmonyPostfix]
    private static void Postfix(AbilityModifyRotationDuringCastSystem_Shared __instance)
    {
        if (!Config.Extras.AutoCounter.Enabled && !Config.Extras.AutoCounterDebugLog.Value)
            return;

        var local = EntityList.LocalCharacter;
        ProcessPreCastFinished(__instance._AbilityPreCastFinishedEvents, local);
        ProcessPreCastEnded(__instance._AbilityPreCastEndedEvents, local);
    }

    private static void ProcessPreCastFinished(EntityQuery query, Entity local)
    {
        query.ForEach(entity =>
        {
            if (!entity.TryGetComponent<AbilityPreCastFinishedEvent>(out var ev)) return;
            if (ev.Character == Entity.Null || ev.Character == local) return;
            if (!ev.Character.IsPlayer()) return;

            var castGuid = ev.Ability.GetPrefabGuid();
            var groupGuid = ev.AbilityGroup.GetPrefabGuid();
            AutoCounter.OnEnemyAbilityCastCommitted(ev.Character, groupGuid, castGuid);
        });
    }

    private static void ProcessPreCastEnded(EntityQuery query, Entity local)
    {
        query.ForEach(entity =>
        {
            if (!entity.TryGetComponent<AbilityPreCastEndedEvent>(out var ev)) return;
            if (!ev.WasInterrupted) return;
            if (ev.Character == Entity.Null || ev.Character == local) return;
            if (!ev.Character.IsPlayer()) return;

            var castGuid = ev.Ability.GetPrefabGuid();
            var groupGuid = ev.AbilityGroup.GetPrefabGuid();
            AutoCounter.OnEnemyAbilityCastInterrupted(ev.Character, groupGuid, castGuid);
        });
    }
}

/// <summary>
/// Secondary interrupt path via <see cref="AbilityInterruptedEvent"/> (movement cast system).
/// Covers cases where PreCastEnded is not emitted for the interrupted ability.
/// </summary>
[HarmonyPatch(typeof(AbilityModifyMovementDuringCastSystem_Shared),
    nameof(AbilityModifyMovementDuringCastSystem_Shared.OnUpdate))]
public static class AbilityCastInterruptPatch
{
    [HarmonyPostfix]
    private static void Postfix(AbilityModifyMovementDuringCastSystem_Shared __instance)
    {
        if (!Config.Extras.AutoCounter.Enabled && !Config.Extras.AutoCounterDebugLog.Value)
            return;

        var local = EntityList.LocalCharacter;
        __instance._InterruptedQuery.ForEach(entity =>
        {
            if (!entity.TryGetComponent<AbilityInterruptedEvent>(out var ev)) return;
            if (ev.Character == Entity.Null || ev.Character == local) return;
            if (!ev.Character.IsPlayer()) return;

            var castGuid = ev.Ability.GetPrefabGuid();
            var groupGuid = ev.AbilityGroup.GetPrefabGuid();
            AutoCounter.OnEnemyAbilityCastInterrupted(ev.Character, groupGuid, castGuid);
        });
    }
}
