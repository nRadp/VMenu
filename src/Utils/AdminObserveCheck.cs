using ExtrasensoryPerception.API;
using ProjectM;
using Unity.Entities;

namespace ExtrasensoryPerception.Utils;

/// <summary>
/// Checks whether an entity carries the <c>Admin_Observe_Invisible_Buff</c>
/// (PrefabGUID hash 1880224358). Players with this buff are invisible server-side
/// admin observers and should be excluded from ESP / forced HUD display.
/// </summary>
internal static class AdminObserveCheck
{
    private const int AdminObserveInvisibleBuffHash = 1880224358;

    /// <summary>
    /// Returns <c>true</c> if <paramref name="entity"/> currently has the admin
    /// observe invisible buff and the <c>HideAdminObservers</c> config is enabled.
    /// </summary>
    public static bool IsAdminObserver(Entity entity)
    {
        if (!Config.ESP.HideAdminObservers.Value) return false;
        return HasAdminObserveBuff(entity);
    }

    private static bool HasAdminObserveBuff(Entity entity)
    {
        if (!entity.HasBuffer<BuffBuffer>()) return false;

        var buffs = entity.ReadBuffer<BuffBuffer>();
        for (var i = 0; i < buffs.Length; i++)
        {
            if (buffs[i].PrefabGuid.GuidHash == AdminObserveInvisibleBuffHash)
                return true;
        }

        return false;
    }
}
