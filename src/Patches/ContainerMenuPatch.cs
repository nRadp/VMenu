using ExtrasensoryPerception.Utils;
using HarmonyLib;
using ProjectM.UI;

namespace ExtrasensoryPerception.Patches;

[HarmonyPatch(typeof(ContainerMenuMapper), nameof(ContainerMenuMapper.OnUpdate))]
public class ContainerMenuPatch
{
    [HarmonyPostfix]
    private static void Postfix(ContainerMenuMapper __instance)
    {
        if (!Config.Extras.AutoLoot.Enabled || !__instance._ContainerSubMenu.isActiveAndEnabled) return;
        __instance._ContainerSubMenu.TakeAllButton.Press();
    }
}