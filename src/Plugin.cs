using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using ExtrasensoryPerception.API;
using ExtrasensoryPerception.ESP;
using ExtrasensoryPerception.Patches;
using ExtrasensoryPerception.UI;
using ExtrasensoryPerception.Utils;
using HarmonyLib;

namespace ExtrasensoryPerception;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public static Plugin Instance { get; private set; } = null!;
    private Harmony _harmony = null!;
    public static bool IsMenuOpen = false;
    public static bool IsInGame = false;

    public static ManualLogSource Logger => Instance.Log;

    public override void Load()
    {
        Instance = this;
        Config.SaveOnConfigSet = true;

        // Plugin startup logic
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} version {MyPluginInfo.PLUGIN_VERSION} is loaded!");

        // Load persistent player database
        PlayerDatabase.Load();

        // Harmony patching
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());

        // Components
        AddComponent<Menu>();
        AddComponent<Overlay>();
        AddComponent<AimController>();
        AddComponent<AutoCounterController>();
        AddComponent<KeyBindSystem>();
        AddComponent<CursorVisibilityKeeper>();
        AddComponent<AutoRetryController>();
    }

    public void OnGameInitialized()
    {
        EntityList.InitializeQueries();
        MapOverlay.ResetState();
        RenderQueue.Clear();
        CastleHeartTracker.LoadFromDisk();
    }

    public void OnGameEnded()
    {
        PlayerDatabase.Save();
        IsInGame = false;
        VWorld.Reset();
        EntityList.ResetState();
        MapOverlay.ResetState();
        RenderQueue.Clear();
        EnemyCooldownTracker.Reset();
        AbilityIconResolver.Reset();
        AutoCounter.Reset();
        CastleHeartTracker.ResetSession();
        Logic.ResetServerTimeQuery();
        CameraZoomPatch.ResetState();
    }

    public override bool Unload()
    {
        PlayerDatabase.Save();
        _harmony.UnpatchSelf();
        return true;
    }
}
