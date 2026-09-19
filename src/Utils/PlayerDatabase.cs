using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BepInEx;

namespace ExtrasensoryPerception.Utils;

internal static class PlayerDatabase
{
    private static readonly string FilePath =
        Path.Combine(Paths.ConfigPath, "ExtrasensoryPerception_Players.json");

    // SteamId -> PlayerRecord
    private static Dictionary<ulong, PlayerRecord> _records = new();

    // Current session: Name -> SteamId (populated from UserInfoElement buffer)
    private static readonly Dictionary<string, ulong> _nameLookup = new();

    private static bool _dirty;

    internal sealed class PlayerRecord
    {
        public List<string> Names { get; set; } = new();
        public string FirstSeen { get; set; } = "";
        public string LastSeen { get; set; } = "";
    }

    internal static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<ulong, PlayerRecord>>(json);
            if (loaded != null)
            {
                _records = loaded;
                Plugin.Logger.LogInfo($"[PlayerDB] Loaded {_records.Count} player records");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[PlayerDB] Failed to load: {e.Message}");
        }
    }

    internal static void Save()
    {
        if (!_dirty) return;
        try
        {
            var json = JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
            _dirty = false;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[PlayerDB] Failed to save: {e.Message}");
        }
    }

    /// <summary>
    /// Update the database with a player sighting. Adds new names if not already known.
    /// </summary>
    internal static void RecordPlayer(ulong steamId, string currentName)
    {
        if (steamId == 0 || string.IsNullOrEmpty(currentName)) return;

        if (!_records.TryGetValue(steamId, out var record))
        {
            record = new PlayerRecord { FirstSeen = DateTime.UtcNow.ToString("o") };
            _records[steamId] = record;
            _dirty = true;
        }

        if (!record.Names.Contains(currentName))
        {
            record.Names.Add(currentName);
            record.LastSeen = DateTime.UtcNow.ToString("o");
            _dirty = true;
            Plugin.Logger.LogInfo($"[PlayerDB] New name for {steamId}: \"{currentName}\" (known names: {string.Join(", ", record.Names)})");
        }

        _nameLookup[currentName] = steamId;
    }

    /// <summary>
    /// Get the first known name for a player by their current in-game name.
    /// Returns null if unknown.
    /// </summary>
    internal static string? GetOriginalName(string currentName)
    {
        if (!_nameLookup.TryGetValue(currentName, out var steamId)) return null;
        if (!_records.TryGetValue(steamId, out var record)) return null;
        return record.Names.Count > 0 ? record.Names[0] : null;
    }

    /// <summary>
    /// Get the original (first) name for a player by their Steam ID directly.
    /// Returns null if unknown.
    /// </summary>
    internal static string? GetOriginalNameBySteamId(ulong steamId)
    {
        if (!_records.TryGetValue(steamId, out var record)) return null;
        return record.Names.Count > 0 ? record.Names[0] : null;
    }

    /// <summary>
    /// Get all known names for a player by their current in-game name.
    /// Returns null if unknown.
    /// </summary>
    internal static List<string>? GetAllNames(string currentName)
    {
        if (!_nameLookup.TryGetValue(currentName, out var steamId)) return null;
        if (!_records.TryGetValue(steamId, out var record)) return null;
        return record.Names;
    }

    /// <summary>
    /// Get the SteamId for a player by their current in-game name.
    /// </summary>
    internal static ulong GetSteamId(string currentName)
    {
        return _nameLookup.TryGetValue(currentName, out var steamId) ? steamId : 0;
    }

    /// <summary>
    /// Get the record for a SteamId directly.
    /// </summary>
    internal static PlayerRecord? GetRecord(ulong steamId)
    {
        return _records.TryGetValue(steamId, out var record) ? record : null;
    }

    internal static int RecordCount => _records.Count;

    /// <summary>
    /// Get all records for UI display.
    /// </summary>
    internal static Dictionary<ulong, PlayerRecord> GetAllRecords() => _records;

    /// <summary>
    /// Set which name index is considered the "original" by moving it to position 0.
    /// </summary>
    internal static void SetOriginalName(ulong steamId, int nameIndex)
    {
        if (!_records.TryGetValue(steamId, out var record)) return;
        if (nameIndex <= 0 || nameIndex >= record.Names.Count) return;

        var name = record.Names[nameIndex];
        record.Names.RemoveAt(nameIndex);
        record.Names.Insert(0, name);
        _dirty = true;
        Plugin.Logger.LogInfo($"[PlayerDB] Set original name for {steamId} to \"{name}\"");
    }

    /// <summary>
    /// Set a custom original name (typed by user). Inserts at position 0.
    /// </summary>
    internal static void SetCustomOriginalName(ulong steamId, string name)
    {
        if (!_records.TryGetValue(steamId, out var record)) return;
        if (string.IsNullOrEmpty(name)) return;

        // Remove if already in list, then insert at front
        record.Names.Remove(name);
        record.Names.Insert(0, name);
        _dirty = true;
        Save();
        Plugin.Logger.LogInfo($"[PlayerDB] Custom original name for {steamId}: \"{name}\"");
    }

    /// <summary>
    /// Check if a player with this SteamId is currently connected.
    /// </summary>
    internal static bool IsCurrentlyConnected(string name)
    {
        return _nameLookup.ContainsKey(name);
    }

    /// <summary>
    /// Get the current in-game name for a SteamId from live session data.
    /// Returns null if the player is not currently connected.
    /// </summary>
    internal static string? GetCurrentName(ulong steamId)
    {
        return Patches.SocialMenuPatch.SteamIdToName.TryGetValue(steamId, out var name) ? name : null;
    }
}
