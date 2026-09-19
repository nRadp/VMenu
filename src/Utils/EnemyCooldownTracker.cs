using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using ExtrasensoryPerception.API;
using ExtrasensoryPerception.ESP;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using UnityEngine;

namespace ExtrasensoryPerception.Utils;

/// <summary>
/// Cast-time observation cooldown tracker for enemy players, with auto-discovered cooldowns.
///
/// On every non-local player cast we resolve the AbilityCast prefab via
/// <see cref="PrefabLookupMap.TryGetValue"/> and read <see cref="AbilityCooldownData"/> off it
/// — the same source the in-game tooltip pulls from. So the cooldown numbers stay accurate
/// across balance patches without any hardcoded table.
///
/// Resolution order: cast prefab first (e.g. <c>AB_Blood_BloodRite_Cast</c>), then walk the
/// group prefab's <see cref="AbilityGroupStartAbilitiesBuffer"/> as a fallback.
///
/// Filtering knobs (all in <c>Config.Extras</c>):
///   - <c>EnemyCooldownTrackerMinSeconds</c>: hide cooldowns shorter than this (kills
///     weapon-primary attack-rate noise).
///   - <c>EnemyCooldownTrackerLabelOverrides</c>: rename auto-derived labels.
///   - <c>EnemyCooldownTrackerIgnoredHashes</c>: explicit suppress list for noisy abilities.
///
/// Limitations:
///   - We can only learn an ability exists once the enemy has cast it at least once.
///   - We don't see talents/shards/spell-mods that reduce cooldowns. The displayed time is the
///     base cooldown from the prefab; real remaining may be shorter.
///   - State is keyed by the full <see cref="Entity"/> struct (index + version), so recycled
///     entity indices won't return stale data. <see cref="MaybePrune"/> handles cleanup on a
///     5-second cadence.
/// </summary>
internal static class EnemyCooldownTracker
{
    private struct Definition
    {
        public float Cooldown;
        public string Label;
    }

    private static readonly Dictionary<int, Definition> DiscoveredDefinitions = new();
    private static readonly Dictionary<int, string> LabelOverrides = new();
    private static readonly HashSet<int> IgnoredHashes = new();
    // Hardcoded allow-list of AbilityGroup hashes we care about — counter-relevant spells
    // (BloodRite, MistTrance, ColdSnap, Discharge), the three big damage-soaking barriers
    // (ChaosBarrier, FrostBarrier, WardOfTheDamned), travel Veils, and the two canonical
    // weapon slots (any weapon Q/E cast stamps these; icons are always Spear Q/E).
    // Display / stamp keys for weapon Q and E. Icons always resolve to Spear skills.
    private const int WeaponSlotQ = 377778793; // AB_Spear_AThousandSpears_Stab_AbilityGroup
    private const int WeaponSlotE = 830123499; // AB_Vampire_Spear_Harpoon_Throw_AbilityGroup
    private const int BuffFreeCastWeaponHash = 651199070; // Buff_FreeCast_Weapon
    // Fallback when an ability group isn't in WeaponAbilityCooldown.
    private const float WeaponCooldownDefault = 8f;

    private static readonly HashSet<int> AllowedHashes = new()
    {
        // Counters
        1191439206,    // BloodRite
        110097606,     // MistTrance
        -1000260252,   // ColdSnap
        1952703098,    // Discharge
        // Barriers
        -1016145613,   // ChaosBarrier
        1293609465,    // FrostBarrier
        -1136860480,   // WardOfTheDamned
        // Veils
        305230608,     // VeilOfBlood
        -498302954,    // VeilOfBones
        711231628,     // VeilOfChaos
        1709284795,    // VeilOfFrost
        -935015750,    // VeilOfIllusion
        -84816111,     // VeilOfStorm
        // Canonical weapon slots (icons always Spear Q/E; any weapon skill stamps these)
        WeaponSlotQ,
        WeaponSlotE,
    };

    // Any weapon Q/E AbilityGroup → canonical slot. Follow-ups / Recasts are NOT here.
    private static readonly Dictionary<int, int> WeaponCastToSlot = new()
    {
        // Q
        { -2029046970, WeaponSlotQ }, // Sword Whirlwind
        { -1968364229, WeaponSlotQ }, // Axe Frenzy
        { 1262003451, WeaponSlotQ },  // Mace CrushingBlow
        { 377778793, WeaponSlotQ },   // Spear AThousandSpears
        { -1545438316, WeaponSlotQ }, // Slashers ElusiveStrike
        { 1461754263, WeaponSlotE },  // Reaper HowlingReaper (E)
        { -316825244, WeaponSlotE },  // Reaper HowlingReaper Cast
        { 760344149, WeaponSlotQ },   // Pistols FanTheHammer (Q — ability-bar SlotId=1)
        { 1356553255, WeaponSlotQ },  // Pistols FanTheHammer Cast
        { -1142698587, WeaponSlotQ }, // Longbow MultiShot (Q)
        { 243674495, WeaponSlotQ },   // Longbow MultiShot Cast
        { 1160282797, WeaponSlotQ },  // Longbow Acrobatic (legacy)
        { -1760359784, WeaponSlotQ }, // Crossbow RainOfBolts
        { -1181502209, WeaponSlotQ }, // GreatSword Cleaver
        { -144302000, WeaponSlotQ },  // NecromancyDagger NecroticPulse
        { 149514079, WeaponSlotQ },   // Daggers RainOfDaggers (Q)
        { 579933189, WeaponSlotQ },   // Daggers RainOfDaggers Cast
        { -496335760, WeaponSlotQ },  // Claws VaultSlash
        { -1217790595, WeaponSlotQ }, // Claws VaultSlash Unholy
        { 1600917906, WeaponSlotQ },  // DualHammers Thunderclap
        { 131953285, WeaponSlotQ },   // Pollaxe Lunge
        { 89236731, WeaponSlotQ },    // TwinBlades Javelin (Q — ability-bar SlotId=1)
        { 713894552, WeaponSlotQ },   // TwinBlades Javelin Cast
        { 1080492267, WeaponSlotQ },  // TwinBlades Javelin Throw
        { -513489536, WeaponSlotQ },  // TwinBlades Javelin Projectile
        { -1623070578, WeaponSlotQ }, // TwinBlades Javelin SpellObject
        { -687991518, WeaponSlotQ },  // TwinBlades Javelin DashTriggeredBuff
        { 1420346034, WeaponSlotQ },  // Whip Dash
        { -287679019, WeaponSlotQ },  // Rapier Feint
        // E
        { 1335008684, WeaponSlotE },  // Sword Shockwave
        { -898001858, WeaponSlotE },  // Axe XStrike
        { 1121958763, WeaponSlotE },  // Mace Smack
        { 830123499, WeaponSlotE },   // Spear Harpoon
        { 1438305657, WeaponSlotE },  // Slashers Camouflage Main
        { -345652149, WeaponSlotQ },  // Reaper TendonSwing (Q)
        { 344610321, WeaponSlotQ },   // Reaper TendonSwing Cast
        { 66606146, WeaponSlotE },    // Pistols ExplosiveShot (E — barSlot=4)
        { 2098101392, WeaponSlotE },  // Pistols ExplosiveShot DashCast
        { -149514613, WeaponSlotE },  // Longbow GuidedArrow (E)
        { -1622040119, WeaponSlotE }, // Longbow GuidedArrow Cast
        { 442088166, WeaponSlotE },   // Longbow Sharpshot (legacy)
        { 477749225, WeaponSlotE },   // Crossbow Snapshot
        { -2095151729, WeaponSlotE }, // GreatSword LeapAttack
        { -2140721739, WeaponSlotE }, // GreatSword LeapAttack Cast
        { -1516407963, WeaponSlotE }, // Unarmed Secondary
        { 936540637, WeaponSlotE },   // NecromancyDagger Skeleton
        { 1398187000, WeaponSlotE },  // Daggers CallDaggers (E)
        { 1394178657, WeaponSlotE },  // Daggers CallDaggers Cast
        { -621324159, WeaponSlotE },  // Claws SkeweringLeap
        { 1957381608, WeaponSlotE },  // Claws SkeweringLeap Unholy
        { -1612983976, WeaponSlotE }, // DualHammers StormMace
        { 578807372, WeaponSlotE },   // Pollaxe SweepAndSmash
        { -1238817965, WeaponSlotE }, // TwinBlades SweepingStrike (E — barSlot=4)
        { -1474711270, WeaponSlotE }, // TwinBlades SweepingStrike Cast
        { 438453495, WeaponSlotE },   // TwinBlades SweepingStrike Melee
        { -313732628, WeaponSlotE },  // Whip Entangle
        { -1663833157, WeaponSlotE }, // Rapier Lunge
    };

    // Hardcoded weapon skill cooldowns (AbilityGroup hash → seconds). Not shown in GUI.
    private static readonly Dictionary<int, float> WeaponAbilityCooldown = new()
    {
        // Spear
        { 377778793, 8f },    // Q AThousandSpears
        { 830123499, 8f },    // E Harpoon
        // Sword
        { -2029046970, 8f },  // Q Whirlwind
        { 1335008684, 8f },   // E Shockwave
        // Axe
        { -1968364229, 8f },  // Q Frenzy
        { -898001858, 8f },   // E XStrike
        // Mace
        { 1262003451, 8f },   // Q CrushingBlow
        { 1121958763, 8f },   // E Smack
        // Slashers
        { -1545438316, 10f }, // Q ElusiveStrike
        { 1438305657, 10f },  // E Camouflage
        // Reaper — TendonSwing is Q, HowlingReaper is E (wiki / bar order)
        { -345652149, 8f },   // Q TendonSwing
        { 344610321, 8f },    // Q TendonSwing Cast
        { 1461754263, 8f },   // E HowlingReaper
        { -316825244, 8f },   // E HowlingReaper Cast
        // Pistols — FanTheHammer is Q (8s), ExplosiveShot is E (10s)
        { 760344149, 8f },    // Q FanTheHammer
        { 1356553255, 8f },   // Q FanTheHammer Cast
        { 66606146, 10f },    // E ExplosiveShot
        { 2098101392, 10f },  // E ExplosiveShot DashCast
        // Bow / Longbow — MultiShot is Q, GuidedArrow is E
        { -1142698587, 8f },  // Q MultiShot
        { 243674495, 8f },    // Q MultiShot Cast
        { 1160282797, 8f },   // Q Acrobatic (legacy)
        { -149514613, 8f },   // E GuidedArrow
        { -1622040119, 8f },  // E GuidedArrow Cast
        { 442088166, 8f },    // E Sharpshot (legacy)
        // Crossbow
        { -1760359784, 8f },  // Q RainOfBolts
        { 477749225, 8f },    // E Snapshot
        // Greatsword
        { -1181502209, 8f },  // Q Cleaver
        { -2095151729, 10f }, // E LeapAttack
        { -2140721739, 10f }, // E LeapAttack Cast
        // Daggers — RainOfDaggers is Q (10s), CallDaggers is E (8s)
        { 149514079, 10f },   // Q RainOfDaggers
        { 579933189, 10f },   // Q RainOfDaggers Cast
        { 1398187000, 8f },   // E CallDaggers
        { 1394178657, 8f },   // E CallDaggers Cast
        { -144302000, 10f },  // Q NecroticPulse
        { 936540637, 8f },    // E Skeleton
        // Claws
        { -496335760, 8f },   // Q VaultSlash
        { -1217790595, 8f },  // Q VaultSlash Unholy
        { -621324159, 10f },  // E SkeweringLeap
        { 1957381608, 10f },  // E SkeweringLeap Unholy
        // Twinblade — Javelin is Q, SweepingStrike is E
        { 89236731, 8f },     // Q Javelin
        { 713894552, 8f },    // Q Javelin Cast
        { 1080492267, 8f },   // Q Javelin Throw
        { -513489536, 8f },   // Q Javelin Projectile
        { -1623070578, 8f },  // Q Javelin SpellObject
        { -687991518, 8f },   // Q Javelin DashTriggeredBuff
        { -1238817965, 8f },  // E SweepingStrike
        { -1474711270, 8f },  // E SweepingStrike Cast
        { 438453495, 8f },    // E SweepingStrike Melee
        // Whip
        { 1420346034, 10f },  // Q Dash
        { -313732628, 10f },  // E Entangle
    };

    // Recast / follow-up AbilityGroup → slot (closes window; does NOT re-stamp CD).
    private static readonly Dictionary<int, int> WeaponRecastToSlot = new()
    {
        { 993583640, WeaponSlotE },   // Sword Shockwave Recast
        { 1250277114, WeaponSlotQ },  // Spear Impale Recast
        { -13231823, WeaponSlotE },   // Slashers Camouflage Secondary
        { -1145923288, WeaponSlotE }, // Pistols ExplosiveShot Shot (E follow-up)
        { 1913579080, WeaponSlotE },  // Pistols ExplosiveShot Shot Recast
        { 210529811, WeaponSlotQ },   // TwinBlades Javelin Recast (Q)
        { 2023216160, WeaponSlotQ },  // TwinBlades Javelin Recast Cast
        { -768614933, WeaponSlotE },  // Pollaxe SweepAndSmash Recast
        { -1209038175, WeaponSlotQ }, // Pollaxe Lunge Dash
        { -272123926, WeaponSlotQ },  // Claws VaultSlash Unholy Recast
        { -669769327, WeaponSlotQ },  // Claws VaultSlash Unholy Cast Recast
        { -1161653858, WeaponSlotQ }, // Claws VaultSlash Recast
        { -65036116, WeaponSlotQ },   // Claws VaultSlash Cast Recast
    };

    // AbilityGroup hash -> per-skill CDR config. Veils share one slider.
    private static readonly Dictionary<int, ConfigEntry<float>> SkillCdrByHash = new()
    {
        { 305230608, Config.Extras.CdrVeil },
        { -498302954, Config.Extras.CdrVeil },
        { 711231628, Config.Extras.CdrVeil },
        { 1709284795, Config.Extras.CdrVeil },
        { -935015750, Config.Extras.CdrVeil },
        { -84816111, Config.Extras.CdrVeil },
    };

    // Counter + barrier hashes that receive CdrAllCounters.
    private static readonly HashSet<int> CounterCdrHashes = new()
    {
        1191439206,   // BloodRite
        110097606,    // MistTrance
        -1000260252,  // ColdSnap
        1952703098,   // Discharge
        -1016145613,  // ChaosBarrier
        1293609465,   // FrostBarrier
        -1136860480,  // WardOfTheDamned
    };

    // Veils that grant a temporary recast (second dash). First cast must NOT start the
    // displayed CD — the ability is usable again until the recast is spent or the
    // recast buff falls off. Illusion is intentionally excluded (normal CD on first cast).
    private static readonly HashSet<int> RecastableVeilHashes = new()
    {
        711231628,     // VeilOfChaos only
    };

    // Recast AbilityGroup hash -> parent travel AbilityGroup hash.
    private static readonly Dictionary<int, int> RecastGroupToParent = new()
    {
        { 1711943933, 711231628 },   // AB_Vampire_VeilOfChaos_Recast_Group
    };

    // Recast-window buff hashes -> parent travel hash (any match means recast still available).
    // These appear AFTER a hit while the main VeilOfChaos buff is active — not on first cast.
    private static readonly Dictionary<int, int> RecastBuffToParent = new()
    {
        { -2045383141, 711231628 },  // AB_Vampire_VeilOfChaos_Recast_Buff
        { -1206989621, 711231628 },  // AB_Vampire_VeilOfChaos_Recast_Init_Buff
    };

    private const int VeilOfChaosHash = 711231628;
    private const int VeilOfChaosBuffHash = -2127493953; // AB_Vampire_VeilOfChaos_Buff (active after first dash)

    private static readonly Dictionary<Entity, Dictionary<int, double>> Stamps = new();
    // caster -> (parentHash -> first-cast server time) while the recast window is open.
    private static readonly Dictionary<Entity, Dictionary<int, double>> RecastPending = new();
    private static readonly List<Entity> PruneScratch = new();
    private static readonly List<(double Remaining, string Label)> FormatScratch = new();
    private static readonly StringBuilder FormatBuilder = new();
    private static readonly List<CooldownPip> HeadPipScratch = new();
    private static readonly List<CooldownPip> BodyPipScratch = new();
    private static readonly List<CooldownPip> FeetPipScratch = new();

    /// <summary>
    /// Structured cooldown entry for pip-based rendering.
    /// </summary>
    internal struct CooldownPip
    {
        public int AbilityHash;
        public double Remaining;
        public float TotalCooldown;
        public Color Color;
    }

    /// <summary>
    /// Fixed order and per-ability color for pip rendering. Order determines slot position
    /// (left-to-right). Colors are thematically chosen:
    ///   Veils: order -1 (leftmost; mutually exclusive travel slot); color encodes school
    ///   Counters: red (BloodRite), turquoise (MistTrance), dark blue (ColdSnap), yellow (Discharge)
    ///   Barriers: purple (ChaosBarrier), blue (FrostBarrier), green (WardOfTheDamned)
    ///   Weapons: order 8 = canonical Weapon Q/E slots (Spear icons; any weapon stamps these)
    /// </summary>
    private static readonly Dictionary<int, (int Order, Color Color)> PipMeta = new()
    {
        { 305230608,   (-1, new Color(0.85f, 0.15f, 0.25f)) },   // VeilOfBlood - Crimson
        { -498302954,  (-1, new Color(0.9f, 0.85f, 0.7f)) },     // VeilOfBones - Bone
        { 711231628,   (-1, new Color(1f, 0.45f, 0.1f)) },       // VeilOfChaos - Orange
        { 1709284795,  (-1, new Color(0.55f, 0.85f, 1f)) },      // VeilOfFrost - Ice
        { -935015750,  (-1, new Color(0.85f, 0.4f, 0.9f)) },     // VeilOfIllusion - Magenta
        { -84816111,   (-1, new Color(0.95f, 0.95f, 0.35f)) },   // VeilOfStorm - Lightning
        { 1191439206,  (0, new Color(1f, 0.2f, 0.2f)) },         // BloodRite - Red
        { 110097606,   (1, new Color(0f, 0.8f, 0.8f)) },         // MistTrance - Turquoise
        { -1000260252, (2, new Color(0.1f, 0.2f, 0.7f)) },       // ColdSnap - Dark Blue
        { 1952703098,  (3, new Color(1f, 0.9f, 0.2f)) },         // Discharge - Yellow
        { -1016145613, (4, new Color(0.6f, 0.2f, 0.8f)) },       // ChaosBarrier - Purple
        { 1293609465,  (5, new Color(0.27f, 0.53f, 1f)) },       // FrostBarrier - Blue
        { -1136860480, (6, new Color(0.2f, 0.8f, 0.2f)) },       // WardOfTheDamned - Green
        { 377778793,   (8, new Color(0.55f, 0.75f, 0.95f)) },    // Spear Q — AThousandSpears
        { 830123499,   (8, new Color(0.4f, 0.65f, 0.85f)) },     // Spear E — Harpoon
    };
    private static readonly List<(int Order, CooldownPip Pip)> PipOrderScratch = new();
    private static readonly List<CooldownPip> PipResultScratch = new();

    private static string _lastConfiguredLabelOverrides = string.Empty;
    private static string _lastConfiguredIgnoredHashes = string.Empty;
    private static EntityQuery _serverTimeQuery;
    private static bool _serverTimeQueryReady;
    private static double _lastPruneAt;
    private const double PruneIntervalSeconds = 5.0;

    /// <summary>
    /// Called from the cast-event patch for every non-local player caster. Cheap no-op when
    /// the ability has no <see cref="AbilityCooldownData"/>, its cooldown is below the
    /// threshold, or it's been suppressed by config.
    /// </summary>
    // Ability bar SlotId (from AbilityRunScriptsShared.GetSlotIndex): 1 = weapon Q, 4 = weapon E
    // (legacy docs said 2; live client returns 4 for the second weapon skill).
    private const int AbilityBarWeaponQ = 1;
    private const int AbilityBarWeaponE = 4;
    private const int AbilityBarWeaponELegacy = 2;

    internal static void OnAbilityCastObserved(Entity caster, PrefabGUID abilityGroupGuid, PrefabGUID abilityCastGuid,
        int abilityBarSlot = -1)
    {
        if (!Config.Extras.EnemyCooldownTracker.Enabled) return;
        if (caster == Entity.Null) return;

        var hash = abilityGroupGuid.GuidHash;
        RefreshConfigSets();
        if (IgnoredHashes.Contains(hash)) return;

        // Weapon skill follow-ups / recast groups — not a fresh Q/E press; CD already stamped.
        if (WeaponRecastToSlot.TryGetValue(hash, out _) ||
            WeaponRecastToSlot.TryGetValue(abilityCastGuid.GuidHash, out _) ||
            TryResolveWeaponRecastByName(abilityGroupGuid, abilityCastGuid, out _))
            return;

        // Resolve weapon Q/E: ability bar → cast slot index → hash map → name tokens.
        if (TryResolveWeaponSlot(caster, abilityGroupGuid, abilityCastGuid, abilityBarSlot,
                out var weaponSlot, out _))
        {
            HandleWeaponSlotCast(caster, weaponSlot, abilityGroupGuid, abilityCastGuid);
            return;
        }

        // Veil of Chaos recast (second dash) — stamp the PARENT travel CD now.
        if (RecastGroupToParent.TryGetValue(hash, out var parentHash))
        {
            HandleSlotRecastUsed(caster, parentHash, new PrefabGUID(parentHash), abilityCastGuid);
            return;
        }

        if (!AllowedHashes.Contains(hash)) return;
        // Weapon slots are only stamped via weapon resolution above.
        if (hash == WeaponSlotQ || hash == WeaponSlotE) return;

        if (!TryGetOrDiscoverDefinition(abilityGroupGuid, abilityCastGuid, out var def))
            return;
        if (def.Cooldown < Config.Extras.EnemyCooldownTrackerMinSeconds.Value)
            return;

        // Free-cast charge: cast does not start CD — still register as ready so the pip appears.
        if (HasSpellFreeCastReady(caster))
        {
            EnsureReadyStamp(caster, hash, def);
            return;
        }

        StampAbility(caster, hash, def.Cooldown, RecastableVeilHashes.Contains(hash));
        AbilityIconResolver.TryGetSprite(hash, out _);
    }

    /// <summary>
    /// Resolve canonical weapon Q/E stamp key.
    /// Order: ability-bar SlotId → cast slot index → hash map → name tokens on group/cast.
    /// </summary>
    private static bool TryResolveWeaponSlot(Entity caster, PrefabGUID abilityGroupGuid,
        PrefabGUID abilityCastGuid, int abilityBarSlot, out int slotHash, out string via)
    {
        via = "";
        var abilityGroupHash = abilityGroupGuid.GuidHash;

        if (TryResolveWeaponSlotFromAbilityBar(caster, abilityGroupHash, out slotHash, out var barSlotId))
        {
            via = $"ability-bar SlotId={barSlotId}";
            return true;
        }

        if (abilityBarSlot == AbilityBarWeaponQ)
        {
            slotHash = WeaponSlotQ;
            via = "cast-slot-index=1";
            return true;
        }
        if (abilityBarSlot == AbilityBarWeaponE || abilityBarSlot == AbilityBarWeaponELegacy)
        {
            slotHash = WeaponSlotE;
            via = $"cast-slot-index={abilityBarSlot}";
            return true;
        }

        if (WeaponCastToSlot.TryGetValue(abilityGroupHash, out slotHash))
        {
            via = "hash-map";
            return true;
        }

        // Some cast events put the Cast/Melee prefab in AbilityGroup — try cast hash too.
        var castHash = abilityCastGuid.GuidHash;
        if (castHash != 0 && WeaponCastToSlot.TryGetValue(castHash, out slotHash))
        {
            via = "cast-hash-map";
            return true;
        }

        if (TryResolveWeaponSlotByName(abilityGroupGuid, abilityCastGuid, out slotHash, out via))
            return true;

        return false;
    }

    private static bool TryResolveWeaponRecastByName(PrefabGUID groupGuid, PrefabGUID castGuid, out int slotHash)
    {
        slotHash = 0;
        var groupName = VWorld.PrefabLookupMap.GetName(groupGuid) ?? "";
        var castName = VWorld.PrefabLookupMap.GetName(castGuid) ?? "";
        var haystack = groupName + " " + castName;
        if (!NameHas(haystack, "Recast") &&
            !(NameHas(haystack, "Camouflage") && NameHas(haystack, "Secondary")) &&
            !NameHas(haystack, "ExplosiveShot_Shot"))
            return false;

        // Match underlying skill token with Recast allowed in the name.
        if (NameHas(haystack, "Shockwave") ||
            NameHas(haystack, "Camouflage") || NameHas(haystack, "ExplosiveShot") ||
            NameHas(haystack, "HowlingReaper") || NameHas(haystack, "LeapAttack") ||
            NameHas(haystack, "SkeweringLeap") || NameHas(haystack, "SweepAndSmash") ||
            NameHas(haystack, "Entangle") || NameHas(haystack, "Harpoon") ||
            NameHas(haystack, "Snapshot") || NameHas(haystack, "Smack") ||
            NameHas(haystack, "XStrike") || NameHas(haystack, "CallDaggers") ||
            NameHas(haystack, "SweepingStrike") || NameHas(haystack, "GuidedArrow"))
        {
            slotHash = WeaponSlotE;
            return true;
        }

        if (NameHas(haystack, "VaultSlash") || NameHas(haystack, "Impale") ||
            NameHas(haystack, "Javelin") || NameHas(haystack, "FanTheHammer") ||
            NameHas(haystack, "ElusiveStrike") || NameHas(haystack, "TendonSwing") ||
            NameHas(haystack, "Frenzy") || NameHas(haystack, "Whirlwind") ||
            NameHas(haystack, "CrushingBlow") || NameHas(haystack, "Cleaver") ||
            NameHas(haystack, "RainOfDaggers") || NameHas(haystack, "RainOfBolts") ||
            NameHas(haystack, "MultiShot"))
        {
            slotHash = WeaponSlotQ;
            return true;
        }

        return false;
    }

    private static bool IsWeaponPrimaryName(string name) =>
        !string.IsNullOrEmpty(name) &&
        name.IndexOf("Primary", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool LooksLikeWeaponSkillName(string name)
    {
        if (string.IsNullOrEmpty(name) || IsWeaponPrimaryName(name)) return false;
        return name.IndexOf("Spear", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Sword", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Axe", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Mace", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Slashers", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Reaper", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Pistol", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Longbow", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Crossbow", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("GreatSword", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Dagger", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Claw", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("TwinBlade", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Whip", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Rapier", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Pollaxe", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("DualHammer", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("SweepingStrike", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Javelin", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static readonly string[] WeaponNameTokensQ =
    [
        "Javelin", "Whirlwind", "Frenzy", "AThousandSpears", "ElusiveStrike",
        "TendonSwing", "FanTheHammer", "MultiShot", "Acrobatic", "RainOfBolts", "Cleaver",
        "RainOfDaggers", "NecroticPulse", "VaultSlash", "Thunderclap", "Feint", "CrushingBlow",
        "Whip_Dash", "Pollaxe_Lunge",
    ];

    private static readonly string[] WeaponNameTokensE =
    [
        "SweepingStrike", "Shockwave", "XStrike", "Harpoon", "Camouflage", "HowlingReaper",
        "ExplosiveShot", "GuidedArrow", "Sharpshot", "Snapshot", "LeapAttack", "CallDaggers",
        "Skeleton", "SkeweringLeap", "Entangle", "Smack", "StormMace", "SweepAndSmash",
        "Unarmed_Secondary", "Rapier_Lunge",
    ];

    private static bool TryResolveWeaponSlotByName(PrefabGUID groupGuid, PrefabGUID castGuid,
        out int slotHash, out string via)
    {
        slotHash = 0;
        via = "";
        var groupName = VWorld.PrefabLookupMap.GetName(groupGuid) ?? "";
        var castName = VWorld.PrefabLookupMap.GetName(castGuid) ?? "";
        if (IsWeaponPrimaryName(groupName) || IsWeaponPrimaryName(castName))
            return false;

        // Ignore non-weapon systems (wolf form, blood spells, etc.)
        if (NameHas(groupName, "Shapeshift") || NameHas(castName, "Shapeshift") ||
            NameHas(groupName, "AB_Blood_") || NameHas(groupName, "AB_Chaos_") ||
            NameHas(groupName, "AB_Frost_") || NameHas(groupName, "AB_Unholy_") ||
            NameHas(groupName, "AB_Storm_") || NameHas(groupName, "AB_Illusion_") ||
            NameHas(groupName, "VeilOf"))
            return false;

        if (NameHas(groupName, "Recast") || NameHas(castName, "Recast") ||
            NameHas(groupName, "Camouflage_Secondary") || NameHas(castName, "Camouflage_Secondary") ||
            NameHas(groupName, "ExplosiveShot_Shot") || NameHas(castName, "ExplosiveShot_Shot"))
            return false;

        var haystack = groupName + " " + castName;

        for (var i = 0; i < WeaponNameTokensE.Length; i++)
        {
            if (!NameHas(haystack, WeaponNameTokensE[i])) continue;
            slotHash = WeaponSlotE;
            via = $"name-E '{WeaponNameTokensE[i]}'";
            return true;
        }

        for (var i = 0; i < WeaponNameTokensQ.Length; i++)
        {
            if (!NameHas(haystack, WeaponNameTokensQ[i])) continue;
            slotHash = WeaponSlotQ;
            via = $"name-Q '{WeaponNameTokensQ[i]}'";
            return true;
        }

        if (NameHas(haystack, "TwinBlade") && NameHas(haystack, "Strike") && !NameHas(haystack, "Javelin"))
        {
            slotHash = WeaponSlotE;
            via = "name-E TwinBlade+Strike";
            return true;
        }

        return false;
    }

    private static bool NameHas(string haystack, string token) =>
        !string.IsNullOrEmpty(haystack) &&
        haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool TryResolveWeaponSlotFromAbilityBar(Entity caster, int abilityGroupHash,
        out int slotHash, out int matchedSlotId)
    {
        slotHash = 0;
        matchedSlotId = -1;
        if (!caster.HasBuffer<AbilityGroupSlotBuffer>()) return false;

        var slots = caster.ReadBuffer<AbilityGroupSlotBuffer>();
        for (var i = 0; i < slots.Length; i++)
        {
            var baseHash = slots[i].BaseAbilityGroupOnSlot.GuidHash;
            var slotEntity = slots[i].GroupSlotEntity._Entity;
            var currentHash = 0;
            var slotId = -1;
            if (slotEntity != Entity.Null && slotEntity.Exists() &&
                slotEntity.TryGetComponent<AbilityGroupSlot>(out var groupSlot))
            {
                currentHash = groupSlot.GroupGuid.Value.GuidHash;
                slotId = groupSlot.SlotId;
            }

            if (baseHash != abilityGroupHash && currentHash != abilityGroupHash)
                continue;

            if (slotId == AbilityBarWeaponQ)
            {
                slotHash = WeaponSlotQ;
                matchedSlotId = slotId;
                return true;
            }
            if (slotId == AbilityBarWeaponE || slotId == AbilityBarWeaponELegacy)
            {
                slotHash = WeaponSlotE;
                matchedSlotId = slotId;
                return true;
            }

            matchedSlotId = slotId;
        }

        return false;
    }

    /// <summary>
    /// Stamp canonical Spear Q/E with hardcoded per-weapon baseline. Known weapons stamp even
    /// when prefab CD discovery fails or is below the min-seconds noise gate.
    /// </summary>
    private static void HandleWeaponSlotCast(Entity caster, int slotHash,
        PrefabGUID abilityGroupGuid, PrefabGUID abilityCastGuid)
    {
        var groupName = VWorld.PrefabLookupMap.GetName(abilityGroupGuid) ?? "";
        var castName = VWorld.PrefabLookupMap.GetName(abilityCastGuid) ?? "";
        var groupHash = abilityGroupGuid.GuidHash;

        var hasFixed = WeaponAbilityCooldown.TryGetValue(groupHash, out var fixedCd);
        if (!hasFixed)
            hasFixed = WeaponAbilityCooldown.TryGetValue(abilityCastGuid.GuidHash, out fixedCd);
        if (!hasFixed)
            hasFixed = TryInferWeaponCooldownByName(groupName, castName, slotHash, out fixedCd);

        var hasPrefab = TryGetOrDiscoverDefinition(abilityGroupGuid, abilityCastGuid, out var def);

        if (!hasFixed)
        {
            if (!hasPrefab) return;
            if (def.Cooldown < Config.Extras.EnemyCooldownTrackerMinSeconds.Value) return;
        }

        var baseline = hasFixed ? fixedCd : WeaponCooldownDefault;
        DiscoveredDefinitions[slotHash] = new Definition
        {
            Cooldown = baseline,
            Label = slotHash == WeaponSlotQ ? "WeaponQ" : "WeaponE"
        };

        AbilityIconResolver.TryGetSprite(slotHash, out _);

        if (HasWeaponFreeCastReady(caster))
        {
            EnsureReadyStamp(caster, slotHash, DiscoveredDefinitions[slotHash]);
            return;
        }

        StampAbility(caster, slotHash, baseline, openRecastPending: false);
    }

    private static bool TryInferWeaponCooldownByName(string groupName, string castName, int slotHash,
        out float cooldown)
    {
        cooldown = 0f;
        var hay = groupName + " " + castName;
        if (string.IsNullOrWhiteSpace(hay)) return false;

        if (NameHas(hay, "Slashers") || NameHas(hay, "Whip") ||
            (NameHas(hay, "Claw") && slotHash == WeaponSlotE) ||
            (NameHas(hay, "Pistol") && slotHash == WeaponSlotE) ||
            NameHas(hay, "ExplosiveShot") ||
            (NameHas(hay, "GreatSword") && slotHash == WeaponSlotE) ||
            (NameHas(hay, "Dagger") && slotHash == WeaponSlotQ) ||
            NameHas(hay, "RainOfDaggers") || NameHas(hay, "NecroticPulse") ||
            NameHas(hay, "ElusiveStrike") || NameHas(hay, "Camouflage") ||
            NameHas(hay, "SkeweringLeap") || NameHas(hay, "Entangle"))
        {
            cooldown = 10f;
            return true;
        }

        if (LooksLikeWeaponSkillName(groupName) || LooksLikeWeaponSkillName(castName) ||
            NameHas(hay, "SweepingStrike") || NameHas(hay, "Javelin") || NameHas(hay, "TwinBlade"))
        {
            cooldown = 8f;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Fallback when a weapon skill's cast event doesn't replicate but its projectile does
    /// (observed Twinblade Javelin gap). Only stamps for projectile hashes in the weapon maps.
    /// </summary>
    internal static void OnWeaponProjectileObserved(Entity caster, PrefabGUID projectileGuid)
    {
        if (!Config.Extras.EnemyCooldownTracker.Enabled) return;
        if (caster == Entity.Null) return;

        var hash = projectileGuid.GuidHash;
        if (hash == 0) return;
        if (!WeaponCastToSlot.TryGetValue(hash, out var slotHash)) return;

        // Deduplicate against a cast STAMP in the same window — projectile often follows cast.
        if (Stamps.TryGetValue(caster, out var perAbility) &&
            perAbility.TryGetValue(slotHash, out var stampedAt) &&
            GetServerTime() - stampedAt < 0.35)
            return;

        HandleWeaponSlotCast(caster, slotHash, projectileGuid, projectileGuid);
    }

    /// <summary>
    /// Register an ability as known and ready without starting a cooldown (free-cast charge path).
    /// Stamp time is back-dated so remaining evaluates to ~0.
    /// </summary>
    private static void EnsureReadyStamp(Entity caster, int hash, Definition def)
    {
        DiscoveredDefinitions[hash] = def;
        var serverTime = GetServerTime();
        var effective = EffectiveCooldown(hash, def.Cooldown);
        if (!Stamps.TryGetValue(caster, out var perAbility))
        {
            perAbility = new Dictionary<int, double>();
            Stamps[caster] = perAbility;
        }
        // Only plant a ready stamp if we have no entry yet, or the previous CD already expired.
        if (!perAbility.TryGetValue(hash, out var stampedAt) ||
            (stampedAt + effective) - serverTime <= 0.0)
        {
            perAbility[hash] = serverTime - effective;
        }
        AbilityIconResolver.TryGetSprite(hash, out _);
        MaybePrune(serverTime);
    }

    private static void HandleSlotRecastUsed(Entity caster, int slotHash,
        PrefabGUID abilityGroupGuid, PrefabGUID abilityCastGuid)
    {
        // Prefer the already-known parent/slot CD. Recast prefabs often have 0 / tiny CD.
        float cooldown;
        if (DiscoveredDefinitions.TryGetValue(slotHash, out var existing))
        {
            cooldown = existing.Cooldown;
        }
        else if (TryGetOrDiscoverDefinition(abilityGroupGuid, abilityCastGuid, out var def))
        {
            cooldown = def.Cooldown;
            if (slotHash == WeaponSlotQ || slotHash == WeaponSlotE)
            {
                DiscoveredDefinitions[slotHash] = new Definition
                {
                    Cooldown = cooldown,
                    Label = slotHash == WeaponSlotQ ? "WeaponQ" : "WeaponE"
                };
            }
        }
        else
        {
            return;
        }

        StampAbility(caster, slotHash, cooldown, openRecastPending: false);
    }

    private static void StampAbility(Entity caster, int hash, float cooldown, bool openRecastPending)
    {
        var serverTime = GetServerTime();
        if (!Stamps.TryGetValue(caster, out var perAbility))
        {
            perAbility = new Dictionary<int, double>();
            Stamps[caster] = perAbility;
        }
        perAbility[hash] = serverTime;

        if (openRecastPending)
        {
            if (!RecastPending.TryGetValue(caster, out var pending))
            {
                pending = new Dictionary<int, double>();
                RecastPending[caster] = pending;
            }
            pending[hash] = serverTime;
        }
        else
        {
            ClearRecastPending(caster, hash);
        }

        MaybePrune(serverTime);
    }

    private static void ClearRecastPending(Entity caster, int parentHash)
    {
        if (!RecastPending.TryGetValue(caster, out var pending)) return;
        pending.Remove(parentHash);
        if (pending.Count == 0) RecastPending.Remove(caster);
    }

    /// <summary>True when this AbilityGroup hash is a travel Veil (drawn at feet).</summary>
    internal static bool IsVeilAbility(int abilityHash) =>
        PipMeta.TryGetValue(abilityHash, out var meta) && meta.Order == -1;

    /// <summary>True when this AbilityGroup hash is a weapon Q/E skill (drawn at torso).</summary>
    internal static bool IsWeaponAbility(int abilityHash) =>
        PipMeta.TryGetValue(abilityHash, out var meta) && meta.Order == 8;

    /// <summary>
    /// Split <see cref="GetCooldownPips"/> into head (counters/barriers), body (weapons),
    /// and feet (veils). Returned lists are static scratch buffers — consume before the next call.
    /// </summary>
    internal static void SplitZonePips(List<CooldownPip> all,
        out List<CooldownPip> head, out List<CooldownPip> body, out List<CooldownPip> feet)
    {
        HeadPipScratch.Clear();
        BodyPipScratch.Clear();
        FeetPipScratch.Clear();
        for (var i = 0; i < all.Count; i++)
        {
            var hash = all[i].AbilityHash;
            if (IsVeilAbility(hash))
                FeetPipScratch.Add(all[i]);
            else if (IsWeaponAbility(hash))
                BodyPipScratch.Add(all[i]);
            else
                HeadPipScratch.Add(all[i]);
        }
        head = HeadPipScratch;
        body = BodyPipScratch;
        feet = FeetPipScratch;
    }

    /// <summary>
    /// Build a stacked block of an enemy's known counter abilities, sorted ascending by
    /// remaining seconds. Off-cooldown abilities are rendered as a bare label (no number),
    /// so they sort to the top and act as a "ready right now" warning, e.g.
    /// <code>
    /// BloodRite
    /// ColdSnap 1.3
    /// MistTrance 4.2
    /// Discharge 14
    /// </code>
    /// We only render abilities we've actually observed this enemy cast at least once —
    /// the per-enemy stamp dictionary doubles as their known repertoire. Returns empty
    /// when we've never seen this enemy cast a tracked ability. Color tags rely on rich-text
    /// being enabled on the IMGUI styles in <see cref="ESP.Primitives"/>; TMP honors them natively.
    /// </summary>
    internal static string FormatCooldownLine(Entity enemy)
    {
        if (!Config.Extras.EnemyCooldownTracker.Enabled) return string.Empty;
        if (!Stamps.TryGetValue(enemy, out var perAbility) || perAbility.Count == 0) return string.Empty;

        RefreshConfigSets();
        var serverTime = GetServerTime();

        FormatScratch.Clear();
        List<int>? expired = null;

        foreach (var kvp in perAbility)
        {
            // Orphaned definitions can happen if the user edits label overrides at runtime
            // (RefreshConfigSets clears the def cache to force re-derivation). Drop them so
            // they get re-discovered on the next cast rather than rendering a stale label.
            if (!DiscoveredDefinitions.TryGetValue(kvp.Key, out var def))
            {
                expired ??= new List<int>();
                expired.Add(kvp.Key);
                continue;
            }

            // Belt-and-suspenders allow-list check at format time — defensive against stamps
            // that somehow predate an allow-list change (e.g. future code paths that bypass
            // OnAbilityCastObserved). Skip (don't expire) rather than removing entries.
            if (!AllowedHashes.Contains(kvp.Key)) continue;

            var remaining = GetRemainingSeconds(enemy, kvp.Key, kvp.Value, def, serverTime);

            // Late-applied label overrides — pick latest at format-time so config edits land live.
            var label = LabelOverrides.TryGetValue(kvp.Key, out var overridden) ? overridden : def.Label;
            FormatScratch.Add((remaining, label));
        }

        if (expired != null)
        {
            for (var i = 0; i < expired.Count; i++) perAbility.Remove(expired[i]);
        }

        if (FormatScratch.Count == 0) return string.Empty;

        // Imminent recast first. Stable enough for the small N (handful) we ever see per enemy.
        FormatScratch.Sort(static (a, b) => a.Remaining.CompareTo(b.Remaining));

        FormatBuilder.Clear();
        for (var i = 0; i < FormatScratch.Count; i++)
        {
            if (i > 0) FormatBuilder.Append('\n');
            AppendEntry(FormatBuilder, FormatScratch[i].Label, FormatScratch[i].Remaining);
        }
        FormatScratch.Clear();
        return FormatBuilder.ToString();
    }

    /// <summary>
    /// Append one entry. Color carries the cooldown state, not the ability identity:
    ///   - Ready (<paramref name="remaining"/> &le; 0) — no color tag, label inherits the base
    ///     ESP color (default red), and we render only the bare label. Absence of a timer is
    ///     itself the "available right now" signal.
    ///   - On cooldown — label is wrapped in yellow (<c>#ffcc55</c>) so the player's eye is
    ///     drawn to abilities still on cooldown vs. the urgent base-color ones that are up.
    /// Adaptive precision on the number: one decimal under 5s, integer above (the long tail
    /// doesn't need tenths and the extra characters just widen the label).
    /// </summary>
    private static void AppendEntry(StringBuilder sb, string label, double remaining)
    {
        if (remaining <= 0)
        {
            // Ready — inherit base ESP color, no number.
            sb.Append(label);
            return;
        }

        sb.Append("<color=#ffcc55>").Append(label).Append(' ');
        if (remaining < 5.0)
            sb.Append(remaining.ToString("F1", CultureInfo.InvariantCulture));
        else
            sb.Append(((int)remaining).ToString(CultureInfo.InvariantCulture));
        sb.Append("</color>");
    }

    /// <summary>
    /// Return structured cooldown data for pip-based rendering. Each entry carries the
    /// ability's assigned color and remaining cooldown. Entries are sorted in fixed slot
    /// order (counters, barriers, then veil) so the pip row is spatially stable.
    /// Returns an empty list when no tracked abilities have been observed for this enemy.
    /// The returned list is a static scratch buffer — consume it before the next call.
    /// </summary>
    internal static List<CooldownPip> GetCooldownPips(Entity enemy)
    {
        PipResultScratch.Clear();
        if (!Config.Extras.EnemyCooldownTracker.Enabled) return PipResultScratch;
        if (!Stamps.TryGetValue(enemy, out var perAbility) || perAbility.Count == 0) return PipResultScratch;

        RefreshConfigSets();
        var serverTime = GetServerTime();

        PipOrderScratch.Clear();
        foreach (var kvp in perAbility)
        {
            if (!DiscoveredDefinitions.TryGetValue(kvp.Key, out var def)) continue;
            if (!AllowedHashes.Contains(kvp.Key)) continue;
            if (!PipMeta.TryGetValue(kvp.Key, out var meta)) continue;

            var effectiveCD = EffectiveCooldown(kvp.Key, def.Cooldown);
            var remaining = GetRemainingSeconds(enemy, kvp.Key, kvp.Value, def, serverTime);
            PipOrderScratch.Add((meta.Order, new CooldownPip
            {
                AbilityHash = kvp.Key,
                Remaining = remaining,
                TotalCooldown = effectiveCD,
                Color = meta.Color
            }));
        }

        // Sort by fixed slot order, then hash so same-order weapons stay stable left-to-right.
        PipOrderScratch.Sort(static (a, b) =>
        {
            var cmp = a.Order.CompareTo(b.Order);
            return cmp != 0 ? cmp : a.Pip.AbilityHash.CompareTo(b.Pip.AbilityHash);
        });
        for (var i = 0; i < PipOrderScratch.Count; i++)
            PipResultScratch.Add(PipOrderScratch[i].Pip);

        return PipResultScratch;
    }

    internal static void Reset()
    {
        Stamps.Clear();
        RecastPending.Clear();
        DiscoveredDefinitions.Clear();
        _serverTimeQueryReady = false;
        _serverTimeQuery = default;
        _lastPruneAt = 0.0;
    }

    /// <summary>
    /// Remaining CD seconds. Special cases:
    ///   Weapon Q/E — free-cast charge ready, or skill-recast buff available → show 0.
    ///   Veil of Chaos:
    ///   1. First dash stamps CD and opens a "session" (RecastPending).
    ///   2. While the main Veil buff is up, CD keeps ticking (recast not earned yet).
    ///   3. Landing a hit grants Recast_Buff / Recast_Init_Buff → show ready (0).
    ///   4. Spending the recast (Recast_Group) re-stamps CD and closes the session.
    ///   5. If the Veil buff ends without a recast buff, session closes; CD continues from stamp.
    /// </summary>
    private static double GetRemainingSeconds(Entity enemy, int hash, double stampedAt, Definition def, double serverTime)
    {
        var effectiveCD = EffectiveCooldown(hash, def.Cooldown);
        var normalRemaining = (stampedAt + effectiveCD) - serverTime;

        // Weapon Q/E use the same remaining math as counters — no special recast overlay.

        if (hash != VeilOfChaosHash)
            return normalRemaining;

        var hasRecast = HasVeilChaosRecastAvailable(enemy);
        var hasVeil = HasVeilChaosActiveBuff(enemy);
        var isPending = RecastPending.TryGetValue(enemy, out var pending) && pending.ContainsKey(hash);

        // Hit landed while veiled → recast available. Show ready even if pending was lost.
        if (hasRecast)
        {
            if (!isPending)
            {
                if (!RecastPending.TryGetValue(enemy, out pending))
                {
                    pending = new Dictionary<int, double>();
                    RecastPending[enemy] = pending;
                }
                pending[hash] = stampedAt;
            }
            return 0.0;
        }

        if (isPending)
        {
            if (hasVeil)
            {
                // Still in veil window, waiting for a hit to unlock recast — keep counting CD.
                return normalRemaining;
            }

            // Veil buff gone and no recast earned/spent — close session.
            pending.Remove(hash);
            if (pending.Count == 0) RecastPending.Remove(enemy);
            return normalRemaining;
        }

        return normalRemaining;
    }

    private static bool HasVeilChaosRecastAvailable(Entity enemy)
    {
        if (!enemy.HasBuffer<BuffBuffer>()) return false;
        var buffs = enemy.ReadBuffer<BuffBuffer>();
        for (var i = 0; i < buffs.Length; i++)
        {
            var guid = buffs[i].PrefabGuid;
            var buffHash = guid.GuidHash;
            if (RecastBuffToParent.TryGetValue(buffHash, out var parent) && parent == VeilOfChaosHash)
                return true;

            // Name fallback — catches renamed hashes across patches.
            var name = VWorld.PrefabLookupMap.GetName(guid);
            if (string.IsNullOrEmpty(name)) continue;
            if (name.IndexOf("VeilOfChaos", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (name.IndexOf("Recast", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool HasVeilChaosActiveBuff(Entity enemy)
    {
        if (!enemy.HasBuffer<BuffBuffer>()) return false;
        var buffs = enemy.ReadBuffer<BuffBuffer>();
        for (var i = 0; i < buffs.Length; i++)
        {
            var guid = buffs[i].PrefabGuid;
            if (guid.GuidHash == VeilOfChaosBuffHash) return true;

            var name = VWorld.PrefabLookupMap.GetName(guid);
            if (string.IsNullOrEmpty(name)) continue;
            if (name.IndexOf("VeilOfChaos", StringComparison.OrdinalIgnoreCase) < 0) continue;
            // Exclude recast / confuse dummy / immaterial extras — main effect buff only.
            if (name.IndexOf("Recast", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (name.EndsWith("_Buff", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("VeilOfChaos_Buff", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool HasSpellFreeCastReady(Entity caster) =>
        HasChargeBuffReady(caster, Config.Extras.EnemyCooldownTrackerFreeCastBuffHash.Value);

    private static bool HasWeaponFreeCastReady(Entity caster) =>
        HasChargeBuffReady(caster, BuffFreeCastWeaponHash);

    private static bool HasChargeBuffReady(Entity caster, int buffHash) =>
        TryGetChargeBuffStacks(caster, buffHash, out var stacks) &&
        stacks >= Config.Extras.EnemyCooldownTrackerFreeCastThreshold.Value;

    private static bool TryGetChargeBuffStacks(Entity caster, int buffHash, out int stacks)
    {
        stacks = 0;
        if (buffHash == 0) return false;
        if (!caster.HasBuffer<BuffBuffer>()) return false;

        var buffs = caster.ReadBuffer<BuffBuffer>();
        for (var i = 0; i < buffs.Length; i++)
        {
            if (buffs[i].PrefabGuid.GuidHash != buffHash) continue;

            var buffEntity = buffs[i].Entity;
            if (buffEntity == Entity.Null || !buffEntity.Exists()) continue;
            if (!buffEntity.TryGetComponent<Buff>(out var buff)) continue;

            stacks = buff.Stacks;
            return true;
        }
        return false;
    }

    private static bool TryGetOrDiscoverDefinition(PrefabGUID groupGuid, PrefabGUID castGuid, out Definition def)
    {
        var hash = groupGuid.GuidHash;
        if (DiscoveredDefinitions.TryGetValue(hash, out def)) return true;

        // Cooldowns live on the cast prefab (e.g. AB_Blood_BloodRite_Cast), not on the group
        // container (AB_Blood_BloodRite_AbilityGroup). Try the cast we just observed first;
        // if that fails, walk the group's child-ability buffer as a fallback.
        if (!TryReadPrefabCooldown(castGuid, out var cooldown) &&
            !TryReadCooldownFromGroupChildren(groupGuid, out cooldown))
        {
            def = default;
            return false;
        }

        var label = LabelOverrides.TryGetValue(hash, out var overridden)
            ? overridden
            : DeriveLabel(groupGuid);

        def = new Definition { Cooldown = cooldown, Label = label };
        DiscoveredDefinitions[hash] = def;
        return true;
    }

    private static bool TryReadCooldownFromGroupChildren(PrefabGUID groupGuid, out float cooldown)
    {
        cooldown = 0f;
        var prefabMap = VWorld.PrefabLookupMap;
        if (!prefabMap.TryGetValue(groupGuid, out var groupPrefab) || groupPrefab == Entity.Null || !groupPrefab.Exists())
            return false;
        if (!groupPrefab.HasBuffer<AbilityGroupStartAbilitiesBuffer>())
            return false;

        var children = groupPrefab.ReadBuffer<AbilityGroupStartAbilitiesBuffer>();
        for (var i = 0; i < children.Length; i++)
        {
            var childGuid = children[i].PrefabGUID;
            if (childGuid.GuidHash == 0) continue;
            if (TryReadPrefabCooldown(childGuid, out cooldown)) return true;
        }

        return false;
    }

    private static bool TryReadPrefabCooldown(PrefabGUID guid, out float cooldown)
    {
        cooldown = 0f;
        var prefabMap = VWorld.PrefabLookupMap;
        if (!prefabMap.TryGetValue(guid, out var prefabEntity)) return false;
        if (prefabEntity == Entity.Null || !prefabEntity.Exists()) return false;
        if (!prefabEntity.TryGetComponent<AbilityCooldownData>(out var cdData)) return false;

        cooldown = cdData.Cooldown.Value;
        return cooldown > 0f;
    }

    private static void MaybePrune(double serverTime)
    {
        if (serverTime - _lastPruneAt < PruneIntervalSeconds) return;
        _lastPruneAt = serverTime;

        // We deliberately do NOT prune per-ability stamps when their cooldown lapses — the
        // off-cooldown state is itself useful ("ability ready right now") and the formatter
        // surfaces it as a bare label. Stamps live until the entity itself goes away.
        PruneScratch.Clear();
        foreach (var kvp in Stamps)
        {
            if (!kvp.Key.Exists()) PruneScratch.Add(kvp.Key);
        }
        for (var i = 0; i < PruneScratch.Count; i++)
        {
            Stamps.Remove(PruneScratch[i]);
            RecastPending.Remove(PruneScratch[i]);
        }
        PruneScratch.Clear();

        foreach (var kvp in RecastPending)
        {
            if (!kvp.Key.Exists()) PruneScratch.Add(kvp.Key);
        }
        for (var i = 0; i < PruneScratch.Count; i++) RecastPending.Remove(PruneScratch[i]);
        PruneScratch.Clear();
    }

    private static void RefreshConfigSets()
    {
        var labelsRaw = Config.Extras.EnemyCooldownTrackerLabelOverrides.Value;
        if (labelsRaw != _lastConfiguredLabelOverrides)
        {
            _lastConfiguredLabelOverrides = labelsRaw;
            LabelOverrides.Clear();
            ParseLabelOverrides(labelsRaw);
            // Force re-resolution next time so already-cached defs pick up renamed labels.
            DiscoveredDefinitions.Clear();
        }

        var ignoredRaw = Config.Extras.EnemyCooldownTrackerIgnoredHashes.Value;
        if (ignoredRaw != _lastConfiguredIgnoredHashes)
        {
            _lastConfiguredIgnoredHashes = ignoredRaw;
            IgnoredHashes.Clear();
            ParseIgnoredHashes(ignoredRaw);
        }
    }

    private static void ParseLabelOverrides(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var entries = raw.Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < entries.Length; i++)
        {
            var parts = entries[i].Split(':');
            if (parts.Length < 2) continue;
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash)) continue;
            var label = parts[1].Trim();
            if (label.Length == 0) continue;
            LabelOverrides[hash] = label;
        }
    }

    private static void ParseIgnoredHashes(string raw) => ParseHashSet(raw, IgnoredHashes);

    private static void ParseHashSet(string raw, HashSet<int> target)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var tokens = raw.Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            if (int.TryParse(tokens[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash))
                target.Add(hash);
        }
    }

    /// <summary>
    /// Apply per-skill CDR (veils / weapons) or All Counters for counter/barrier hashes.
    /// Floored at a small positive number so a misconfigured slider can't make every
    /// cooldown evaluate as instantly expired.
    /// </summary>
    private static float EffectiveCooldown(int abilityHash, float baseCooldown)
    {
        var skillCdr = 0f;
        if (SkillCdrByHash.TryGetValue(abilityHash, out var entry))
            skillCdr = entry.Value;
        else if (CounterCdrHashes.Contains(abilityHash))
            skillCdr = Config.Extras.CdrAllCounters.Value;
        var reduced = baseCooldown - skillCdr;
        return reduced < 0.1f ? 0.1f : reduced;
    }

    private static string DeriveLabel(PrefabGUID groupGuid)
    {
        var name = VWorld.PrefabLookupMap.GetName(groupGuid);
        if (string.IsNullOrEmpty(name)) return groupGuid.GuidHash.ToString(CultureInfo.InvariantCulture);

        // AB_Blood_BloodRite_AbilityGroup -> BloodRite
        // AB_Vampire_VeilOfShadow_Group   -> VeilOfShadow
        var trimmed = name;
        if (trimmed.StartsWith("AB_", StringComparison.Ordinal)) trimmed = trimmed[3..];
        if (trimmed.EndsWith("_AbilityGroup", StringComparison.Ordinal)) trimmed = trimmed[..^"_AbilityGroup".Length];
        else if (trimmed.EndsWith("_Group", StringComparison.Ordinal)) trimmed = trimmed[..^"_Group".Length];

        var lastUnderscore = trimmed.LastIndexOf('_');
        return lastUnderscore >= 0 ? trimmed[(lastUnderscore + 1)..] : trimmed;
    }

    private static double GetServerTime()
    {
        if (!_serverTimeQueryReady)
        {
            try
            {
                _serverTimeQuery = VWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ServerTime>());
                _serverTimeQueryReady = true;
            }
            catch
            {
                return 0.0;
            }
        }

        if (_serverTimeQuery.IsEmpty) return 0.0;
        try
        {
            return _serverTimeQuery.GetSingleton<ServerTime>().Time;
        }
        catch
        {
            return 0.0;
        }
    }
}
