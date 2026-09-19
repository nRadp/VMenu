using Engine.Console;
using Engine.Console.PublicInterface;
using ExtrasensoryPerception.Utils;
using HarmonyLib;
using ProjectM;
using Stunlock.Network;
using UnityEngine;

namespace ExtrasensoryPerception.Patches;

[HarmonyPatch(typeof(ClientBootstrapSystem), "OnClientDisconnected")]
public static class ConnectionPatch
{
    private static float _retryTime;
    private static bool _retryPending;
    private static string? _lastConnectCommand;

    /// <summary>
    /// Called by the console command interceptor to record the most recent Connect command text.
    /// </summary>
    internal static void RecordConnectCommand(string fullCommand)
    {
        _lastConnectCommand = fullCommand;
    }

    [HarmonyPostfix]
    static void Postfix(ConnectionStatusChangeReason connectionStatusChangeReason, string extraData)
    {
        Plugin.Logger.LogInfo($"[Connection] Disconnected: {connectionStatusChangeReason} ({(int)connectionStatusChangeReason}), extra: \"{extraData}\"");

        if (connectionStatusChangeReason != ConnectionStatusChangeReason.ServerFull)
            return;

        if (!Config.Extras.AutoRetryConnect.Enabled)
            return;

        if (string.IsNullOrEmpty(_lastConnectCommand))
        {
            Plugin.Logger.LogWarning("[AutoRetry] No connect command recorded — cannot retry.");
            return;
        }

        var delay = Config.Extras.AutoRetryDelaySeconds.Value;
        _retryTime = Time.realtimeSinceStartup + delay;
        _retryPending = true;
        Plugin.Logger.LogInfo($"[AutoRetry] Server full — will retry in {delay:F1}s with: {_lastConnectCommand}");
    }

    /// <summary>
    /// Called each frame from the AutoRetryController MonoBehaviour.
    /// </summary>
    internal static void Update()
    {
        if (!_retryPending) return;
        if (Time.realtimeSinceStartup < _retryTime) return;

        _retryPending = false;

        if (!Config.Extras.AutoRetryConnect.Enabled)
            return;

        var cmd = _lastConnectCommand;
        if (string.IsNullOrEmpty(cmd))
            return;

        Plugin.Logger.LogInfo($"[AutoRetry] Retrying: {cmd}");
        try
        {
            StunConsole.Commands.TryCommand(cmd, true);
        }
        catch (System.Exception ex)
        {
            Plugin.Logger.LogError($"[AutoRetry] Failed to execute command: {ex.Message}");
        }
    }

    internal static void Reset()
    {
        _retryPending = false;
        _lastConnectCommand = null;
    }
}

/// <summary>
/// Intercepts console TryCommand to capture Connect commands for auto-retry replay.
/// </summary>
[HarmonyPatch(typeof(ConsoleCommandHandler), nameof(ConsoleCommandHandler.TryCommand))]
public static class ConsoleCommandInterceptor
{
    [HarmonyPrefix]
    static void Prefix(string text)
    {
        if (text != null && text.StartsWith("Connect ", System.StringComparison.OrdinalIgnoreCase))
        {
            ConnectionPatch.RecordConnectCommand(text);
        }
    }
}

/// <summary>
/// MonoBehaviour that drives the auto-retry timer each frame.
/// </summary>
public class AutoRetryController : MonoBehaviour
{
    private void Update()
    {
        ConnectionPatch.Update();
    }
}
