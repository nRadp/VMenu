using HarmonyLib;
using ProjectM.UI;

namespace ExtrasensoryPerception.Patches;

[HarmonyPatch]
public static class HUDMenuPatch
{
    [HarmonyPatch(typeof(HUDMenu), nameof(HUDMenu.OnEnable))]
    [HarmonyPostfix]
    static void OnEnablePostfix()
    {
        Plugin.IsMenuOpen = true;
    }

    [HarmonyPatch(typeof(HUDMenu), nameof(HUDMenu.OnDisable))]
    [HarmonyPostfix]
    static void OnDisablePostfix()
    {
        Plugin.IsMenuOpen = false;
    }
}