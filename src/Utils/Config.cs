using BepInEx.Configuration;
using UnityEngine;

namespace ExtrasensoryPerception.Utils;

internal static class Config
{
    private static ConfigFile ConfigFile => Plugin.Instance.Config;

    internal class FeatureConfig
    {
        private ConfigEntry<bool> StatusEntry { get; }
        private ConfigEntry<int>? ColorEntry { get; }
        private ConfigEntry<int>? OptionEntry { get; }
        private ConfigEntry<float>? QualityEntry { get; }

        internal bool Enabled
        {
            get => StatusEntry.Value;
            set => StatusEntry.Value = value;
        }

        internal int Color
        {
            get => ColorEntry!.Value;
            set => ColorEntry!.Value = value;
        }
        
        internal int Option
        {
            get => OptionEntry!.Value;
            set => OptionEntry!.Value = value;
        }

        internal float MinimumQuality
        {
            get => QualityEntry?.Value ?? 0f;
            set => QualityEntry!.Value = value;
        }

        internal FeatureConfig(string section, string key, int defaultColor, float minQuality, string? displayName = null)
        {
            var configName = displayName ?? key;
            StatusEntry = ConfigFile.Bind(section, key, false, $"Enable/disable {configName}.");
            ColorEntry = ConfigFile.Bind(section, $"{key}Color", defaultColor, $"{configName} color index.");
            if (minQuality != 0) QualityEntry = ConfigFile.Bind(section, $"{key}Quality", minQuality, $"{configName} minimum quality.");
        }
        
        internal FeatureConfig(string section, string key, int option, string? displayName = null)
        {
            var configName = displayName ?? key;
            StatusEntry = ConfigFile.Bind(section, key, false, $"Enable/disable {configName}");
            OptionEntry = ConfigFile.Bind(section, $"{key}Option", option, $"{configName} selected option.");
        }


        internal FeatureConfig(string section, string key, string? displayName = null)
        {
            var configName = displayName ?? key;
            StatusEntry = ConfigFile.Bind(section, key, false, $"Enable/disable {configName}.");
        }
    }

    internal static readonly ConfigEntry<bool> ModToggle = ConfigFile.Bind("Options", "Enabled", false, "Toggle the mod.");
    internal static readonly ConfigEntry<KeyCode> MenuKey = ConfigFile.Bind("Options", "MenuKey", KeyCode.Insert, "Toggle the menu.");

    internal static class Aimbot
    {
        internal static bool Enabled => Status.Enabled;
        internal static readonly FeatureConfig Status = new("Aimbot", "Enabled");
        internal static readonly FeatureConfig Players = new("Aimbot", "Players");
        internal static readonly FeatureConfig Bosses = new("Aimbot", "Bosses");
        internal static readonly FeatureConfig Mobs = new("Aimbot", "Mobs");
        internal static readonly FeatureConfig DrawAimPosition = new("Aimbot", "DrawAimPosition");
        internal static readonly ConfigEntry<int> Mode = ConfigFile.Bind("Aimbot", "Mode", 0, "Aimbot mode (Hold/Toggle).");
        internal static readonly ConfigEntry<KeyCode> Key = ConfigFile.Bind("Aimbot", "Key", KeyCode.Mouse4, "Aimbot triggering key.");
        internal static readonly ConfigEntry<KeyCode> Key2 = ConfigFile.Bind("Aimbot", "Key2", KeyCode.None, "Secondary aimbot triggering key.");
        
        // Limits
        internal static readonly ConfigEntry<float> MaxDistance = ConfigFile.Bind("Aimbot", "MaxDistance", 15f, "Max target distance.");
        internal static readonly ConfigEntry<float> MaxCursorDistance = ConfigFile.Bind("Aimbot", "MaxCursorDistance", 500f, "Max target distance from cursor.");
        internal static readonly ConfigEntry<float> SwitchCooldown = ConfigFile.Bind("Aimbot", "SwitchCooldown", 0.2f, "Minimum time between target switch.");
        
        // Weights
        internal static readonly ConfigEntry<float> DistanceWeight = ConfigFile.Bind("Aimbot", "Distance", 0.5f, "How much its distance (from player) affects target selection.");
        internal static readonly ConfigEntry<float> CursorDistanceWeight = ConfigFile.Bind("Aimbot", "CursorDistance", 0.75f, "How much its distance (from cursor) affects target selection.");
        internal static readonly ConfigEntry<float> HealthWeight = ConfigFile.Bind("Aimbot", "Health", 0.25f, "How much its health affects target selection.");
        internal static readonly ConfigEntry<float> EntityTypeWeight = ConfigFile.Bind("Aimbot", "Entity", 1.0f, "How much its type affects target selection.");
    }

    internal static class ESP
    {
        internal static readonly ConfigEntry<int> HighQualityBloodTypeFilter = ConfigFile.Bind("ESP", "BloodSourcesTypeFilter", 0, "High quality blood type filter.");
        internal static readonly ConfigEntry<float> FontScale = ConfigFile.Bind("ESP", "FontScale", 1.20f, "Multiplier applied to ESP label font size and line spacing. 1.0 = previous default.");
        internal static readonly FeatureConfig Boxes = new("ESP", "Boxes", 0);
        internal static readonly FeatureConfig UseTMP = new("ESP", "UseTMP");
        internal static readonly FeatureConfig Outlines = new("ESP", "Outlines", 1);
        internal static readonly FeatureConfig Players = new("ESP", "Players", 1, 0);
        internal static readonly ConfigEntry<bool> PlayerName = ConfigFile.Bind("ESP", "PlayerName", true, "Show player name in ESP.");
        internal static readonly ConfigEntry<bool> PlayerGearLevel = ConfigFile.Bind("ESP", "PlayerGearLevel", true, "Show player gear level in ESP.");
        internal static readonly ConfigEntry<bool> PlayerHP = ConfigFile.Bind("ESP", "PlayerHP", true, "Show player HP in ESP.");
        internal static readonly ConfigEntry<bool> AlwaysShowPlayerHUD = ConfigFile.Bind("ESP", "AlwaysShowPlayerHUD", true, "Keep the native player name/level/HP bar visible at any distance, behind cover, and through stealth/invisibility (as long as the player is on screen).");
        internal static readonly ConfigEntry<bool> HideAdminObservers = ConfigFile.Bind("ESP", "HideAdminObservers", true, "Hide players with the Admin Observe Invisible buff from ESP and native HUD.");
        internal static readonly FeatureConfig MinimapPlayers = new("ESP", "MinimapPlayers", 1, 0);
        internal static readonly FeatureConfig VBloodCarriers = new("ESP", "VBloodCarriers", 5, 0);
        internal static readonly FeatureConfig HighQualityBlood = new("ESP", "BloodSources", 6, 90f, "High Quality Blood");
        internal static readonly FeatureConfig GateBosses = new("ESP", "GateBosses", 8, 0);
        internal static readonly FeatureConfig Items = new("ESP", "Items", 0, 0);
        internal static readonly FeatureConfig Containers = new("ESP", "Containers", 10, 0);
        internal static readonly FeatureConfig Ores = new("ESP", "Ores", 19, 0);
        internal static readonly FeatureConfig Plants = new("ESP", "Plants", 15, 0);
        internal static readonly FeatureConfig FishingSpots = new("ESP", "FishingSpots", 13, 0);
        internal static readonly FeatureConfig Horses = new("ESP", "Horses", 16, 90f);
        internal static readonly FeatureConfig Servants = new("ESP", "Servants", 4, 0);
        internal static readonly FeatureConfig Carriages = new("ESP", "Carriages", 7, 0);
        internal static readonly FeatureConfig CastleHearts = new("ESP", "CastleHearts", 2, 0);
    }

    internal static class Extras
    {
        internal static readonly FeatureConfig AutoFishing = new("Extras", "AutoFishing");
        internal static readonly FeatureConfig AutoLoot = new("Extras", "AutoLoot");
        internal static readonly FeatureConfig NoFog = new("Extras", "NoFog");
        internal static readonly ConfigEntry<int> LogAbilityCasts = ConfigFile.Bind("Extras", "LogAbilityCasts", 0, "Log ability casts to the BepInEx console. 0 = off, 1 = local player only, 2 = all casters (noisy).");

        internal static readonly FeatureConfig AutoCounter = new("Extras", "AutoCounter");
        internal static readonly ConfigEntry<bool> AutoCounterSlasherE = ConfigFile.Bind("Extras", "AutoCounterSlasherE", true, "Counter slashers' Camouflage Secondary (E) — cone dash + 120° swing.");
        internal static readonly ConfigEntry<bool> AutoCounterSlasherQ = ConfigFile.Bind("Extras", "AutoCounterSlasherQ", true, "Counter slashers' ElusiveStrike Dash (Q) — forward dash hit box corridor.");
        internal static readonly ConfigEntry<bool> AutoCounterWhipQ = ConfigFile.Bind("Extras", "AutoCounterWhipQ", true, "Counter whip Dash (Q) — 5m dash + 3.5m circle AoE at landing.");
        internal static readonly ConfigEntry<bool> AutoCounterSwordE = ConfigFile.Bind("Extras", "AutoCounterSwordE", true, "Counter sword Shockwave (E) — 14m forward projectile (radius 0.45m).");
        internal static readonly ConfigEntry<bool> AutoCounterSpearQ = ConfigFile.Bind("Extras", "AutoCounterSpearQ", true, "Counter spear AThousandSpears Stab (Q) — forward box thrust.");
        internal static readonly ConfigEntry<bool> AutoCounterTwinbladeE = ConfigFile.Bind("Extras", "AutoCounterTwinbladeE", true, "Counter twinblade SweepingStrike (E) — forward lunge + line swing.");
        internal static readonly ConfigEntry<bool> AutoCounterReaperQ = ConfigFile.Bind("Extras", "AutoCounterReaperQ", true, "Counter reaper TendonSwing Twist (Q) — 3m circle AoE around caster.");
        internal static readonly ConfigEntry<bool> AutoCounterPistolPrimary = ConfigFile.Bind("Extras", "AutoCounterPistolPrimary", true, "Counter pistol Primary Attack — 8m forward projectile (radius 0.5m).");
        internal static readonly ConfigEntry<bool> AutoCounterPistolE = ConfigFile.Bind("Extras", "AutoCounterPistolE", true, "Counter pistol ExplosiveShot (E) — Shot follow-up projectile 8m (radius 0.6m); ignores the dash cast.");
        internal static readonly ConfigEntry<bool> AutoCounterCrossbowSnapshot = ConfigFile.Bind("Extras", "AutoCounterCrossbowSnapshot", true, "Counter crossbow Snapshot — 12m forward projectile (radius 0.5m).");
        internal static readonly ConfigEntry<bool> AutoCounterCrossbowPrimary = ConfigFile.Bind("Extras", "AutoCounterCrossbowPrimary", true, "Counter crossbow Primary Attack — 12m forward projectile; decides near windup end (remote spawn does not replicate).");
        internal static readonly ConfigEntry<bool> AutoCounterUseCounters = ConfigFile.Bind("Extras", "AutoCounterUseCounters", true, "Use parry-style counter abilities (BloodRite, MistTrance, Discharge) when auto-countering.");
        internal static readonly ConfigEntry<bool> AutoCounterUseBarriers = ConfigFile.Bind("Extras", "AutoCounterUseBarriers", true, "Use damage-soaking barrier abilities (ChaosBarrier, FrostBarrier, WardOfTheDamned) when auto-countering.");
        internal static readonly ConfigEntry<KeyCode> AutoCounterSpellSlot1Key = ConfigFile.Bind("Extras", "AutoCounterSpellSlot1Key", KeyCode.R, "Key for spell slot 1. Auto-Counter presses this when a counter ability is equipped in the first spell slot. Must match your in-game keybind.");
        internal static readonly ConfigEntry<KeyCode> AutoCounterSpellSlot2Key = ConfigFile.Bind("Extras", "AutoCounterSpellSlot2Key", KeyCode.C, "Key for spell slot 2. Auto-Counter presses this when a counter ability is equipped in the second spell slot. Must match your in-game keybind.");
        internal static readonly ConfigEntry<string> AutoCounterTriggerHashes = ConfigFile.Bind("Extras", "AutoCounterTriggerHashes", "-13231823,377778793", "Comma-separated AbilityGroup or AbilityCast guidHash values that should provoke an auto-counter. Defaults: -13231823 (AB_Vampire_Slashers_Camouflage_Secondary_AbilityGroup), 377778793 (AB_Spear_AThousandSpears_Stab_AbilityGroup). Adding new hashes also requires a matching per-ability hit-arc entry in AutoCounter.DefaultHitArcs — hashes without an arc entry are skipped (we no longer guess geometry from sliders).");
        internal static readonly ConfigEntry<float> AutoCounterActivationLatencySeconds = ConfigFile.Bind("Extras", "AutoCounterActivationLatencySeconds", 0.12f, "Approximate seconds between our synthetic key press and the counter ability becoming active. The per-cast fire deadline is windup - this; pending casts whose deadline has elapsed are dropped without firing (since pressing later wouldn't parry in time, just burn the cooldown). Lower = fire later in windup; higher = fire earlier (safer activation timing). Empirically calibrated chain: SendInput → Unity poll → frame → server RTT/2 → validate → ability active.");
        internal static readonly ConfigEntry<float> AutoCounterTargetColliderRadius = ConfigFile.Bind("Extras", "AutoCounterTargetColliderRadius", 0.55f, "Approximate radius (meters) of the local player's hit collider, added as a buffer to the gate's box halfWidth/halfLength and capsule radius. HitColliderCast tests against the target's collider (not its center), so a stab whose box passes 0.5m to your side still lands on you. 0 disables the buffer (gate uses raw prefab dimensions, will miss edge hits); too high causes false-positive fires on near-misses.");
        internal static readonly ConfigEntry<bool> AutoCounterDebugLog = ConfigFile.Bind("Extras", "AutoCounterDebugLog", false, "Log distance + angle (relative to caster facing) for every nearby enemy player cast. Use this with a sparring partner to pick the right per-ability range / cone half-angle values before adding hashes to AutoCounterTriggerHashes. Independent of the AutoCounter master toggle.");
        internal static readonly ConfigEntry<float> AutoCounterDebugLogRadius = ConfigFile.Bind("Extras", "AutoCounterDebugLogRadius", 25.0f, "Only debug-log casts from casters within this many meters. Debug-only — does not affect counter gating. 0 disables the radius filter (logs every enemy cast).");

        internal static readonly FeatureConfig EnemyCooldownTracker = new("Extras", "EnemyCooldownTracker");
        internal static readonly ConfigEntry<int> EnemyCooldownTrackerDisplayMode = ConfigFile.Bind("Extras", "EnemyCooldownTrackerDisplayMode", 1, "Cooldown display style. 0 = text stack (legacy), 1 = colored pips with timers.");
        internal static readonly ConfigEntry<float> EnemyCooldownTrackerMinSeconds = ConfigFile.Bind("Extras", "EnemyCooldownTrackerMinSeconds", 2.0f, "Only show enemy cooldowns at least this many seconds long. Hides weapon-primary attack-rate noise. The cooldown values themselves are pulled from each ability prefab's AbilityCooldownData (same source the tooltip uses), so they stay accurate across patches.");
        internal static readonly ConfigEntry<string> EnemyCooldownTrackerLabelOverrides = ConfigFile.Bind("Extras", "EnemyCooldownTrackerLabelOverrides", "", "Optional label overrides. Format: hash:label,hash:label,... When omitted, labels auto-derive from the ability prefab name (e.g. AB_Blood_BloodRite_AbilityGroup -> BloodRite).");
        internal static readonly ConfigEntry<string> EnemyCooldownTrackerIgnoredHashes = ConfigFile.Bind("Extras", "EnemyCooldownTrackerIgnoredHashes", "", "Optional comma-separated AbilityGroup hashes to never display, even if their prefab cooldown is above the minimum threshold.");
        internal static readonly ConfigEntry<int> EnemyCooldownTrackerFreeCastBuffHash = ConfigFile.Bind("Extras", "EnemyCooldownTrackerFreeCastBuffHash", -650272969, "Prefab guid hash of the spell-charge buff that grants a free cast at threshold stacks (default = Buff_FreeCast_Spell). When an enemy with this buff at >= threshold stacks casts a spell, we skip the cooldown stamp because that cast didn't put the spell on cooldown.");
        internal static readonly ConfigEntry<int> EnemyCooldownTrackerFreeCastThreshold = ConfigFile.Bind("Extras", "EnemyCooldownTrackerFreeCastThreshold", 100, "Minimum buff stack count that triggers a free cast (skips cooldown). Default = 100.");

        // Per-skill rough CDR (seconds subtracted from prefab base CD).
        internal static readonly ConfigEntry<float> CdrAllCounters = ConfigFile.Bind("Extras", "CdrAllCounters", 2.0f, "Rough CDR applied to all counters and barriers.");
        internal static readonly ConfigEntry<float> CdrVeil = ConfigFile.Bind("Extras", "CdrVeil", 0f, "Rough CDR for all travel Veils.");

        internal static readonly FeatureConfig AutoRetryConnect = new("Extras", "AutoRetryConnect", "Auto-Retry Connect");
        internal static readonly ConfigEntry<float> AutoRetryDelaySeconds = ConfigFile.Bind("Extras", "AutoRetryDelaySeconds", 1.0f, "Seconds to wait before retrying a connect after Server Full rejection.");
    }

    internal static class Camera
    {
        internal static readonly FeatureConfig ExtendedZoom = new("Camera", "ExtendedZoom", "Extended Camera Zoom");
        internal static readonly ConfigEntry<float> MaxZoomDistance = ConfigFile.Bind("Camera", "MaxZoomDistance", 25f, "Maximum camera zoom-out distance. Game default is ~18. Higher = further out.");
        internal static readonly ConfigEntry<float> MinZoomDistance = ConfigFile.Bind("Camera", "MinZoomDistance", 2f, "Minimum camera zoom-in distance. Game default is ~6. Lower = closer.");
    }
}
