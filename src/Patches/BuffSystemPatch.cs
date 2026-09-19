using ExtrasensoryPerception.API;
using ExtrasensoryPerception.ESP;
using ExtrasensoryPerception.Utils;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace ExtrasensoryPerception.Patches;

[HarmonyPatch(typeof(BuffSystem_Spawn_Client), nameof(BuffSystem_Spawn_Client.OnUpdate))]
public class BuffSystemPatch
{
    [HarmonyPostfix]
    private static void Postfix(BuffSystem_Spawn_Client __instance)
    {
        if (__instance._Query.IsEmpty) return;

        var trackWeapon = Config.Extras.EnemyCooldownTracker.Enabled;
        var autoFish = Config.Extras.AutoFishing.Enabled;
        var autoCounter = Config.Extras.AutoCounter.Enabled || Config.Extras.AutoCounterDebugLog.Value;
        if (!trackWeapon && !autoFish && !autoCounter) return;

        var localCharacter = EntityList.LocalCharacter;
        var buffs = __instance._Query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var buff in buffs)
            {
                if (!buff.TryGetComponent<PrefabGUID>(out var buffGuid)) continue;
                if (!buff.TryGetComponent<EntityOwner>(out var ownerComp)) continue;
                var owner = ownerComp.Owner;

                if (autoFish &&
                    IsFishingReadyBuff(buffGuid) &&
                    owner == localCharacter)
                {
                    Plugin.Logger.LogDebug("There's a fish ready to catch!");
                    MouseSimulator.LeftClick();
                }

                if (owner == Entity.Null || !owner.Exists() || !owner.IsPlayer() || owner == localCharacter)
                    continue;

                // Twinblade Javelin (and similar) may spawn as SpellObject/buff without AbilityCastStarted.
                if (trackWeapon)
                    EnemyCooldownTracker.OnWeaponProjectileObserved(owner, buffGuid);

                // Sword Shockwave BlockBuff — remote-safe cast-commit for AutoCounter.
                if (autoCounter)
                    AutoCounter.OnEnemyBuffObserved(owner, buffGuid);
            }
        }
        finally
        {
            buffs.Dispose();
        }
    }

    private static bool IsFishingReadyBuff(PrefabGUID buffGuid)
    {
        return VWorld.PrefabLookupMap.GetName(buffGuid) == "AB_Fishing_Target_ReadyBuff";
    }
}
