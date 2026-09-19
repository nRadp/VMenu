using ExtrasensoryPerception.API;
using ExtrasensoryPerception.Utils;
using HarmonyLib;
using ProjectM;
using ProjectM.UI;
using Unity.Collections;
using Unity.Entities;

namespace ExtrasensoryPerception.Patches;

/// <summary>
/// Forces enemy player HUDs (name / level / HP bar) to remain visible at any distance
/// as long as the player entity is on screen.
///
/// The game's <see cref="CheckOnScreenSystem"/> sets <c>CheckOnScreen.IsOnScreen = false</c>
/// when the distance exceeds <c>MaxDistanceForHudAndFadeOut</c> — even though the 3-D model is
/// still rendered. Downstream, <c>GetCharacterHUDSystem.GetDataJob</c> skips HUD entries for
/// entities whose <c>IsOnScreen</c> is false.
///
/// This prefix runs just before <c>GetCharacterHUDSystem.OnUpdate</c> and overrides
/// <c>CheckOnScreen</c> for every <see cref="PlayerCharacter"/> entity so the distance
/// gate is effectively removed.
/// </summary>
[HarmonyPatch(typeof(GetCharacterHUDSystem), nameof(GetCharacterHUDSystem.OnUpdate))]
public static class PlayerHUDDistancePatch
{
    private static EntityQuery? _query;
    private static World? _queryWorld;

    [HarmonyPrefix]
    static void Prefix(GetCharacterHUDSystem __instance)
    {
        if (!Config.ESP.AlwaysShowPlayerHUD.Value) return;

        var em = VWorld.EntityManager;

        // Rebuild the query if the world was recreated (disconnect/reconnect).
        if (_query == null || _queryWorld != VWorld.Game)
        {
            _queryWorld = VWorld.Game;
            _query = em.CreateEntityQuery(
                ComponentType.ReadWrite<CheckOnScreen>(),
                ComponentType.ReadOnly<PlayerCharacter>(),
                ComponentType.ReadOnly<CharacterHUD>()
            );
        }

        var entities = _query.Value.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                // Skip admin observers — let them stay hidden.
                if (AdminObserveCheck.IsAdminObserver(entity)) continue;

                var check = em.GetComponentData<CheckOnScreen>(entity);

                // Force the HUD to be treated as on-screen regardless of distance.
                check.IsOnScreen = true;
                check.HasLineOfSight = true;

                // Push MaxDistanceForHudAndFadeOut to an effectively infinite value
                // so the fade-distance interpolation also stays at full opacity.
                check.MaxDistanceForHudAndFadeOut = 9999f;

                em.SetComponentData(entity, check);

                // Also clear Hideable so the LOS/cover check doesn't hide the HUD.
                if (em.HasComponent<Hideable>(entity))
                {
                    var hideable = em.GetComponentData<Hideable>(entity);
                    hideable.IsHidden = false;
                    hideable.Visibility = 1f;
                    em.SetComponentData(entity, hideable);
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}

/// <summary>
/// Strips player entities from the hide-HUD-buffs list so that stealth/camouflage
/// buffs (<see cref="HideTargetHUD"/>) don't suppress the overhead HUD.
///
/// <c>GetHideTargetHUDBuffs_Execute</c> collects <c>Buff.Target</c> entities that carry
/// a <see cref="HideTargetHUD"/> component (stealth, camouflage, etc.). The resulting list
/// is fed into <c>GetDataJob</c> which skips HUD entries for those targets. By removing
/// player entities from the list right after it is populated, we keep their HUDs visible.
/// </summary>
[HarmonyPatch(typeof(GetCharacterHUDSystem), "GetHideTargetHUDBuffs_Execute")]
public static class PlayerHUDStealthPatch
{
    [HarmonyPostfix]
    static void Postfix(ref NativeList<Entity> entitiesWithHideHudBuffs)
    {
        if (!Config.ESP.AlwaysShowPlayerHUD.Value) return;

        var em = VWorld.EntityManager;

        // Walk backwards so we can swap-remove without skipping elements.
        for (var i = entitiesWithHideHudBuffs.Length - 1; i >= 0; i--)
        {
            var entity = entitiesWithHideHudBuffs[i];
            if (!em.Exists(entity)) continue;
            if (!em.HasComponent<PlayerCharacter>(entity)) continue;

            // Keep admin observers in the hide list — let them stay hidden.
            if (AdminObserveCheck.IsAdminObserver(entity)) continue;

            // Remove this player from the hide list — swap with last element.
            entitiesWithHideHudBuffs.RemoveAtSwapBack(i);
        }
    }
}
