using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using ExtrasensoryPerception.API;
using ProjectM;
using Unity.Entities;
using UnityEngine;

namespace ExtrasensoryPerception.Utils;

internal static class CastleHeartTracker
{
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal sealed class HeartRecord
    {
        internal string SaveKey = "";
        internal int EntityIndex = -1;
        internal Vector3 Position;

        // Baseline: remaining seconds at the moment LastSeenUtc was recorded
        internal double BaselineSeconds = double.MaxValue;
        // Wall-clock moment the baseline was captured (UTC)
        internal DateTime LastSeenUtc = DateTime.MinValue;

        // Live countdown — subtracts real elapsed time from the baseline
        internal double CurrentRemainingSeconds
        {
            get
            {
                if (BaselineSeconds >= double.MaxValue || LastSeenUtc == DateTime.MinValue)
                    return double.MaxValue;
                var elapsed = (DateTime.UtcNow - LastSeenUtc).TotalSeconds;
                return Math.Max(0.0, BaselineSeconds - elapsed);
            }
        }

        internal string CurrentRemainingText
        {
            get
            {
                var secs = CurrentRemainingSeconds;
                if (secs >= double.MaxValue) return "?";
                var ts = TimeSpan.FromSeconds(secs);
                var days = (int)ts.TotalDays;
                return days > 0
                    ? $"{days}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s"
                    : $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
            }
        }

        // When the heart will run out of fuel (local time)
        internal string ExpiresAtText
        {
            get
            {
                if (BaselineSeconds >= double.MaxValue || LastSeenUtc == DateTime.MinValue) return "?";
                var expiresLocal = LastSeenUtc.AddSeconds(BaselineSeconds).ToLocalTime();
                return expiresLocal.Date == DateTime.Today
                    ? expiresLocal.ToString("HH:mm:ss")
                    : expiresLocal.ToString("dd/MM HH:mm");
            }
        }

        // Local time string for the "last updated" column
        internal string LastUpdatedText =>
            LastSeenUtc == DateTime.MinValue ? "—" : LastSeenUtc.ToLocalTime().ToString("HH:mm:ss");
    }

    private static readonly Dictionary<string, HeartRecord> Records = new();
    private static readonly Dictionary<int, string> IndexToKey = new();
    private static readonly List<HeartRecord> SortedCache = new();
    private static bool _sortedCacheDirty = true;
    private static string? _pinnedSaveKey;
    private static double _lastSaveTime;

    internal static string? PinnedSaveKey => _pinnedSaveKey;

    internal static int? SelectedEntityIndex
    {
        get
        {
            if (_pinnedSaveKey == null) return null;
            if (Records.TryGetValue(_pinnedSaveKey, out var r) && r.EntityIndex >= 0)
                return r.EntityIndex;
            return null;
        }
    }

    internal static HeartRecord? GetSelected() =>
        _pinnedSaveKey != null && Records.TryGetValue(_pinnedSaveKey, out var r) ? r : null;

    internal static void Select(string saveKey) { _pinnedSaveKey = saveKey; SaveToDisk(); }
    internal static void Deselect() { _pinnedSaveKey = null; SaveToDisk(); }

    internal static IReadOnlyList<HeartRecord> GetSorted()
    {
        if (_sortedCacheDirty)
        {
            SortedCache.Clear();
            SortedCache.AddRange(Records.Values);
            _sortedCacheDirty = false;
        }
        // Sort by live countdown so most urgent stays at top
        SortedCache.Sort((a, b) => a.CurrentRemainingSeconds.CompareTo(b.CurrentRemainingSeconds));
        return SortedCache;
    }

    internal static void Register(Entity entity)
    {
        var entityIndex = entity.Index;
        var position = entity.GetPosition();
        var saveKey = MakeSaveKey(position);

        ESP.Logic.TryGetCastleHeartRemainingSeconds(entity, out var seconds);

        if (Records.TryGetValue(saveKey, out var record))
        {
            if (record.EntityIndex != entityIndex)
            {
                if (record.EntityIndex >= 0) IndexToKey.Remove(record.EntityIndex);
                record.EntityIndex = entityIndex;
                IndexToKey[entityIndex] = saveKey;
            }
        }
        else
        {
            record = new HeartRecord { SaveKey = saveKey, EntityIndex = entityIndex, Position = position };
            Records[saveKey] = record;
            IndexToKey[entityIndex] = saveKey;
            _sortedCacheDirty = true;
        }

        record.Position = position;
        if (seconds > 0)
        {
            record.BaselineSeconds = seconds;
            record.LastSeenUtc = DateTime.UtcNow;
        }

        // Persist every ~60 s; also on first sighting (LastSeenUtc just set above)
        if (Time.timeAsDouble - _lastSaveTime > 60.0)
        {
            _lastSaveTime = Time.timeAsDouble;
            SaveToDisk();
        }
    }

    internal static void ResetSession()
    {
        SaveToDisk();
        IndexToKey.Clear();
        foreach (var r in Records.Values)
            r.EntityIndex = -1;
    }

    internal static void Clear()
    {
        Records.Clear();
        IndexToKey.Clear();
        SortedCache.Clear();
        _sortedCacheDirty = false;
        _pinnedSaveKey = null;
        SaveToDisk();
    }

    internal static void LoadFromDisk()
    {
        try
        {
            var path = SaveFilePath();
            if (!File.Exists(path)) return;

            string? pendingPin = null;

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#pin=")) { pendingPin = line.Substring(5); continue; }

                // Format: X|Z|BaselineSeconds|LastSeenUnixSeconds
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) continue;
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) continue;

                var baseline = double.MaxValue;
                var lastSeenUtc = DateTime.MinValue;

                if (parts.Length >= 3)
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out baseline);
                if (parts.Length >= 4 && long.TryParse(parts[3], out var unixSecs))
                    lastSeenUtc = Epoch.AddSeconds(unixSecs);

                var pos = new Vector3(x, 0f, z);
                var saveKey = MakeSaveKey(pos);
                if (!Records.ContainsKey(saveKey))
                {
                    Records[saveKey] = new HeartRecord
                    {
                        SaveKey = saveKey,
                        EntityIndex = -1,
                        Position = pos,
                        BaselineSeconds = baseline,
                        LastSeenUtc = lastSeenUtc
                    };
                    _sortedCacheDirty = true;
                }
            }

            if (pendingPin != null && Records.ContainsKey(pendingPin))
                _pinnedSaveKey = pendingPin;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[CastleHeartTracker] Load failed: {e.Message}");
        }
    }

    private static void SaveToDisk()
    {
        try
        {
            var lines = new List<string>();
            if (_pinnedSaveKey != null) lines.Add($"#pin={_pinnedSaveKey}");

            foreach (var r in Records.Values)
            {
                var x = r.Position.x.ToString("F1", CultureInfo.InvariantCulture);
                var z = r.Position.z.ToString("F1", CultureInfo.InvariantCulture);
                var secs = r.BaselineSeconds < double.MaxValue
                    ? r.BaselineSeconds.ToString("F0", CultureInfo.InvariantCulture) : "";
                var unix = r.LastSeenUtc != DateTime.MinValue
                    ? ((long)(r.LastSeenUtc - Epoch).TotalSeconds).ToString() : "";
                lines.Add($"{x}|{z}|{secs}|{unix}");
            }

            File.WriteAllLines(SaveFilePath(), lines);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[CastleHeartTracker] Save failed: {e.Message}");
        }
    }

    private static string SaveFilePath() =>
        Path.Combine(Paths.ConfigPath, "ExtrasensoryPerception_Hearts.txt");

    private static string MakeSaveKey(Vector3 pos) =>
        $"{Mathf.RoundToInt(pos.x)}_{Mathf.RoundToInt(pos.z)}";
}
