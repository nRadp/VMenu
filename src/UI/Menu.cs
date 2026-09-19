using BepInEx.Configuration;
using ExtrasensoryPerception.API;
using ExtrasensoryPerception.ESP;
using ExtrasensoryPerception.Utils;
using ProjectM;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ExtrasensoryPerception.UI;

internal class Menu : MonoBehaviour
{
    private static Rect _windowRect = new(20, 20, 860, 780);
    private bool _showMenu = false;
    private const float ColumnWidth = 410f;

    private readonly string[] _logAbilityCastsModes = ["Off", "Local", "All"];
    private readonly string[] _aimbotModes = ["Hold", "Toggle"];
    private readonly string[] _cooldownDisplayModes = ["Text", "Pips"];

    private int _activeTab = 0;
    private int _activePlayerSubTab = 0;
    private Vector2 _heartScrollPos = Vector2.zero;
    private float _heartScrollTarget;
    private Vector2 _playerScrollPos = Vector2.zero;
    private float _playerScrollTarget;
    private Vector2 _onlineScrollPos = Vector2.zero;
    private float _onlineScrollTarget;
    private ulong _editingSteamId;
    private string _editingName = "";
    private bool _showEnemyCdr;
    private bool _showAutoCounterSkills;

    // Cached per-frame snapshots to keep IMGUI control count consistent across Layout/Repaint
    private System.Collections.Generic.IReadOnlyList<CastleHeartTracker.HeartRecord>? _cachedHeartRecords;
    private string? _cachedHeartPinnedKey;
    private System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ulong, PlayerDatabase.PlayerRecord>>? _cachedPlayerFiltered;
    private System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ulong, string>>? _cachedOnlinePlayers;

    // Layout constants
    private const float LabelWidth = 110f;
    private const float ValueWidth = 50f;
    private const float SwatchSize = 20f;

    private void OnGUI()
    {
        if (!_showMenu) return;
        MenuTheme.SetupDarkTheme();

        _windowRect = GUI.Window(1, _windowRect, (GUI.WindowFunction)DrawWindow, "VMenu", MenuTheme.WindowStyle);

        // Popups always last so they overlay everything
        Popups.Draw(2);

        ShowTooltip();
    }

    private void Update()
    {
        if (Input.GetKeyDown(Config.MenuKey.Value)) _showMenu = !_showMenu;

        // Handle player name editing input here (Update runs once per frame)
        if (_editingSteamId != 0)
        {
            foreach (var c in Input.inputString)
            {
                if (c == '\b')
                    _editingName = _editingName.Length > 0 ? _editingName.Substring(0, _editingName.Length - 1) : "";
                else if (c == '\n' || c == '\r')
                {
                    if (!string.IsNullOrEmpty(_editingName))
                        PlayerDatabase.SetCustomOriginalName(_editingSteamId, _editingName);
                    _editingSteamId = 0;
                }
                else if (!char.IsControl(c))
                    _editingName += c;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) _editingSteamId = 0;
        }
    }

    private void DrawWindow(int windowID)
    {
        var showCastleHeartsTab = Config.ESP.CastleHearts.Enabled;
        if (!showCastleHeartsTab && _activeTab == 1)
            _activeTab = 0;

        // Tab bar
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("ESP / Extras", _activeTab == 0 ? MenuTheme.TabActiveStyle : MenuTheme.TabInactiveStyle)) _activeTab = 0;
        if (showCastleHeartsTab && GUILayout.Button("Castle Hearts", _activeTab == 1 ? MenuTheme.TabActiveStyle : MenuTheme.TabInactiveStyle)) _activeTab = 1;
        if (GUILayout.Button($"Players ({PlayerDatabase.RecordCount})", _activeTab == 2 ? MenuTheme.TabActiveStyle : MenuTheme.TabInactiveStyle)) _activeTab = 2;
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        if (_activeTab == 0)
            DrawMainTab();
        else if (_activeTab == 1)
            DrawCastleHeartsTab();
        else
            DrawPlayersTab();

        GUI.DragWindow(new Rect(0, 0, _windowRect.width, 22));
    }

    private void DrawMainTab()
    {
        GUILayout.BeginHorizontal();

        // ===================== Left Column =====================
        GUILayout.BeginVertical(GUILayout.Width(ColumnWidth));

        Header("General");
        Toggle(Config.ModToggle, "Mod Enabled");
        KeyBindRow("Toggle Menu", Config.MenuKey);

        Header("ESP — Targets");
        EspRow("Players", Config.ESP.Players);
        GUILayout.BeginHorizontal();
        GUILayout.Space(12);
        GUILayout.BeginVertical();
        Toggle(Config.ESP.PlayerName, "Name");
        Toggle(Config.ESP.PlayerGearLevel, "Gear Level");
        Toggle(Config.ESP.PlayerHP, "HP");
        Toggle(Config.ESP.AlwaysShowPlayerHUD, new GUIContent("Always Show HUD", null, "Keep native player name/level/HP bar visible at any distance, behind cover, and through stealth/invisibility."));
        Toggle(Config.ESP.HideAdminObservers, new GUIContent("Hide Admin Observers", null, "Exclude players with the Admin Observe Invisible buff from ESP and native HUD."));
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        EspRow("Map Overlay", Config.ESP.MinimapPlayers);
        EspRow("VBlood Carriers", Config.ESP.VBloodCarriers);
        EspRow("High Quality Blood", Config.ESP.HighQualityBlood);
        DrawHighQualityBloodTypeFilter();
        EspRow("Gate Bosses", Config.ESP.GateBosses);
        EspRow("Items", Config.ESP.Items);
        EspRow("Containers", Config.ESP.Containers);
        EspRow("Ores", Config.ESP.Ores);
        EspRow("Plants", Config.ESP.Plants);
        EspRow("Fishing Spots", Config.ESP.FishingSpots);
        EspRow("Horses", Config.ESP.Horses);
        EspRow("Servants", Config.ESP.Servants);
        EspRow("Carriages", Config.ESP.Carriages);
        EspRow("Castle Hearts", Config.ESP.CastleHearts);

        GUILayout.EndVertical();

        GUILayout.Space(10);

        // ===================== Right Column =====================
        GUILayout.BeginVertical(GUILayout.Width(ColumnWidth));

        Header("ESP — Display");
        SliderRow("Font Scale", Config.ESP.FontScale, 0.5f, 3.0f, "F2", "x");

        Header("Aimbot");
        Toggle(Config.Aimbot.Status, "Enabled");
        if (Config.Aimbot.Enabled)
        {
            RadioGroup("Mode", _aimbotModes, Config.Aimbot.Mode);
            KeyBindRow("Key", Config.Aimbot.Key);
            KeyBindRow("Key 2", Config.Aimbot.Key2);
            GUILayout.Space(4);
            Toggle(Config.Aimbot.Players, "Players");
            Toggle(Config.Aimbot.Bosses, "Bosses");
            Toggle(Config.Aimbot.Mobs, "Mobs");
            Toggle(Config.Aimbot.DrawAimPosition, "Draw Aim Position");
            GUILayout.Space(4);
            SubHeader("Limits");
            SliderRow("Distance", Config.Aimbot.MaxDistance, 1f, 50f, "F0", "m");
            SliderRow("Cursor Dist", Config.Aimbot.MaxCursorDistance, 0f, 1000f, "F0");
            SliderRow("Switch CD", Config.Aimbot.SwitchCooldown, 0f, 1f, "F1", "s");
        }

        GUILayout.Space(6);
        Header("Extras");
        Toggle(Config.Extras.AutoFishing, "Auto-Fishing");
        Toggle(Config.Extras.AutoLoot, "Auto-Loot (WIP)");
        Toggle(Config.Extras.NoFog, "No Fog");
            Toggle(Config.Extras.EnemyCooldownTracker, new GUIContent("Enemy Cooldowns", null, "Track when enemy players cast spells and show remaining cooldown in their ESP label. Cooldown values auto-discover from the ability prefabs (same data the in-game tooltip reads)."));
        if (Config.Extras.EnemyCooldownTracker.Enabled)
        {
            RadioGroup("Display", _cooldownDisplayModes, Config.Extras.EnemyCooldownTrackerDisplayMode);
            if (FullButton("Reset Known Abilities"))
                EnemyCooldownTracker.Reset();

            // CDR list is long — keep collapsed so Auto Counter / Camera stay on-screen.
            _showEnemyCdr = GUILayout.Toggle(_showEnemyCdr, _showEnemyCdr ? "▾ CDR (rough)" : "▸ CDR (rough)", MenuTheme.ToggleStyle);
            if (_showEnemyCdr)
            {
                GUILayout.Space(4);
                SubHeader("Counters");
                SliderRow("All Counters", Config.Extras.CdrAllCounters, 0f, 15f, "F1", "s");

                SubHeader("Veil");
                SliderRow("All Veils", Config.Extras.CdrVeil, 0f, 15f, "F1", "s");
            }
        }
        Toggle(Config.Extras.AutoCounter, new GUIContent("Auto Counter", null, "Automatically activates your counter ability when an enemy starts a tracked lock ability (spear AThousandSpears, slashers Camouflage counter). Dynamically detects equipped counter abilities (BloodRite, MistTrance, ChaosBarrier, FrostBarrier, WardOfTheDamned, Discharge) in your spell slots. If no counter is equipped, nothing happens. Set Spell Slot keys to match your in-game keybinds."));
        if (Config.Extras.AutoCounter.Enabled)
        {
            // Skill list is long — collapse so Camera / Debug stay reachable.
            _showAutoCounterSkills = GUILayout.Toggle(
                _showAutoCounterSkills,
                _showAutoCounterSkills ? "▾ Tracked Skills" : "▸ Tracked Skills",
                MenuTheme.ToggleStyle);
            if (_showAutoCounterSkills)
            {
                GUILayout.Space(2);
                Toggle(Config.Extras.AutoCounterSlasherE, new GUIContent("Slasher E", null, "Counter slashers' Camouflage Secondary (E) — cone dash + 120° swing."));
                Toggle(Config.Extras.AutoCounterSlasherQ, new GUIContent("Slasher Q", null, "Counter slashers' ElusiveStrike Dash (Q) — forward dash corridor (~8m × 1.25m)."));
                Toggle(Config.Extras.AutoCounterWhipQ, new GUIContent("Whip Q", null, "Counter whip Dash (Q) — 5m dash + 3.5m circle at landing."));
                Toggle(Config.Extras.AutoCounterSwordE, new GUIContent("Sword E", null, "Counter sword Shockwave (E) — BlockBuff commit; prefab range 14m + tip pad (~15m head-on)."));
                Toggle(Config.Extras.AutoCounterSpearQ, new GUIContent("Spear Q", null, "Counter spear AThousandSpears Stab (Q) — forward box thrust."));
                Toggle(Config.Extras.AutoCounterTwinbladeE, new GUIContent("Twinblade E", null, "Counter twinblade SweepingStrike (E) — forward lunge + line swing."));
                Toggle(Config.Extras.AutoCounterReaperQ, new GUIContent("Reaper Q", null, "Counter reaper TendonSwing Twist (Q) — 3m circle around the caster (ignores facing)."));
                Toggle(Config.Extras.AutoCounterPistolPrimary, new GUIContent("Pistol Primary", null, "Counter pistol Primary — near windup end with body facing (remote aim is unreliable)."));
                Toggle(Config.Extras.AutoCounterPistolE, new GUIContent("Pistol E", null, "Counter pistol ExplosiveShot (E) — Shot/Recast projectile only (not the dash); 8m × r=0.60, late-fire like primary."));
                Toggle(Config.Extras.AutoCounterCrossbowSnapshot, new GUIContent("Crossbow Snapshot", null, "Counter crossbow Snapshot — 12m forward projectile, same dual-gate timing as pistol."));
                Toggle(Config.Extras.AutoCounterCrossbowPrimary, new GUIContent("Crossbow Primary", null, "Counter crossbow Primary — near windup end (1s); interrupt drops pending when observed."));
                GUILayout.Space(2);
            }

            Toggle(Config.Extras.AutoCounterUseCounters, new GUIContent("Counters", null, "Use parry-style counter abilities: BloodRite, MistTrance, Discharge."));
            Toggle(Config.Extras.AutoCounterUseBarriers, new GUIContent("Barriers", null, "Use damage-soaking barrier abilities: ChaosBarrier, FrostBarrier, WardOfTheDamned."));
            KeyBindRow("Spell Slot 1", Config.Extras.AutoCounterSpellSlot1Key);
            KeyBindRow("Spell Slot 2", Config.Extras.AutoCounterSpellSlot2Key);
            SliderRow("Activation Lag", Config.Extras.AutoCounterActivationLatencySeconds, 0.0f, 0.5f, "F2", "s");
            SliderRow("Target Radius", Config.Extras.AutoCounterTargetColliderRadius, 0f, 1.5f, "F2", "m");
        }
        Toggle(Config.Extras.AutoCounterDebugLog, new GUIContent("AC Debug Log", null, "Log dist+angle for every nearby enemy cast so you can pick per-ability range/cone values. Output goes to BepInEx.log prefixed with 'AutoCounterDebug'. Works even when Auto Counter is off."));
        if (Config.Extras.AutoCounterDebugLog.Value)
            SliderRow("AC Log Radius", Config.Extras.AutoCounterDebugLogRadius, 0f, 50f, "F0", "m");
        RadioGroup("Log Ability Casts", _logAbilityCastsModes, Config.Extras.LogAbilityCasts);
        Toggle(Config.Extras.AutoRetryConnect, new GUIContent("Auto-Retry Connect", null, "Automatically retry connecting when the server is full. Replays the last console Connect command after a delay."));
        if (Config.Extras.AutoRetryConnect.Enabled)
        {
            SliderRow("Retry Delay", Config.Extras.AutoRetryDelaySeconds, 0f, 1f, "F2", "s");
        }

        GUILayout.Space(6);
        Header("Camera");
        Toggle(Config.Camera.ExtendedZoom, new GUIContent("Extended Zoom", null, "Override camera zoom limits to allow zooming further out (or closer in) than default."));
        if (Config.Camera.ExtendedZoom.Enabled)
        {
            SliderRow("Max Zoom", Config.Camera.MaxZoomDistance, 10f, 50f, "F0", "m");
            SliderRow("Min Zoom", Config.Camera.MinZoomDistance, 1f, 10f, "F1", "m");
        }

        GUILayout.Space(6);
        Header("Debug");
        if (FullButton("Log Player Buffs"))
            LogAllPlayerBuffs();
        if (FullButton("Log Ability Bar"))
            LogLocalAbilityBar();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void DrawCastleHeartsTab()
    {
        // Snapshot data on Layout to keep control count consistent between Layout and Repaint
        if (Event.current.type == EventType.Layout || _cachedHeartRecords == null)
        {
            _cachedHeartRecords = CastleHeartTracker.GetSorted();
            _cachedHeartPinnedKey = CastleHeartTracker.PinnedSaveKey;
        }
        var records = _cachedHeartRecords;
        var pinnedKey = _cachedHeartPinnedKey;

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Tracked: {records.Count}", MenuTheme.LabelStyle);
        if (pinnedKey != null)
        {
            GUILayout.Space(6);
            GUILayout.Label("● Pinned on minimap", MenuTheme.PinnedBadgeStyle);
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Clear List", MenuTheme.ButtonStyle, GUILayout.Width(90)))
            CastleHeartTracker.Clear();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        // Column headers
        GUILayout.BeginHorizontal();
        GUILayout.Label("", MenuTheme.SubHeaderStyle, GUILayout.Width(84));
        GUILayout.Label("Remaining", MenuTheme.SubHeaderStyle, GUILayout.ExpandWidth(true));
        GUILayout.Label("Expires At", MenuTheme.SubHeaderStyle, GUILayout.Width(100));
        GUILayout.Label("Updated", MenuTheme.SubHeaderStyle, GUILayout.Width(72));
        GUILayout.EndHorizontal();

        if (records.Count == 0)
        {
            GUILayout.Space(8);
            GUILayout.Label("No castle hearts seen yet — move near a castle or enable Castle Hearts ESP.", MenuTheme.LabelStyle);
            return;
        }

        // BeginScrollView and GUILayoutUtility.GetLastRect are both stripped in this IL2CPP build.
        // Scroll by shifting the GUILayout origin with a negative Space, then let the window rect
        // clip both visual output and input for rows that fall outside its bounds.
        // HeartRowStyle padding(4+4) + margin(1+1) + tallest child ~PinStyle(19px) ≈ 30px
        const float rowH = 30f;
        // Window padding(24+10) + tab bar(26) + Space(4) + stats row(28) + Space(4) + col headers(26) + range indicator(26) + buffer
        const float approxHeaderH = 158f;
        var visibleH = _windowRect.height - approxHeaderH;
        var maxScroll = Mathf.Max(0f, records.Count * rowH - visibleH);

        var ev = Event.current;
        if (ev.type == EventType.ScrollWheel)
        {
            _heartScrollTarget = Mathf.Clamp(_heartScrollTarget + ev.delta.y * rowH * 3f, 0f, maxScroll);
            ev.Use();
        }
        _heartScrollTarget = Mathf.Clamp(_heartScrollTarget, 0f, maxScroll);
        // Only update scroll position during Layout to keep controls consistent across IMGUI passes
        if (ev.type == EventType.Layout)
        {
            _heartScrollPos.y = Mathf.Lerp(_heartScrollPos.y, _heartScrollTarget, Time.deltaTime * 15f);
            if (Mathf.Abs(_heartScrollPos.y - _heartScrollTarget) < 0.5f)
                _heartScrollPos.y = _heartScrollTarget;
        }

        var firstVisible = Mathf.Max(0, Mathf.FloorToInt(_heartScrollPos.y / rowH));
        var lastVisible = Mathf.Min(records.Count - 1, firstVisible + Mathf.CeilToInt(visibleH / rowH));

        if (maxScroll > 0f)
        {
            GUILayout.Label($"  {firstVisible + 1}–{lastVisible + 1} of {records.Count}", MenuTheme.SubHeaderStyle);
        }

        // Virtual scrolling: only render visible rows, offset first row for smooth sub-row scroll
        var partialOffset = _heartScrollPos.y % rowH;
        if (partialOffset > 0f)
            GUILayout.Space(-partialOffset);

        for (var i = firstVisible; i <= lastVisible; i++)
        {
            var r = records[i];
            var isPinned = r.SaveKey == pinnedKey;
            GUILayout.BeginHorizontal(MenuTheme.HeartRowStyle(i, isPinned));

            var pinLabel = isPinned ? "★ PINNED" : "☆ Pin";
            if (GUILayout.Button(pinLabel, isPinned ? MenuTheme.PinActiveStyle : MenuTheme.PinStyle, GUILayout.Width(82)))
            {
                if (isPinned) CastleHeartTracker.Deselect();
                else CastleHeartTracker.Select(r.SaveKey);
            }

            GUILayout.Label(r.CurrentRemainingText, MenuTheme.LabelStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label(r.ExpiresAtText, MenuTheme.LabelStyle, GUILayout.Width(100));
            GUILayout.Label(r.LastUpdatedText, MenuTheme.LabelStyle, GUILayout.Width(72));
            GUILayout.EndHorizontal();
        }
    }

    private void DrawPlayersTab()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"Known ({PlayerDatabase.RecordCount})", _activePlayerSubTab == 0 ? MenuTheme.SubTabActiveStyle : MenuTheme.SubTabInactiveStyle))
            _activePlayerSubTab = 0;
        if (GUILayout.Button($"Online ({Patches.SocialMenuPatch.SteamIdToName.Count})", _activePlayerSubTab == 1 ? MenuTheme.SubTabActiveStyle : MenuTheme.SubTabInactiveStyle))
            _activePlayerSubTab = 1;
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        if (_activePlayerSubTab == 0)
            DrawKnownPlayersSubTab();
        else
            DrawOnlinePlayersSubTab();
    }

    private void DrawKnownPlayersSubTab()
    {
        var records = PlayerDatabase.GetAllRecords();

        GUILayout.Label($"Known players: {records.Count}", MenuTheme.LabelStyle);

        // Editing bar
        if (_editingSteamId != 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Type name:", MenuTheme.LabelStyle, GUILayout.Width(75));
            GUILayout.Label($"<color=#ffcc44>{_editingName}_</color>", MenuTheme.HeaderStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("OK", MenuTheme.ButtonStyle, GUILayout.Width(40)))
            {
                if (!string.IsNullOrEmpty(_editingName))
                    PlayerDatabase.SetCustomOriginalName(_editingSteamId, _editingName);
                _editingSteamId = 0;
            }
            if (GUILayout.Button("X", MenuTheme.ButtonStyle, GUILayout.Width(30)))
                _editingSteamId = 0;
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(2);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Current", MenuTheme.SubHeaderStyle, GUILayout.Width(160));
        GUILayout.Label("Original", MenuTheme.SubHeaderStyle, GUILayout.Width(160));
        GUILayout.Label("Steam ID", MenuTheme.SubHeaderStyle, GUILayout.ExpandWidth(true));
        GUILayout.Label("", MenuTheme.SubHeaderStyle, GUILayout.Width(40));
        GUILayout.EndHorizontal();

        if (records.Count == 0)
        {
            GUILayout.Space(8);
            GUILayout.Label("No players recorded yet — join a server to start tracking.", MenuTheme.LabelStyle);
            return;
        }

        // Scrollable list — HeartRowStyle padding(4+4) + margin(1+1) + tallest child ~ButtonStyle(24px) ≈ 34px
        const float rowH = 34f;
        // Window padding(24+10) + tab bar(26) + Space(4) + subtab bar(24) + Space(4) + label(22) + editing bar(0 or 34) + Space(2) + col headers(26) + range(26) + buffer
        var approxHeaderH = _editingSteamId != 0 ? 212f : 178f;
        var visibleH = _windowRect.height - approxHeaderH;

        // Snapshot on Layout to keep control count consistent across IMGUI passes
        if (Event.current.type == EventType.Layout || _cachedPlayerFiltered == null)
        {
            _cachedPlayerFiltered = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ulong, PlayerDatabase.PlayerRecord>>();
            foreach (var kvp in records)
                _cachedPlayerFiltered.Add(kvp);
            _cachedPlayerFiltered.Sort((a, b) =>
            {
                var aOnline = a.Value.Names.Exists(n => PlayerDatabase.IsCurrentlyConnected(n));
                var bOnline = b.Value.Names.Exists(n => PlayerDatabase.IsCurrentlyConnected(n));
                if (aOnline != bOnline) return aOnline ? -1 : 1;
                var aName = a.Value.Names.Count > 0 ? a.Value.Names[0] : "";
                var bName = b.Value.Names.Count > 0 ? b.Value.Names[0] : "";
                return string.Compare(aName, bName, System.StringComparison.OrdinalIgnoreCase);
            });
        }
        var filtered = _cachedPlayerFiltered;

        var maxScroll = Mathf.Max(0f, filtered.Count * rowH - visibleH);

        // Smooth scrolling: scroll wheel adjusts target, lerp towards it
        var ev = Event.current;
        if (ev.type == EventType.ScrollWheel)
        {
            _playerScrollTarget = Mathf.Clamp(_playerScrollTarget + ev.delta.y * rowH * 3f, 0f, maxScroll);
            ev.Use();
        }
        _playerScrollTarget = Mathf.Clamp(_playerScrollTarget, 0f, maxScroll);
        // Only update scroll position during Layout to keep controls consistent across IMGUI passes
        if (ev.type == EventType.Layout)
        {
            _playerScrollPos.y = Mathf.Lerp(_playerScrollPos.y, _playerScrollTarget, Time.deltaTime * 15f);
            if (Mathf.Abs(_playerScrollPos.y - _playerScrollTarget) < 0.5f)
                _playerScrollPos.y = _playerScrollTarget;
        }

        var firstVisible = Mathf.Max(0, Mathf.FloorToInt(_playerScrollPos.y / rowH));
        var lastVisible = Mathf.Min(filtered.Count - 1, firstVisible + Mathf.CeilToInt(visibleH / rowH));

        if (filtered.Count > 0)
            GUILayout.Label($"  {firstVisible + 1}\u2013{lastVisible + 1} of {filtered.Count}", MenuTheme.SubHeaderStyle);

        // Virtual scrolling: only render visible rows, offset first row for smooth sub-row scroll
        var partialOffset = _playerScrollPos.y % rowH;
        if (partialOffset > 0f)
            GUILayout.Space(-partialOffset);

        for (var i = firstVisible; i <= lastVisible; i++)
        {
            var kvp = filtered[i];
            var steamId = kvp.Key;
            var record = kvp.Value;
            var currentName = PlayerDatabase.GetCurrentName(steamId);
            var isOnline = currentName != null;
            var originalName = record.Names.Count > 0 ? record.Names[0] : "?";
            currentName ??= originalName; // fallback if offline: show original
            var isEditing = _editingSteamId == steamId;

            GUILayout.BeginHorizontal(MenuTheme.HeartRowStyle(i));

            // Current name
            var nameColor = isOnline ? "<color=#88ff88>" : "<color=#cccccc>";
            GUILayout.Label($"{nameColor}{currentName}</color>", MenuTheme.LabelStyle, GUILayout.Width(160));

            // Original name (yellow if different)
            var origColor = originalName != currentName ? "<color=#ffcc44>" : nameColor;
            GUILayout.Label($"{origColor}{originalName}</color>", MenuTheme.LabelStyle, GUILayout.Width(160));

            // Steam ID (clickable — opens Steam profile)
            if (GUILayout.Button(steamId.ToString(), MenuTheme.LabelStyle, GUILayout.ExpandWidth(true)))
                Application.OpenURL($"https://steamcommunity.com/profiles/{steamId}");

            // Edit button
            if (GUILayout.Button(isEditing ? "..." : "\u270E", MenuTheme.ButtonStyle, GUILayout.Width(40)))
            {
                if (isEditing)
                    _editingSteamId = 0;
                else
                {
                    _editingSteamId = steamId;
                    _editingName = originalName;
                }
            }

            GUILayout.EndHorizontal();
        }
    }

    private void DrawOnlinePlayersSubTab()
    {
        // Snapshot data on Layout to keep control count consistent between Layout and Repaint
        if (Event.current.type == EventType.Layout || _cachedOnlinePlayers == null)
        {
            _cachedOnlinePlayers = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ulong, string>>();
            foreach (var kvp in Patches.SocialMenuPatch.SteamIdToName)
                _cachedOnlinePlayers.Add(kvp);
            _cachedOnlinePlayers.Sort((a, b) => string.Compare(a.Value, b.Value, System.StringComparison.OrdinalIgnoreCase));
        }
        var onlinePlayers = _cachedOnlinePlayers;

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Online: {onlinePlayers.Count}", MenuTheme.LabelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Editing bar
        if (_editingSteamId != 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Type name:", MenuTheme.LabelStyle, GUILayout.Width(75));
            GUILayout.Label($"<color=#ffcc44>{_editingName}_</color>", MenuTheme.HeaderStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("OK", MenuTheme.ButtonStyle, GUILayout.Width(40)))
            {
                if (!string.IsNullOrEmpty(_editingName))
                    PlayerDatabase.SetCustomOriginalName(_editingSteamId, _editingName);
                _editingSteamId = 0;
            }
            if (GUILayout.Button("X", MenuTheme.ButtonStyle, GUILayout.Width(30)))
                _editingSteamId = 0;
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(2);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Current", MenuTheme.SubHeaderStyle, GUILayout.Width(160));
        GUILayout.Label("Original", MenuTheme.SubHeaderStyle, GUILayout.Width(160));
        GUILayout.Label("Steam ID", MenuTheme.SubHeaderStyle, GUILayout.ExpandWidth(true));
        GUILayout.Label("", MenuTheme.SubHeaderStyle, GUILayout.Width(40));
        GUILayout.EndHorizontal();

        if (onlinePlayers.Count == 0)
        {
            GUILayout.Space(8);
            GUILayout.Label("No players currently online.", MenuTheme.LabelStyle);
            return;
        }

        // Scrollable list — HeartRowStyle padding(4+4) + margin(1+1) + tallest child ~ButtonStyle(24px) ≈ 34px
        const float rowH = 34f;
        // Window padding(24+10) + tab bar(26) + Space(4) + subtab bar(24) + Space(4) + online row(22) + editing bar(0 or 34) + Space(2) + col headers(26) + range(26) + buffer
        var approxHeaderH = _editingSteamId != 0 ? 212f : 178f;
        var visibleH = _windowRect.height - approxHeaderH;
        var maxScroll = Mathf.Max(0f, onlinePlayers.Count * rowH - visibleH);

        var ev = Event.current;
        if (ev.type == EventType.ScrollWheel)
        {
            _onlineScrollTarget = Mathf.Clamp(_onlineScrollTarget + ev.delta.y * rowH * 3f, 0f, maxScroll);
            ev.Use();
        }
        _onlineScrollTarget = Mathf.Clamp(_onlineScrollTarget, 0f, maxScroll);
        // Only update scroll position during Layout to keep controls consistent across IMGUI passes
        if (ev.type == EventType.Layout)
        {
            _onlineScrollPos.y = Mathf.Lerp(_onlineScrollPos.y, _onlineScrollTarget, Time.deltaTime * 15f);
            if (Mathf.Abs(_onlineScrollPos.y - _onlineScrollTarget) < 0.5f)
                _onlineScrollPos.y = _onlineScrollTarget;
        }

        var firstVisible = Mathf.Max(0, Mathf.FloorToInt(_onlineScrollPos.y / rowH));
        var lastVisible = Mathf.Min(onlinePlayers.Count - 1, firstVisible + Mathf.CeilToInt(visibleH / rowH));

        if (onlinePlayers.Count > 0)
            GUILayout.Label($"  {firstVisible + 1}\u2013{lastVisible + 1} of {onlinePlayers.Count}", MenuTheme.SubHeaderStyle);

        var partialOffset = _onlineScrollPos.y % rowH;
        if (partialOffset > 0f)
            GUILayout.Space(-partialOffset);

        for (var i = firstVisible; i <= lastVisible; i++)
        {
            var kvp = onlinePlayers[i];
            var steamId = kvp.Key;
            var currentName = kvp.Value;
            var record = PlayerDatabase.GetRecord(steamId);
            var originalName = record != null && record.Names.Count > 0 ? record.Names[0] : "?";
            var isEditing = _editingSteamId == steamId;

            GUILayout.BeginHorizontal(MenuTheme.HeartRowStyle(i));

            // Current name (always green — they're online)
            GUILayout.Label($"<color=#88ff88>{currentName}</color>", MenuTheme.LabelStyle, GUILayout.Width(160));

            // Original name (yellow if different)
            var origColor = originalName != currentName ? "<color=#ffcc44>" : "<color=#88ff88>";
            GUILayout.Label($"{origColor}{originalName}</color>", MenuTheme.LabelStyle, GUILayout.Width(160));

            // Steam ID (clickable — opens Steam profile)
            if (GUILayout.Button(steamId.ToString(), MenuTheme.LabelStyle, GUILayout.ExpandWidth(true)))
                Application.OpenURL($"https://steamcommunity.com/profiles/{steamId}");

            // Edit button
            if (GUILayout.Button(isEditing ? "..." : "\u270E", MenuTheme.ButtonStyle, GUILayout.Width(40)))
            {
                if (isEditing)
                    _editingSteamId = 0;
                else
                {
                    _editingSteamId = steamId;
                    _editingName = originalName;
                }
            }

            GUILayout.EndHorizontal();
        }
    }

    // ===================== Debug =====================

    private static void LogAllPlayerBuffs()
    {
        if (!Plugin.IsInGame)
        {
            Plugin.Logger.LogWarning("[DebugBuffs] Not in game.");
            return;
        }

        if (EntityList.Players.IsEmpty)
        {
            Plugin.Logger.LogInfo("[DebugBuffs] No player entities found.");
            return;
        }

        var entities = EntityList.Players.ToEntityArray(Allocator.Temp);
        try
        {
            Plugin.Logger.LogInfo($"[DebugBuffs] === Player Buff Dump — {entities.Length} player(s) ===");
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!entity.Exists()) continue;

                var playerName = entity.TryGetComponent<PlayerCharacter>(out var pc)
                    ? pc.Name.ToString()
                    : $"Entity({entity.Index}:{entity.Version})";

                if (!entity.HasBuffer<BuffBuffer>())
                {
                    Plugin.Logger.LogInfo($"[DebugBuffs]  Player '{playerName}' — no BuffBuffer");
                    continue;
                }

                var buffs = entity.ReadBuffer<BuffBuffer>();
                Plugin.Logger.LogInfo($"[DebugBuffs]  Player '{playerName}' — {buffs.Length} buff(s):");
                for (var j = 0; j < buffs.Length; j++)
                {
                    var buffEntry = buffs[j];
                    var buffGuid = buffEntry.PrefabGuid;
                    var buffName = VWorld.PrefabLookupMap.GetName(buffGuid);
                    var buffEntity = buffEntry.Entity;

                    var stackInfo = "";
                    if (buffEntity != Entity.Null && buffEntity.Exists() && buffEntity.TryGetComponent<Buff>(out var buff))
                        stackInfo = $" stacks={buff.Stacks}";

                    Plugin.Logger.LogInfo($"[DebugBuffs]    [{j}] '{buffName}' hash={buffGuid.GuidHash}{stackInfo}");
                }
            }
            Plugin.Logger.LogInfo("[DebugBuffs] === End ===");
        }
        finally
        {
            entities.Dispose();
        }
    }

    private static void LogLocalAbilityBar()
    {
        if (!Plugin.IsInGame)
        {
            Plugin.Logger.LogWarning("[DebugAbilityBar] Not in game.");
            return;
        }

        var local = EntityList.LocalCharacter;
        if (local == Entity.Null || !local.Exists())
        {
            Plugin.Logger.LogWarning("[DebugAbilityBar] No local character.");
            return;
        }

        if (!local.HasBuffer<AbilityGroupSlotBuffer>())
        {
            Plugin.Logger.LogWarning("[DebugAbilityBar] Local character has no AbilityGroupSlotBuffer.");
            return;
        }

        var slots = local.ReadBuffer<AbilityGroupSlotBuffer>();
        Plugin.Logger.LogInfo($"[DebugAbilityBar] === Ability Bar Dump — {slots.Length} slot(s) ===");
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            var baseName = VWorld.PrefabLookupMap.GetName(slot.BaseAbilityGroupOnSlot);
            var baseHash = slot.BaseAbilityGroupOnSlot.GuidHash;

            var slotEntityResolved = slot.GroupSlotEntity._Entity;
            var slotEntityInfo = "null";
            var groupGuidInfo = "";

            if (slotEntityResolved != Entity.Null && slotEntityResolved.Exists())
            {
                slotEntityInfo = $"Entity({slotEntityResolved.Index}:{slotEntityResolved.Version})";

                if (slotEntityResolved.TryGetComponent<AbilityGroupSlot>(out var groupSlot))
                {
                    var currentGuid = groupSlot.GroupGuid.Value;
                    var currentName = VWorld.PrefabLookupMap.GetName(currentGuid);
                    groupGuidInfo = $" currentAbility='{currentName}' currentHash={currentGuid.GuidHash} slotId={groupSlot.SlotId}";
                }
                else
                {
                    slotEntityInfo += " (no AbilityGroupSlot component)";
                }
            }

            Plugin.Logger.LogInfo(
                $"[DebugAbilityBar]  [{i}] base='{baseName}' baseHash={baseHash} show={slot.ShowOnBar}" +
                $" slotEntity={slotEntityInfo}{groupGuidInfo}");
        }

        Plugin.Logger.LogInfo("[DebugAbilityBar] === End ===");
    }

    // ===================== Reusable Widgets =====================

    private static void Header(string label) => GUILayout.Label(label, MenuTheme.HeaderStyle);
    private static void SubHeader(string label) => GUILayout.Label(label, MenuTheme.SubHeaderStyle);

    private static bool FullButton(string label) =>
        GUILayout.Button(label, MenuTheme.ButtonStyle, GUILayout.ExpandWidth(true));

    // --- Toggles: clear "☑ / ☐ Label" rows ---

    private static void Toggle(ConfigEntry<bool> entry, string text) => Toggle(entry, new GUIContent(text, null, ""));

    private static void Toggle(ConfigEntry<bool> entry, GUIContent content)
    {
        if (DrawToggleRow(entry.Value, content)) entry.Value = !entry.Value;
    }

    private static void Toggle(Config.FeatureConfig config, string text) => Toggle(config, new GUIContent(text, null, ""));

    private static void Toggle(Config.FeatureConfig config, GUIContent content)
    {
        if (DrawToggleRow(config.Enabled, content)) config.Enabled = !config.Enabled;
    }

    private static bool DrawToggleRow(bool value, GUIContent content)
    {
        var label = (value ? "\u2611  " : "\u2610  ") + content.text;
        var c = new GUIContent(label, content.image, content.tooltip);
        var style = value ? MenuTheme.ToggleOnStyle : MenuTheme.ToggleStyle;
        return GUILayout.Button(c, style, GUILayout.ExpandWidth(true));
    }

    // --- Radio Group: "Label  (•) A   ( ) B   ( ) C" ---

    private static void RadioGroup(string label, string[] options, ConfigEntry<int> entry)
    {
        var idx = ((entry.Value % options.Length) + options.Length) % options.Length;
        var picked = DrawRadioGroup(label, options, idx);
        if (picked >= 0 && picked != idx) entry.Value = picked;
    }

    private static void RadioGroup(string label, string[] options, Config.FeatureConfig config)
    {
        var idx = ((config.Option % options.Length) + options.Length) % options.Length;
        var picked = DrawRadioGroup(label, options, idx);
        if (picked >= 0 && picked != idx) config.Option = picked;
    }

    private static int DrawRadioGroup(string label, string[] options, int currentIndex)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, MenuTheme.LabelStyle, GUILayout.Width(LabelWidth));
        var picked = -1;
        for (var i = 0; i < options.Length; i++)
        {
            var on = i == currentIndex;
            var style = on ? MenuTheme.RadioOnStyle : MenuTheme.RadioStyle;
            var prefix = on ? "\u25C9 " : "\u25CB "; // ◉ / ○
            if (GUILayout.Button(prefix + options[i], style, GUILayout.ExpandWidth(true)))
                picked = i;
        }
        GUILayout.EndHorizontal();
        return picked;
    }

    // --- Dropdown row: "Label  [ Selected ▾ ]" ---

    private static void DropdownRow(string label, string[] options, ConfigEntry<int> entry)
    {
        var idx = ((entry.Value % options.Length) + options.Length) % options.Length;
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, MenuTheme.LabelStyle, GUILayout.Width(LabelWidth));
        if (GUILayout.Button(options[idx] + "   \u25BE", MenuTheme.DropdownButtonStyle, GUILayout.ExpandWidth(true)))
        {
            var lastRect = GUILayoutUtility.GetLastRect();
            var screenPos = WindowPointToScreen(new Vector2(lastRect.x, lastRect.yMax + 2));
            Popups.OpenDropdown(entry.GetHashCode(), options, idx, i => entry.Value = i, screenPos);
        }
        GUILayout.EndHorizontal();
    }

    // --- Slider row ---

    private static void SliderRow(string label, ConfigEntry<float> entry, float min, float max, string format = "F0", string suffix = "")
    {
        entry.Value = DrawSliderRow(label, entry.Value, min, max, format, suffix);
    }

    private static void SliderRow(string label, Config.FeatureConfig config, float min, float max, string format = "F0", string suffix = "")
    {
        config.MinimumQuality = DrawSliderRow(label, config.MinimumQuality, min, max, format, suffix);
    }

    private static float DrawSliderRow(string label, float value, float min, float max, string format, string suffix)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, MenuTheme.LabelStyle, GUILayout.Width(LabelWidth));
        value = GUILayout.HorizontalSlider(value, min, max, MenuTheme.HSliderStyle, MenuTheme.HSliderThumbStyle, GUILayout.ExpandWidth(true));
        GUILayout.Label(value.ToString(format) + suffix, MenuTheme.ValueLabelStyle, GUILayout.Width(ValueWidth));
        GUILayout.EndHorizontal();
        return value;
    }

    // --- KeyBind row ---

    private static void KeyBindRow(string label, ConfigEntry<KeyCode> entry)
    {
        KeyBindSystem.KeyBindButton(label + ":", entry);
    }

    // --- ESP entry: toggle + small color swatch + optional quality slider ---

    private static void EspRow(string sectionName, Config.FeatureConfig config)
    {
        GUILayout.BeginHorizontal();
        if (DrawToggleRow(config.Enabled, new GUIContent(sectionName, null, "")))
            config.Enabled = !config.Enabled;

        // Color swatch button — opens the color picker popup
        var swatchStyle = MenuTheme.GetSwatchStyle(config.Color);
        if (GUILayout.Button(new GUIContent("", null, "Color: " + ColorOptions.GetColorName(config.Color)),
                swatchStyle, GUILayout.Width(SwatchSize), GUILayout.Height(SwatchSize)))
        {
            var lastRect = GUILayoutUtility.GetLastRect();
            var screenPos = WindowPointToScreen(new Vector2(lastRect.xMax - 20, lastRect.yMax + 4));
            // Pin to FeatureConfig identity
            var owner = config.GetHashCode();
            Popups.OpenColorPicker(owner, config.Color, idx => config.Color = idx, screenPos);
        }
        GUILayout.EndHorizontal();

        if (config.MinimumQuality != 0f)
            SliderRow("  Min. Quality", config, 1f, 100f, "F0", "%");
    }

    private void DrawHighQualityBloodTypeFilter()
    {
        var filterEntry = Config.ESP.HighQualityBloodTypeFilter;
        if (filterEntry.Value < 0 || filterEntry.Value >= BloodTypes.FilterOptions.Length)
            filterEntry.Value = 0;

        // Indent slightly so it looks like a sub-option of "High Quality Blood"
        GUILayout.BeginHorizontal();
        GUILayout.Space(12);
        GUILayout.BeginVertical();
        DropdownRow("Blood Type", BloodTypes.FilterOptions, filterEntry);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void ShowTooltip()
    {
        if (string.IsNullOrEmpty(GUI.tooltip)) return;
        var mousePos = Event.current.mousePosition;
        var tooltipContent = new GUIContent(GUI.tooltip, null, "");
        var tooltipSize = MenuTheme.TooltipStyle.CalcSize(tooltipContent);
        GUI.Label(new Rect(mousePos.x, mousePos.y + 25, tooltipSize.x, tooltipSize.y), tooltipContent, MenuTheme.TooltipStyle);
    }

    // --- helpers ---

    private static Vector2 WindowPointToScreen(Vector2 windowPoint)
    {
        // _windowRect position + window-local point ≈ screen point
        return new Vector2(_windowRect.x + windowPoint.x, _windowRect.y + windowPoint.y);
    }
}
