using System.Collections.Generic;
using ExtrasensoryPerception.API;
using ExtrasensoryPerception.Utils;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using ProjectM.UI;
using UnityEngine;

namespace ExtrasensoryPerception.Patches;

[HarmonyPatch(typeof(VoiceOverlaySystem), nameof(VoiceOverlaySystem.OnUpdate))]
public static class SocialMenuPatch
{
    // Name -> SteamId lookup from current session (all connected players)
    public static readonly Dictionary<string, ulong> NameToSteamId = new();
    // Reverse lookup: SteamId -> Name
    public static readonly Dictionary<ulong, string> SteamIdToName = new();

    private static float _lastSaveTime;
    private static float _lastRefreshTime;
    private const float SaveInterval = 30f;
    private const float RefreshInterval = 2f;

    [HarmonyPostfix]
    static void Postfix(VoiceOverlaySystem __instance)
    {
        if (!Plugin.IsInGame) return;

        // Only refresh player list every 2 seconds, not every frame
        var now = Time.time;
        if (now - _lastRefreshTime < RefreshInterval) return;
        _lastRefreshTime = now;

        try
        {
            var em = VWorld.EntityManager;
            var accessor = __instance._UserInfoBufferSingletonAccessor;
            if (!UserInfoUtility.TryGetUserInfoBuffer(em, accessor, out var buffer)) return;

            NameToSteamId.Clear();
            SteamIdToName.Clear();

            for (var i = 0; i < buffer.Length; i++)
            {
                var info = buffer[i];
                if (info.PlatformId != 0 && info.IsConnected)
                {
                    var name = info.Name.ToString();
                    NameToSteamId[name] = info.PlatformId;
                    SteamIdToName[info.PlatformId] = name;
                    PlayerDatabase.RecordPlayer(info.PlatformId, name);
                }
            }

            // Save to disk periodically
            if (now - _lastSaveTime > SaveInterval)
            {
                _lastSaveTime = now;
                PlayerDatabase.Save();
            }
        }
        catch
        {
            // Silently ignore — buffer may not be ready yet
        }
    }

    /// <summary>
    /// Get Steam ID for a player by their current in-game name.
    /// Returns 0 if not found.
    /// </summary>
    public static ulong GetSteamId(string name)
    {
        return NameToSteamId.TryGetValue(name, out var id) ? id : 0;
    }
}
