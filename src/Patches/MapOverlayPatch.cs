using ExtrasensoryPerception.UI;
using HarmonyLib;
using ProjectM.UI;

namespace ExtrasensoryPerception.Patches;

[HarmonyPatch(typeof(MiniMapHUDSystem), nameof(MiniMapHUDSystem.OnUpdate))]
internal static class MinimapOverlayPatch
{
    [HarmonyPostfix]
    private static void Postfix(MiniMapHUDSystem __instance)
    {
        MapOverlay.UpdateMinimap(__instance);
    }
}

[HarmonyPatch(typeof(MapMenuMapper), nameof(MapMenuMapper.OnUpdate))]
internal static class WorldMapOverlayPatch
{
    [HarmonyPostfix]
    private static void Postfix(MapMenuMapper __instance)
    {
        MapOverlay.UpdateWorldMap(__instance);
    }
}
