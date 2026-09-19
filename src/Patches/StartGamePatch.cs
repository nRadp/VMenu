using HarmonyLib;
using ProjectM.UI;

namespace ExtrasensoryPerception.Patches;

[HarmonyPatch(typeof(StartGameLoadMenuView), nameof(StartGameLoadMenuView.Update))]
public static class StartGamePatch
{
    [HarmonyPrefix]
    private static void UpdatePrefix(StartGameLoadMenuView __instance)
    {
        if (__instance.VideoPlayer.isPlaying) __instance.VideoPlayer.Stop();
    }
}