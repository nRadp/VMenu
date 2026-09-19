using System;
using System.Collections.Generic;
using System.Globalization;
using ExtrasensoryPerception.API;
using ExtrasensoryPerception.ESP;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ExtrasensoryPerception.Utils;

/// <summary>
/// Auto-counter: when an enemy player begins casting a configured "lock" ability (spear's
/// AThousandSpears, slashers' Camouflage Secondary) we synthetically press the local
/// player's counter ability key via <see cref="InputSimulator"/> so the parry window opens
/// before the enemy's hit lands.
///
/// The counter key is resolved dynamically: we scan the local player's
/// <see cref="AbilityGroupSlotBuffer"/> for known counter abilities (BloodRite, MistTrance,
/// ChaosBarrier, FrostBarrier, WardOfTheDamned, Discharge) in spell slots 5 and 6.
/// If no counter ability is equipped, the feature is inert — nothing is pressed.
/// If two counter abilities are equipped, we pick the first available one.
/// If a counter/barrier buff is already active on the local player (manual or prior auto),
/// we skip so a second defensive press does not waste the first.
///
/// Driven by <see cref="Patches.AbilityCastStartedPatch"/>, which fires at cast start. We
/// either fire immediately (if the caster is already aimed at us) or queue a per-frame
/// recheck so a mid-cast rotation toward us still triggers the counter. The synthetic press
/// is held for a short window and released by <see cref="Tick"/> on the main thread —
/// pressing and releasing inside the same frame can be too fast for Unity's input poll to
/// register as a "key down".
///
/// Per-ability hit shapes (range, half-angle, dash displacement) live in
/// <see cref="DefaultHitArcs"/>, sourced from the prefab dump. Trigger hashes without a
/// matching arc entry are skipped — we no longer guess geometry from generic sliders.
///
/// Knobs (all in <c>Config.Extras</c>):
///   - <c>AutoCounter</c>: master toggle.
///   - <c>AutoCounterUseCounters</c> / <c>AutoCounterUseBarriers</c>: which equipped
///     ability categories to press (parry counters vs damage-soaking barriers).
///   - <c>AutoCounterSpellSlot1Key</c> / <c>AutoCounterSpellSlot2Key</c>: keys for the two
///     spell slots (default R and C). Must match the player's in-game keybinds.
///   - <c>AutoCounterTriggerHashes</c>: comma-separated AbilityGroup or AbilityCast guid
///     hashes that should provoke a counter. Each hash also needs a <see cref="DefaultHitArcs"/>
///     entry; otherwise the cast is observed for logging but never fires the counter.
///   - <c>AutoCounterActivationLatencySeconds</c>: assumed press-to-active latency for the
///     counter ability; sets the per-cast fire deadline as <c>windup - latency</c>.
///   - <c>AutoCounterTargetColliderRadius</c>: padding added to gate dimensions to model
///     the local player's collider (HitColliderCast tests collider-vs-collider).
///   - <c>AutoCounterDebugLog</c> / <c>AutoCounterDebugLogRadius</c>: per-cast diagnostic
///     logging plus a one-shot prefab dump (<see cref="PrefabInspector"/>) for picking
///     per-ability hit-arc numbers.
///
/// While a counter/barrier buff is active on the local player (or briefly after a local
/// protective cast starts), Auto Counter will not press a second defensive ability.
///
/// Cast windup is read directly from each cast prefab's <see cref="AbilityCastTimeData.MaxCastTime"/>
/// (cached per cast hash); no user-tunable windup slider — the prefab is authoritative.
/// </summary>
internal static class AutoCounter
{
    // ===== Constants ========================================================

    private const double HoldDurationSeconds = 0.05;

    // ===== Nested types =====================================================

    /// <summary>
    /// Discriminator for <see cref="HitArc"/>. Picks which hit-volume primitive the gate
    /// tests against:
    ///   - <see cref="Cone"/>: melee swings whose <c>HitColliderCast</c> is a Cone
    ///     (slasher Camouflage Secondary).
    ///   - <see cref="ForwardCapsule"/>: projectile sweeps and dash-line abilities where
    ///     the hit volume is a circle/capsule of radius <c>Range</c> swept along forward
    ///     for <c>ForwardLength</c> meters — a fixed cone either underfits far or overfits
    ///     near for these.
    ///   - <see cref="ForwardBox"/>: rectangular thrust/sweep hits whose <c>HitColliderCast</c>
    ///     is a Box (spear AThousandSpears Stab — 1.50m wide, 3.75m long box anchored 2.25m
    ///     ahead of the caster). A cone or capsule fits this poorly: cones widen with
    ///     distance whereas the box stays a constant ~1.5m wide, and capsules round the
    ///     corners we don't want rounded.
    ///   - <see cref="Circle"/>: owner-centered AoE (reaper TendonSwing Twist) — horizontal
    ///     distance from caster only; facing is irrelevant (spin / face-flip skills).
    /// </summary>
    internal enum HitShape { Cone, ForwardCapsule, ForwardBox, Circle }

    /// <summary>
    /// Per-ability hit shape. Three flavours dispatched on <see cref="Shape"/>:
    ///   - <see cref="HitShape.Cone"/>: anchor at <c>caster + effectiveForward * forward</c>,
    ///     <see cref="Range"/> is max distance from vertex, <see cref="HalfAngleDeg"/> the
    ///     cone half-angle.
    ///   - <see cref="HitShape.ForwardCapsule"/>: segment from anchor to
    ///     <c>anchor + ForwardLength * forward</c>, <see cref="Range"/> is the capsule radius.
    ///   - <see cref="HitShape.ForwardBox"/>: oriented box centered at anchor, extending
    ///     <c>ForwardLength/2</c> along forward and <c>Width/2</c> perpendicular (Y ignored
    ///     — we already gate horizontal-plane only).
    ///   - <see cref="HitShape.Circle"/>: <see cref="Range"/> is horizontal radius from caster;
    ///     facing and dash are unused.
    ///
    /// The anchor at any time t is <c>caster + (ForwardOffset + DashRemaining) * forward</c>
    /// where <see cref="DashRemaining"/> linearly decays from <see cref="DashDistance"/> at
    /// cast start to 0 at cast end. The split matters because the two terms have different
    /// physical meaning:
    ///   - <see cref="ForwardOffset"/> is a constant prefab geometry offset (e.g. the spear
    ///     stab box's <c>offset.z=2.25m</c> anchors the box 2.25m ahead of the spawn entity
    ///     forever, regardless of how far the caster has dashed).
    ///   - <see cref="DashDistance"/> is the in-cast displacement (<c>MoveDuringCastData</c>'s
    ///     <c>ForceMovementLength</c>) which is in the future at cast start and behind us by
    ///     cast end. We add it to the anchor as a lookahead and decay it as the caster
    ///     physically covers it. Conflating these two would either over-decay the geometry
    ///     anchor (wrong shape position late in the cast) or under-decay the dash (gate
    ///     keeps overshooting after the dash completes).
    ///
    /// We deliberately gate on the caster's body forward (<see cref="LocalToWorld"/>) and
    /// not on aim direction. Empirically the cursor-aim signal (EntityAimData) is too
    /// volatile on remote players (cursor moves around mid-cast, can read stale or off-
    /// target), while the body forward is snapped to its final orientation early in the
    /// windup by <c>ModifyRotationDuringCast</c> and matches where the dash actually goes.
    /// </summary>
    internal readonly struct HitArc
    {
        public readonly HitShape Shape;
        public readonly float Range;          // Cone: max distance from vertex. Capsule: radius. Box: unused.
        public readonly float HalfAngleDeg;   // Cone only.
        public readonly float ForwardOffset;  // Constant geometry anchor offset along caster forward.
        public readonly float ForwardLength;  // Capsule/Box: extent along forward (Box: full length, capsule: segment length).
        public readonly float Width;          // Box only: full perpendicular width.
        public readonly float DashDistance;   // Full MoveDuringCastData.ForceMovementLength. Scaled by ScaleDashForCast() at gate-time.
        public readonly float DashRemaining;  // Current decayed value; equal to DashDistance at cast start (pre-scaling).
        public readonly float DashManualDuration; // MoveDuringCastData.ManualDuration when UseManualDuration=true; 0 = dash completes within windup.
        /// <summary>
        /// When true, pending rechecks keep the cast-start body forward instead of the live
        /// interpolated body.
        /// </summary>
        public readonly bool LockForwardAtCastStart;
        /// <summary>
        /// When true, never immediate-fire; evaluate live aim only in the last slice of
        /// windup (projectile leave aim). Avoids early presses and cast-start lock misses
        /// when aim tracks during cast.
        /// </summary>
        public readonly bool DecideNearWindupEnd;
        /// <summary>
        /// Capsule gates already admit any angle whose lateral miss is within
        /// <c>radius + TargetRadius</c>. At short range that becomes a huge half-angle
        /// (~17° at 3.5m with r≈1.0) and body-sweep false-fires. When &gt; 0, also require
        /// body half-angle ≤ this value (projectile leave-aim gates).
        /// </summary>
        public readonly float MaxHalfAngleDeg;
        /// <summary>
        /// When true, cast-start only arms a pending entry. Counter fires on
        /// <see cref="OnEnemyAbilityCastCommitted"/> (BlockBuff / PreCastFinished).
        /// Interrupted casts never spawn the commit signal and drop without pressing.
        /// Used by Sword Shockwave (E).
        /// </summary>
        public readonly bool WaitForCastCommit;
        /// <summary>
        /// Per-arc override for the local-player collider pad. When &lt; 0, uses
        /// <c>AutoCounterTargetColliderRadius</c>. Thin projectiles (Sword E) need a
        /// smaller pad than melee boxes or the corridor becomes a near-miss sponge.
        /// </summary>
        public readonly float TargetRadiusPad;
        /// <summary>
        /// When true and aim is readable, gate on whichever forward is more on-target
        /// (smaller angle). Twinblade / ExplosiveShot AimDirection dashes: body lags on
        /// flicks while aim is already correct — OR-pass caused false fires on stale aim.
        /// </summary>
        public readonly bool PreferAimForward;

        private HitArc(HitShape shape, float range, float halfAngleDeg, float forwardOffset,
            float forwardLength, float width, float dashDistance, float dashRemaining, float dashManualDuration,
            bool lockForwardAtCastStart = false, bool decideNearWindupEnd = false, float maxHalfAngleDeg = 0f,
            bool waitForCastCommit = false, float targetRadiusPad = -1f, bool preferAimForward = false)
        {
            Shape = shape;
            Range = range;
            HalfAngleDeg = halfAngleDeg;
            ForwardOffset = forwardOffset;
            ForwardLength = forwardLength;
            Width = width;
            DashDistance = dashDistance;
            DashRemaining = dashRemaining;
            DashManualDuration = dashManualDuration;
            LockForwardAtCastStart = lockForwardAtCastStart;
            DecideNearWindupEnd = decideNearWindupEnd;
            MaxHalfAngleDeg = maxHalfAngleDeg;
            WaitForCastCommit = waitForCastCommit;
            TargetRadiusPad = targetRadiusPad;
            PreferAimForward = preferAimForward;
        }

        public static HitArc MakeCone(float range, float halfAngleDeg, float forwardOffset = 0f,
            float dashDistance = 0f, float dashManualDuration = 0f) =>
            new(HitShape.Cone, range, halfAngleDeg, forwardOffset, 0f, 0f, dashDistance, dashDistance, dashManualDuration);

        public static HitArc MakeForwardCapsule(float radius, float forwardLength, float forwardOffset = 0f,
            float dashDistance = 0f, float dashManualDuration = 0f, bool lockForwardAtCastStart = false,
            bool decideNearWindupEnd = false, float maxHalfAngleDeg = 0f, bool waitForCastCommit = false,
            float targetRadiusPad = -1f, bool preferAimForward = false) =>
            new(HitShape.ForwardCapsule, radius, 0f, forwardOffset, forwardLength, 0f, dashDistance, dashDistance,
                dashManualDuration, lockForwardAtCastStart, decideNearWindupEnd, maxHalfAngleDeg, waitForCastCommit,
                targetRadiusPad, preferAimForward);

        public static HitArc MakeForwardBox(float width, float forwardLength, float forwardOffset = 0f,
            float dashDistance = 0f, float dashManualDuration = 0f, bool decideNearWindupEnd = false,
            float maxHalfAngleDeg = 0f, bool preferAimForward = false) =>
            new(HitShape.ForwardBox, 0f, 0f, forwardOffset, forwardLength, width, dashDistance, dashDistance,
                dashManualDuration, decideNearWindupEnd: decideNearWindupEnd, maxHalfAngleDeg: maxHalfAngleDeg,
                preferAimForward: preferAimForward);

        public static HitArc MakeCircle(float radius) =>
            new(HitShape.Circle, radius, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

        /// <summary>Effective anchor forward distance from caster at the current decay state.</summary>
        public float EffectiveForward => ForwardOffset + DashRemaining;

        public float ResolveTargetRadius() =>
            TargetRadiusPad >= 0f
                ? TargetRadiusPad
                : Mathf.Max(0f, Config.Extras.AutoCounterTargetColliderRadius.Value);

        public HitArc WithDashScaled(float fraction) =>
            new(Shape, Range, HalfAngleDeg, ForwardOffset, ForwardLength, Width, DashDistance, DashDistance * fraction,
                DashManualDuration, LockForwardAtCastStart, DecideNearWindupEnd, MaxHalfAngleDeg, WaitForCastCommit,
                TargetRadiusPad, PreferAimForward);
    }

    /// <summary>
    /// One in-flight tracked cast. Pistol Cast01/02/03 can overlap in the pending list
    /// (same caster, different cast hashes) — a single Dictionary slot used to drop Cast01
    /// the moment Cast02 started, which skipped the long Cast01 decision window.
    /// </summary>
    private struct PendingCast
    {
        public Entity Caster;
        public PrefabGUID GroupGuid;
        public PrefabGUID CastGuid;
        public double StartedAt;
        public double ExpiresAt;
        // Snapshot of caster body forward at cast start when HitArc.LockForwardAtCastStart.
        public Vector3 LockedBodyForward;
        public bool HasLockedForward;
        public bool DecideNearWindupEnd;
        // When true, TickPending never late-fires — commit/interrupt events decide.
        public bool AwaitCastCommit;
        // Consecutive late-window frames where the live gate passed.
        public int LateGatePassFrames;
        // Gate angle on the last confirm frame (body, or the rotation goal for lead-gated
        // arcs) — if it jumps up mid-confirm the signal is sweeping past us, so reset and
        // don't fire on a transient graze.
        public float LastConfirmAngle;
        // Last logged trace body angle. Used by TickPending to throttle the per-frame trace
        // log to material rotation changes only — otherwise a 60fps recheck floods the log
        // with near-duplicates of an unchanging value.
        public float LastTracedBodyAngle;
        public float LastTracedLeadAngle;
        public bool HasTraced;
        // Body-angle sample window used to measure how fast the caster is turning onto us.
        // Remote body rotation arrives on network snapshots, so frame-to-frame deltas read
        // 0 on most ticks — the sample is only rolled once the window is wide enough and
        // the last measured rate is held in between.
        public float PrevBodyAngle;
        public double PrevSampleAt;
        public bool HasPrevSample;
        public float LastTurnRate;
        // Same window over the rotation goal: a cursor being dragged onto us moves at a
        // steady rate, so extrapolating it catches the flick while there is still time to
        // press. The body only reveals that flick after it has started chasing.
        public float PrevLeadAngle;
        public double PrevLeadSampleAt;
        public bool HasPrevLeadSample;
        public float LastLeadTurnRate;
    }

    /// <summary>
    /// Horizontal-plane geometry between the caster and the local player.
    /// <see cref="BodyForward"/> comes from <see cref="LocalToWorld"/> (melee boxes).
    /// <see cref="AimForward"/> comes from <see cref="EntityAimData.AimPosition"/> — a world
    /// point, so the derived direction drifts (and can flip) while the caster dashes.
    /// <see cref="LeadForward"/> comes from <c>TargetDirection</c>/<c>EntityInput</c>, which
    /// store an actual direction vector: this is the rotation goal the body snaps onto during
    /// <c>ModifyRotationDuringCast</c>, so it says where the hit box will point before the
    /// body gets there.
    /// </summary>
    private readonly struct HitGeometry
    {
        public readonly bool Valid;
        public readonly Vector3 ToLocal;
        public readonly Vector3 BodyForward;
        public readonly float Distance;
        public readonly float BodyAngle;
        public readonly bool HasAim;
        public readonly Vector3 AimForward;
        public readonly float AimAngle;
        public readonly bool HasLead;
        public readonly Vector3 LeadForward;
        public readonly float LeadAngle;
        public readonly string LeadSource;

        public HitGeometry(bool valid, Vector3 toLocal, Vector3 bodyForward, float distance, float bodyAngle,
            bool hasAim = false, Vector3 aimForward = default, float aimAngle = 0f,
            bool hasLead = false, Vector3 leadForward = default, float leadAngle = 0f, string leadSource = "")
        {
            Valid = valid;
            ToLocal = toLocal;
            BodyForward = bodyForward;
            Distance = distance;
            BodyAngle = bodyAngle;
            HasAim = hasAim;
            AimForward = aimForward;
            AimAngle = aimAngle;
            HasLead = hasLead;
            LeadForward = leadForward;
            LeadAngle = leadAngle;
            LeadSource = leadSource;
        }

        public static HitGeometry Invalid() => new(false, Vector3.zero, Vector3.zero, 0f, 0f);

        /// <summary>
        /// Same positions, but gate/trace against a frozen body forward (projectile aim lock).
        /// </summary>
        public HitGeometry WithBodyForward(Vector3 bodyForward)
        {
            var fwd = bodyForward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-6f) fwd.Normalize();
            else fwd = Vector3.zero;

            var bodyAngle = 0f;
            if (Valid && Distance > 0.05f && fwd.sqrMagnitude > 1e-6f)
            {
                var cos = Mathf.Clamp(Vector3.Dot(fwd, ToLocal / Distance), -1f, 1f);
                bodyAngle = Mathf.Acos(cos) * Mathf.Rad2Deg;
            }

            return new HitGeometry(Valid, ToLocal, fwd, Distance, bodyAngle, HasAim, AimForward, AimAngle,
                HasLead, LeadForward, LeadAngle, LeadSource);
        }

        /// <summary>
        /// Forward used by hit-arc gates. Projectile leave-aim prefers cursor aim when present.
        /// </summary>
        public Vector3 GateForward => HasAim ? AimForward : BodyForward;
        public float GateAngle => HasAim ? AimAngle : BodyAngle;
    }

    // ===== Known trigger hashes (UI-toggleable) ===============================

    private const int SlasherCamouflageSecondaryHash = -13231823;
    private const int SlasherElusiveStrikeGroupHash = -1545438316; // Slasher Q
    private const int SlasherElusiveStrikeCastHash = -1731625958;
    private const int WhipDashGroupHash = 1420346034; // Whip Q
    private const int WhipDashCastHash = 1057163055;
    private const int SwordShockwaveGroupHash = 1335008684; // Sword E
    private const int SwordShockwaveCastHash = 804759750;
    private const int SpearAThousandSpearsStabHash = 377778793;
    private const int TwinbladeSweepingStrikeGroupHash = -1238817965; // Twinblade E
    private const int TwinbladeSweepingStrikeCastHash = -1474711270;
    private const int ReaperTendonSwingTwistGroupHash = -345652149; // Reaper Q
    private const int ReaperTendonSwingTwistCastHash = 344610321;
    private const int PistolPrimaryGroupHash = -1490887838;
    private const int PistolPrimaryCast01Hash = -1246832826;
    private const int PistolPrimaryCast02Hash = 1552752232;
    private const int PistolPrimaryCast03Hash = 1394076868;
    // ExplosiveShot (E): gate Shot / Recast only — DashCast has no projectile.
    private const int PistolExplosiveShotShotGroupHash = -1145923288;
    private const int PistolExplosiveShotShotCastHash = 286387494;
    private const int PistolExplosiveShotRecastGroupHash = 1913579080;
    private const int PistolExplosiveShotRecastCastHash = -1783827601;
    private const int CrossbowSnapshotGroupHash = 477749225;
    private const int CrossbowSnapshotCastHash = -1250944160;
    private const int CrossbowPrimaryGroupHash = 1070827786;
    private const int CrossbowPrimaryCastHash = 1081050588;

    // ===== Per-ability hit-arc table ========================================

    /// <summary>
    /// Hardcoded per-ability hit arcs sourced from the prefab dump (<see cref="PrefabInspector"/>).
    /// When a tracked cast's group or cast hash matches the table, the per-ability arc
    /// replaces the global Max Range / Hit Cone sliders for the gate. Anything not in the
    /// table still falls back to the sliders, so adding a new trigger hash without an entry
    /// here just means "use the global tuning until we measure it".
    /// </summary>
    private static readonly Dictionary<int, HitArc> DefaultHitArcs = new()
    {
        // Slashers Camouflage Secondary (E) — fallback if live prefab read fails.
        // Prefer PrefabInspector.TryReadConeHitArc (patched radius 3.3m after 1.1.13.0).
        { SlasherCamouflageSecondaryHash, HitArc.MakeCone(range: 3.30f, halfAngleDeg: 60f, dashDistance: 2.00f) },

        // Slashers ElusiveStrike Dash (Q) — PrefabDump:
        //   maxCast=0.20s, ModifyRotation → AimDirection snap.
        //   PhaseOut → HitColliderCast Box w=1.25 l=1.00 offset z=0.50 OnUpdate (follows dash),
        //              plus BloodFountain Circle r=1.60 on hit.
        //   Must be a CAPSULE corridor (not a centered ForwardBox): ForwardBox with
        //   length=8/offset=0.5 only covered along≈[0,5.1] and rejected real 6.3m head-ons.
        { SlasherElusiveStrikeGroupHash, HitArc.MakeForwardCapsule(radius: 0.70f, forwardLength: 8.00f, forwardOffset: 0f) },
        { SlasherElusiveStrikeCastHash, HitArc.MakeForwardCapsule(radius: 0.70f, forwardLength: 8.00f, forwardOffset: 0f) },

        // Whip Dash (Q) — PrefabDump:
        //   maxCast=0.55s, MoveDuringCast forceMovementLength=5.00m manualDuration=0.65s.
        //   Dash_Hit → Circle radius=3.50 offset=(0,1,0) OnSpawn at Owner (after dash).
        //   Point-capsule at dash anchor; near/far pads use full radius when length≈0.
        { WhipDashGroupHash, HitArc.MakeForwardCapsule(radius: 3.50f, forwardLength: 0.05f, forwardOffset: 0f, dashDistance: 5.00f, dashManualDuration: 0.65f) },
        { WhipDashCastHash, HitArc.MakeForwardCapsule(radius: 3.50f, forwardLength: 0.05f, forwardOffset: 0f, dashDistance: 5.00f, dashManualDuration: 0.65f) },

        // Sword Shockwave (E) — PrefabDump:
        //   maxCast=0.70s, ModifyRotation → ProjectileAimDirection snap.
        //   Projectile range=14m speed=28m/s, HitColliderCast Circle r=0.45,
        // continuousOwnerRotation. WaitForCastCommit via BlockBuff; tight target pad.
        { SwordShockwaveGroupHash, SwordShockwaveArc },
        { SwordShockwaveCastHash, SwordShockwaveArc },

        // Spear AThousandSpears Stab (Q):
        //   Cast prefab          → MoveDuringCastData forceMovementLength=1.25m UseAimDirection,
        //                          manualDuration=0.70s, UseManualDuration=true.
        //   Cast spawn _Channel  → (state buff) → SpawnPrefabOnGameplayEvent → _Hit prefab
        //   _Hit prefab          → HitColliderCast Box w=1.50 h=1.00 l=3.75 offset=(0,1.00,2.25)
        //                          trigger=OnSpawn, GetOwnerRotationOnlyOnSpawnTag.
        //   Windup is 0.45s but dash takes 0.70s — dash is only ~64% complete by hit-time.
        //   ScaleDashForCast scales the lookahead accordingly so the box is anchored where it
        //   actually lands (caster + 2.25m + ~0.80m of dash) rather than at the post-full-dash
        //   position the caster never reaches before the hit fires.
        //   The ball lightning sibling spawn is irrelevant here — doesn't proc the parry.
        { SpearAThousandSpearsStabHash, HitArc.MakeForwardBox(width: 1.50f, forwardLength: 3.75f, forwardOffset: 2.25f, dashDistance: 1.25f, dashManualDuration: 0.70f) },

        // Twinblade SweepingStrike (E) — PrefabDump:
        //   MoveDuringCast forceMovementLength=2.50m UseAimDirection, manualDuration=0.40s.
        //   Hit Box w=1.90 l=4.80 offset z=2.80. PreferAimForward: flick-casts leave body at
        //   100–170° while aim is already on-target (logs: aim 1–11°, body still >100°).
        { TwinbladeSweepingStrikeGroupHash, TwinbladeSweepingStrikeArc },
        { TwinbladeSweepingStrikeCastHash, TwinbladeSweepingStrikeArc },

        // Reaper TendonSwing Twist (Q) — PrefabDump:
        //   Cast → AbilityCastTimeData maxCast=0.20s (no MoveDuringCast dash).
        //   Spawn _Hit → HitColliderCast Circle radius=3.00 offset=(0,1.00,0) OnSpawn,
        //                GetTranslationOnSpawn source=Owner, continuousOwnerRotation.
        //   Facing-agnostic: bodyAngle 176° at 1.8m still hits. Gate = horizontal dist only.
        { ReaperTendonSwingTwistGroupHash, HitArc.MakeCircle(radius: 3.00f) },
        { ReaperTendonSwingTwistCastHash, HitArc.MakeCircle(radius: 3.00f) },

        // Pistol Primary Attack — PrefabDump (Cast01/02/03 + veil variants share geometry):
        //   Spawn Projectile → range=8.00m speed=28m/s, HitColliderCast Circle r=0.50.
        //   ModifyRotationDuringCast tracks ProjectileAimDirection through windup, so the
        //   shot leaves on LIVE aim — not cast-start body. DecideNearWindupEnd + live-only
        //   gate in the last ~0.18s (with multi-frame confirm) catches late tracking without
        //   early mid-windup snaps. MaxHalfAngleDeg caps close-range capsule acceptance
        //   (body-sweep false fires at 3–5m where asin((r+pad)/d) balloons to ~15–20°).
        { PistolPrimaryGroupHash, PistolPrimaryArc },
        { PistolPrimaryCast01Hash, PistolPrimaryArc },
        { PistolPrimaryCast02Hash, PistolPrimaryArc },
        { PistolPrimaryCast03Hash, PistolPrimaryArc },

        // Pistol ExplosiveShot (E) — PrefabDump Shot_Cast / Shot_Recast_Cast:
        //   maxCast=0.30s, ModifyRotation → AimDirection (post → ProjectileAimDirection).
        //   Projectile range=8m speed=24m/s, Circle r=0.60, OffsetTranslation z=0.70.
        //   DashCast (66606146) is mobility only — do not trigger on it.
        { PistolExplosiveShotShotGroupHash, PistolExplosiveShotArc },
        { PistolExplosiveShotShotCastHash, PistolExplosiveShotArc },
        { PistolExplosiveShotRecastGroupHash, PistolExplosiveShotArc },
        { PistolExplosiveShotRecastCastHash, PistolExplosiveShotArc },

        { CrossbowSnapshotGroupHash, CrossbowSnapshotArc },
        { CrossbowSnapshotCastHash, CrossbowSnapshotArc },

        { CrossbowPrimaryGroupHash, CrossbowPrimaryArc },
        { CrossbowPrimaryCastHash, CrossbowPrimaryArc },
    };

    // Prefab box + 2.50m AimDirection dash. Both the box facing and the dash direction at
    // impact are the caster's rotation goal, which TargetDirection gives us outright — so
    // the gate runs on HitGeometry.LeadForward alone here (see ResolvePendingGateVia).
    // Body position mid-cast is a sweep in progress and says nothing about where it lands.
    private static HitArc TwinbladeSweepingStrikeArc => HitArc.MakeForwardBox(
        width: 1.90f, forwardLength: 4.80f, forwardOffset: 2.80f,
        dashDistance: 2.50f, dashManualDuration: 0.40f,
        maxHalfAngleDeg: 20f, preferAimForward: true);

    // Prefab circle r=0.50 + Target Radius pad. MaxHalfAngle 10°: at ~3m without the cap
    // capsule admits ~15–17° body sweeps. Gate on BODY only — remote EntityAimData is
    // chronically stale (~90–120°) even when the shot is on-target.
    private static HitArc PistolPrimaryArc => HitArc.MakeForwardCapsule(
        radius: 0.50f, forwardLength: 8.00f, lockForwardAtCastStart: false,
        decideNearWindupEnd: true, maxHalfAngleDeg: 10f);

    // ExplosiveShot Shot/Recast — thicker bullet (r=0.60). AimDirection snap during cast;
    // body often sweeps 20–40° while aim stays on-target — body OR aim, wider half-angle.
    private static HitArc PistolExplosiveShotArc => HitArc.MakeForwardCapsule(
        radius: 0.60f, forwardLength: 8.00f, forwardOffset: 0.70f,
        lockForwardAtCastStart: false, decideNearWindupEnd: true, maxHalfAngleDeg: 18f,
        preferAimForward: true);

    // Crossbow Snapshot — PrefabDump:
    //   maxCast=0.30s, Projectile range=12m speed=30m/s, HitColliderCast Circle r=0.50,
    //   OffsetTranslationOnSpawn z=0.50, same late body-gate timing as pistol.
    private static HitArc CrossbowSnapshotArc => HitArc.MakeForwardCapsule(
        radius: 0.50f, forwardLength: 12.00f, forwardOffset: 0.50f,
        lockForwardAtCastStart: false, decideNearWindupEnd: true, maxHalfAngleDeg: 10f);

    // Crossbow Primary — PrefabDump:
    //   maxCast=1.00s (interruptible), Projectile range=12m speed=28m/s, Circle r=0.50,
    //   OffsetTranslationOnSpawn z=0.50. Remote projectile spawn does NOT replicate
    //   (wait-commit always timed out), so late-fire near windup end like pistol.
    //   Interrupt events still drop pending when they arrive.
    private static HitArc CrossbowPrimaryArc => HitArc.MakeForwardCapsule(
        radius: 0.50f, forwardLength: 12.00f, forwardOffset: 0.50f,
        lockForwardAtCastStart: false, decideNearWindupEnd: true, maxHalfAngleDeg: 12f);

    // Sword Shockwave (E) — PrefabDump:
    //   Projectile range=14.00m exact, HitColliderCast Circle r=0.45. Segment ends at 14m;
    //   endPad (= radius+targetPad ≈1.0m) covers the tip sphere so head-on tip ≈15.0m.
    //   ForwardLength 14.75 previously allowed false commit-fire at 15.3–15.6m.
    private static HitArc SwordShockwaveArc => HitArc.MakeForwardCapsule(
        radius: 0.45f, forwardLength: 14.00f,
        lockForwardAtCastStart: false, decideNearWindupEnd: true, maxHalfAngleDeg: 14f,
        waitForCastCommit: true, targetRadiusPad: 0.55f);

    /// <summary>
    /// Spawned on the caster when Shockwave Main actually commits (AbilitySpawnPrefabOnCast).
    /// Used as a remote-safe commit signal — <c>AbilityPreCastFinishedEvent</c> does not
    /// replicate for other players on the client.
    /// </summary>
    private const int SwordShockwaveBlockBuffHash = -518835107; // AB_Vampire_Sword_Shockwave_Main_BlockBuff

    /// <summary>
    /// Prefab hashes that mean a WaitForCastCommit ability has left the caster's hand
    /// (buff or projectile). Maps to (groupHash, castHash) for pending match.
    /// </summary>
    private static readonly Dictionary<int, (int Group, int Cast)> CommitSpawnHashes = new()
    {
        { SwordShockwaveBlockBuffHash, (SwordShockwaveGroupHash, SwordShockwaveCastHash) },
    };

    /// <summary>
    /// Ignore the first few ms of a projectile cast (spawn noise), then open a
    /// distance-scaled decision window before leave (see <see cref="LeaveAimDecisionLead"/>).
    /// </summary>
    private const float NearWindupSettleSeconds = 0.04f;
    /// <summary>Far-range lead before leave (travel time already covers activation).</summary>
    private const float NearWindupDecisionSeconds = 0.18f;
    /// <summary>Close-range lead before leave — counter must be up before the fast hit.</summary>
    private const float NearWindupCloseDecisionSeconds = 0.38f;
    private const float LeaveAimProjectileSpeed = 28f;
    private const int NearWindupConfirmFrames = 2;

    // ===== Counter ability detection ========================================

    /// <summary>
    /// Ability bar slot indices for the two spell slots. Determined empirically from the
    /// AbilityGroupSlotBuffer dump: slots 0–8 have ShowOnBar=true, and indices 5/6 are the
    /// two player-chosen spell slots (mapped to R and C by default).
    /// </summary>
    private const int SpellSlot1Index = 5;
    private const int SpellSlot2Index = 6;

    /// <summary>
    /// Parry-style counter abilities — create a counter/parry window when activated.
    /// Controlled by <see cref="Config.Extras.AutoCounterUseCounters"/>.
    /// </summary>
    private static readonly HashSet<int> CounterAbilityHashes = new()
    {
        1191439206,    // AB_Blood_BloodRite_AbilityGroup
        110097606,     // AB_Illusion_MistTrance_AbilityGroup
        1952703098,    // AB_Storm_Discharge_AbilityGroup
    };

    /// <summary>
    /// Damage-soaking barrier abilities. Controlled by
    /// <see cref="Config.Extras.AutoCounterUseBarriers"/>.
    /// </summary>
    private static readonly HashSet<int> BarrierAbilityHashes = new()
    {
        -1016145613,   // AB_Chaos_Barrier_AbilityGroup
        1293609465,    // AB_FrostBarrier_AbilityGroup
        -1136860480,   // AB_Unholy_WardOfTheDamned_AbilityGroup
    };

    /// <summary>
    /// Active-window buffs applied to the caster when a counter/barrier is already up.
    /// If any of these are present on the local player, Auto Counter must not press a
    /// second defensive ability (manual BloodRite then auto Barrier wastes the first).
    /// Two Chaos Barrier hashes cover older/newer prefab dumps.
    /// </summary>
    private static readonly HashSet<int> ActiveProtectiveBuffHashes = new()
    {
        154981615,     // AB_Blood_BloodRite_Buff
        -844883320,    // AB_Illusion_MistTrance_Buff
        -755938348,    // AB_Storm_Discharge_Counter_Buff (active parry window)
        -352442632,    // AB_Chaos_Barrier_Buff
        27716972,      // AB_Chaos_Barrier_Buff (alt dump)
        -146943441,    // AB_FrostBarrier_Buff
        463497101,     // AB_Unholy_WardOfTheDamned_Buff
    };

    /// <summary>
    /// Exact prefab names for the main protective buffs — patch-resilient fallback when a
    /// hash drifts. SpellMod / Recast / Pulse / AreaTrigger variants are intentionally omitted.
    /// </summary>
    private static readonly HashSet<string> ActiveProtectiveBuffNames = new(StringComparer.Ordinal)
    {
        "AB_Blood_BloodRite_Buff",
        "AB_Illusion_MistTrance_Buff",
        "AB_Storm_Discharge_Counter_Buff",
        "AB_Chaos_Barrier_Buff",
        "AB_FrostBarrier_Buff",
        "AB_Unholy_WardOfTheDamned_Buff",
    };

    private static bool IsAllowedCounterAbility(int equippedHash)
    {
        if (CounterAbilityHashes.Contains(equippedHash))
            return Config.Extras.AutoCounterUseCounters.Value;
        if (BarrierAbilityHashes.Contains(equippedHash))
            return Config.Extras.AutoCounterUseBarriers.Value;
        return false;
    }

    /// <summary>
    /// True when the local player already has an active counter or barrier buff — pressing
    /// another defensive skill would stack uselessly / burn the second CD.
    /// </summary>
    private static bool HasActiveProtectiveBuff(Entity local, out string buffName)
    {
        buffName = string.Empty;
        if (!local.HasBuffer<BuffBuffer>()) return false;

        var buffs = local.ReadBuffer<BuffBuffer>();
        for (var i = 0; i < buffs.Length; i++)
        {
            var guid = buffs[i].PrefabGuid;
            if (ActiveProtectiveBuffHashes.Contains(guid.GuidHash))
            {
                buffName = VWorld.PrefabLookupMap.GetName(guid);
                if (string.IsNullOrEmpty(buffName))
                    buffName = $"hash={guid.GuidHash}";
                return true;
            }

            var name = VWorld.PrefabLookupMap.GetName(guid);
            if (string.IsNullOrEmpty(name)) continue;
            if (!ActiveProtectiveBuffNames.Contains(name)) continue;

            buffName = name;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Result of scanning the local player's ability bar for equipped counter abilities.
    /// </summary>
    private struct EquippedCounter
    {
        public KeyCode Key;
        public string AbilityName;
        public bool OnCooldown;
    }

    /// <summary>
    /// Scan the local player's <see cref="AbilityGroupSlotBuffer"/> for counter abilities in
    /// the two spell slots. Returns true and populates <paramref name="counter"/> with the
    /// key to press. When both slots have counters, picks the one that is off cooldown;
    /// if both are ready, prefers slot 1.
    /// </summary>
    private static bool TryResolveCounterKey(Entity local, out EquippedCounter counter)
    {
        counter = default;
        if (!local.HasBuffer<AbilityGroupSlotBuffer>()) return false;

        var slots = local.ReadBuffer<AbilityGroupSlotBuffer>();
        var slot1Key = Config.Extras.AutoCounterSpellSlot1Key.Value;
        var slot2Key = Config.Extras.AutoCounterSpellSlot2Key.Value;

        // Check both spell slots; prefer the one that is off cooldown.
        var has1 = TryCheckSlot(slots, SpellSlot1Index, slot1Key, out var counter1);
        var has2 = TryCheckSlot(slots, SpellSlot2Index, slot2Key, out var counter2);

        if (has1 && has2)
        {
            // Both equipped — pick the one off cooldown; prefer slot 1 if both ready.
            if (!counter1.OnCooldown) { counter = counter1; return true; }
            if (!counter2.OnCooldown) { counter = counter2; return true; }
            // Both on cooldown — don't fire.
            return false;
        }
        if (has1) { counter = counter1; return !counter1.OnCooldown; }
        if (has2) { counter = counter2; return !counter2.OnCooldown; }
        return false;
    }

    /// <summary>
    /// Human-readable why <see cref="TryResolveCounterKey"/> failed — distinguishes
    /// "BloodRite on cooldown" from "no counter equipped" so range-gate successes are
    /// not mistaken for geometry misses in the debug log.
    /// </summary>
    private static string DescribeCounterSlotBlock(Entity local)
    {
        if (!local.HasBuffer<AbilityGroupSlotBuffer>())
            return "no AbilityGroupSlotBuffer on local";

        var slots = local.ReadBuffer<AbilityGroupSlotBuffer>();
        var slot1Key = Config.Extras.AutoCounterSpellSlot1Key.Value;
        var slot2Key = Config.Extras.AutoCounterSpellSlot2Key.Value;
        var has1 = TryCheckSlot(slots, SpellSlot1Index, slot1Key, out var c1);
        var has2 = TryCheckSlot(slots, SpellSlot2Index, slot2Key, out var c2);

        if (!has1 && !has2)
            return "no counter/barrier in spell slots 1–2 (check equipped skills + Counters/Barriers toggles)";
        if (has1 && c1.OnCooldown && has2 && c2.OnCooldown)
            return $"all counters on cooldown (slot1='{c1.AbilityName}', slot2='{c2.AbilityName}')";
        if (has1 && c1.OnCooldown && !has2)
            return $"counter on cooldown (slot1='{c1.AbilityName}')";
        if (has2 && c2.OnCooldown && !has1)
            return $"counter on cooldown (slot2='{c2.AbilityName}')";
        if (has1 && c1.OnCooldown)
            return $"counter on cooldown (slot1='{c1.AbilityName}'; slot2 has no counter)";
        if (has2 && c2.OnCooldown)
            return $"counter on cooldown (slot2='{c2.AbilityName}'; slot1 has no counter)";
        return "no counter ability ready in spell slots";
    }

    private static bool TryCheckSlot(DynamicBuffer<AbilityGroupSlotBuffer> slots, int index, KeyCode key, out EquippedCounter counter)
    {
        counter = default;
        if (key == KeyCode.None) return false;
        if (index >= slots.Length) return false;

        var slot = slots[index];
        var slotEntity = slot.GroupSlotEntity._Entity;
        if (slotEntity == Entity.Null || !slotEntity.Exists()) return false;
        if (!slotEntity.TryGetComponent<AbilityGroupSlot>(out var groupSlot)) return false;

        var equippedHash = groupSlot.GroupGuid.Value.GuidHash;
        if (!IsAllowedCounterAbility(equippedHash)) return false;

        // Check cooldown: GroupSlotEntity → AbilityGroupSlot.StateEntity (group state)
        //                → AbilityStateBuffer[i].StateEntity (ability entity)
        //                → AbilityCooldownState
        var onCooldown = IsSlotOnCooldown(groupSlot);

        counter = new EquippedCounter
        {
            Key = key,
            AbilityName = VWorld.PrefabLookupMap.GetName(groupSlot.GroupGuid.Value),
            OnCooldown = onCooldown,
        };
        return true;
    }

    /// <summary>
    /// Walk from the group state entity into its <see cref="AbilityStateBuffer"/> children
    /// and check <see cref="AbilityCooldownState"/> on each ability entity. Returns true if
    /// any child has <c>CurrentCooldown &gt; 0</c>.
    /// </summary>
    /// <summary>
    /// Resolve server time from the <see cref="ServerTime"/> singleton, cached per query
    /// lifetime. Returns 0 if unavailable.
    /// </summary>
    private static EntityQuery _serverTimeQuery;
    private static bool _serverTimeQueryReady;

    private static double GetServerTime()
    {
        if (!_serverTimeQueryReady)
        {
            try
            {
                _serverTimeQuery = VWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ServerTime>());
                _serverTimeQueryReady = true;
            }
            catch { return 0.0; }
        }
        if (_serverTimeQuery.IsEmpty) return 0.0;
        try { return _serverTimeQuery.GetSingleton<ServerTime>().Time; }
        catch { return 0.0; }
    }

    private static bool IsSlotOnCooldown(AbilityGroupSlot groupSlot)
    {
        var groupStateEntity = groupSlot.StateEntity._Entity;
        if (groupStateEntity == Entity.Null || !groupStateEntity.Exists()) return false;
        if (!groupStateEntity.HasBuffer<AbilityStateBuffer>()) return false;

        var serverTime = GetServerTime();
        var abilityStates = groupStateEntity.ReadBuffer<AbilityStateBuffer>();
        for (var i = 0; i < abilityStates.Length; i++)
        {
            var abilityEntity = abilityStates[i].StateEntity._Entity;
            if (abilityEntity == Entity.Null || !abilityEntity.Exists()) continue;
            if (!abilityEntity.TryGetComponent<AbilityCooldownState>(out var cdState)) continue;
            if (cdState.CooldownEndTime - serverTime > 0.0) return true;
        }
        return false;
    }

    // ===== Mutable state ====================================================

    private static readonly HashSet<int> TriggerHashes = new();
    private static string _lastConfiguredHashes = string.Empty;

    /// <summary>
    /// First-observation cache of <see cref="AbilityCastTimeData.MaxCastTime"/> per cast
    /// guid hash. Used to cap the per-cast windup window so we stop rechecking once the
    /// hit has landed — otherwise a late mid-cast rotation would fire the counter after
    /// the fact and burn its cooldown for nothing. Cleared on game end.
    /// </summary>
    private static readonly Dictionary<int, float> CastWindupCache = new();

    private static readonly List<PendingCast> Pending = new();
    private static readonly List<int> PendingRemoveScratch = new();

    private static double _heldUntil;
    private static KeyCode _heldKey = KeyCode.None;

    /// <summary>
    /// After a local counter/barrier cast starts (manual or auto), suppress further auto
    /// presses until the protective buff is expected to be on the character. Covers the
    /// brief gap between key press and <c>BuffBuffer</c> update where the other spell slot
    /// would otherwise still look "ready".
    /// </summary>
    private static double _suppressUntil;
    private const double LocalProtectiveCastSuppressSeconds = 0.75;

    // ===== Public entry points ==============================================

    /// <summary>
    /// Called when the local player starts casting any known counter/barrier. Suppresses
    /// Auto Counter briefly so a manual BloodRite cannot be immediately followed by an
    /// auto Barrier press before the buff lands.
    /// </summary>
    internal static void OnLocalProtectiveCastStarted(PrefabGUID abilityGroupGuid)
    {
        if (!IsAllowedCounterAbility(abilityGroupGuid.GuidHash)) return;
        _suppressUntil = Time.realtimeSinceStartupAsDouble + LocalProtectiveCastSuppressSeconds;
    }

    /// <summary>
    /// Remote-safe cast commit: BlockBuff / spell projectile spawn on the enemy means the
    /// interruptible windup finished and the hit left their hand.
    /// </summary>
    internal static void OnEnemyCommitSpawnObserved(Entity owner, PrefabGUID spawnGuid)
    {
        if (owner == Entity.Null || !CommitSpawnHashes.TryGetValue(spawnGuid.GuidHash, out var ids))
            return;

        if (Config.Extras.AutoCounterDebugLog.Value)
            Plugin.Logger.LogInfo($"AutoCounterGate spawn-commit owner spawn={spawnGuid.GuidHash} group={ids.Group}");
        OnEnemyAbilityCastCommitted(owner, new PrefabGUID(ids.Group), new PrefabGUID(ids.Cast));
    }

    /// <summary>Backward-compatible alias used by <see cref="Patches.BuffSystemPatch"/>. </summary>
    internal static void OnEnemyBuffObserved(Entity owner, PrefabGUID buffGuid) =>
        OnEnemyCommitSpawnObserved(owner, buffGuid);

    /// <summary>
    /// Windup completed (<c>AbilityPreCastFinishedEvent</c>) or Shockwave BlockBuff spawn —
    /// projectile/hit is committing. Fires pending entries armed with
    /// <see cref="HitArc.WaitForCastCommit"/>.
    /// </summary>
    internal static void OnEnemyAbilityCastCommitted(Entity caster, PrefabGUID abilityGroupGuid, PrefabGUID abilityCastGuid)
    {
        if (!Config.Extras.AutoCounter.Enabled && !Config.Extras.AutoCounterDebugLog.Value) return;
        if (caster == Entity.Null || !caster.Exists()) return;

        var local = EntityList.LocalCharacter;
        if (local == Entity.Null || !local.Exists()) return;
        if (caster == local || caster.IsAlly(local)) return;

        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            var pending = Pending[i];
            if (pending.Caster != caster) continue;
            if (!PendingMatchesAbility(pending, abilityGroupGuid, abilityCastGuid)) continue;
            if (!pending.AwaitCastCommit) continue;

            Pending.RemoveAt(i);

            if (!Config.Extras.AutoCounter.Enabled) continue;
            if (!TryResolveHitArc(pending.GroupGuid, pending.CastGuid, out var arc)) continue;

            var geom = ComputeHitGeometry(caster, local);
            string reason = "geom invalid";
            if (!geom.Valid || !PassesHitArcGate(geom, arc, out reason))
            {
                if (Config.Extras.AutoCounterDebugLog.Value)
                    Plugin.Logger.LogInfo(
                        $"AutoCounterGate commit-skip group={pending.GroupGuid.GuidHash} " +
                        $"cast={pending.CastGuid.GuidHash} " +
                        $"dist={(geom.Valid ? geom.Distance.ToString("F1") : "?")}m " +
                        $"bodyAngle={(geom.Valid ? geom.BodyAngle.ToString("F0") : "?")}° " +
                        $"reason='{(string.IsNullOrEmpty(reason) ? "geom invalid" : reason)}'");
                continue;
            }

            if (Config.Extras.AutoCounterDebugLog.Value)
                Plugin.Logger.LogInfo(
                    $"AutoCounterGate commit-fire group={pending.GroupGuid.GuidHash} " +
                    $"cast={pending.CastGuid.GuidHash} elapsed={Time.realtimeSinceStartupAsDouble - pending.StartedAt:F2}s " +
                    $"dist={geom.Distance:F1}m bodyAngle={geom.BodyAngle:F0}°");
            FireCounter(pending.GroupGuid, pending.CastGuid);
            RemovePendingForCaster(caster);
            return;
        }
    }

    /// <summary>
    /// Cast interrupted before commit — drop any pending AutoCounter for this cast so we
    /// never press after the skill was cancelled mid-windup.
    /// </summary>
    internal static void OnEnemyAbilityCastInterrupted(Entity caster, PrefabGUID abilityGroupGuid, PrefabGUID abilityCastGuid)
    {
        if (caster == Entity.Null) return;

        var removed = false;
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            var pending = Pending[i];
            if (pending.Caster != caster) continue;
            if (!PendingMatchesAbility(pending, abilityGroupGuid, abilityCastGuid)) continue;
            Pending.RemoveAt(i);
            removed = true;
            if (Config.Extras.AutoCounterDebugLog.Value)
                Plugin.Logger.LogInfo(
                    $"AutoCounterGate interrupt-drop group={pending.GroupGuid.GuidHash} " +
                    $"cast={pending.CastGuid.GuidHash} elapsed={Time.realtimeSinceStartupAsDouble - pending.StartedAt:F2}s");
        }

        if (!removed && Config.Extras.AutoCounterDebugLog.Value)
        {
            // Useful when interrupt arrives without a matching pending (already fired/expired).
            Plugin.Logger.LogInfo(
                $"AutoCounterGate interrupt-seen group={abilityGroupGuid.GuidHash} cast={abilityCastGuid.GuidHash} (no pending)");
        }
    }

    private static bool PendingMatchesAbility(PendingCast pending, PrefabGUID groupGuid, PrefabGUID castGuid)
    {
        if (castGuid.GuidHash != 0 &&
            (pending.CastGuid.GuidHash == castGuid.GuidHash || pending.GroupGuid.GuidHash == castGuid.GuidHash))
            return true;
        if (groupGuid.GuidHash != 0 &&
            (pending.GroupGuid.GuidHash == groupGuid.GuidHash || pending.CastGuid.GuidHash == groupGuid.GuidHash))
            return true;
        return false;
    }

    /// <summary>
    /// Called from the cast-event patch for every observed non-local player cast. Always
    /// computes the cheap geometry so the debug log can run even when AutoCounter is off —
    /// that way you can park yourself near a sparring partner, watch the log, and pick
    /// per-ability range/cone numbers before adding hashes to the trigger list.
    /// </summary>
    internal static void OnEnemyAbilityCastObserved(Entity caster, PrefabGUID abilityGroupGuid, PrefabGUID abilityCastGuid,
        Entity abilityEntity = default)
    {
        if (caster == Entity.Null) return;

        var local = EntityList.LocalCharacter;
        if (local == Entity.Null || !local.Exists()) return;
        if (caster == local) return;
        if (caster.IsAlly(local)) return;

        RefreshTriggerHashes();
        var inTriggerList = TriggerHashes.Contains(abilityGroupGuid.GuidHash) ||
                            TriggerHashes.Contains(abilityCastGuid.GuidHash);

        var geom = ComputeHitGeometry(caster, local);

        // Debug log runs independent of the AutoCounter master toggle so you can dial in
        // values without auto-pressing keys you may not want pressed yet.
        if (Config.Extras.AutoCounterDebugLog.Value)
        {
            MaybeLogDebug(caster, abilityGroupGuid, abilityCastGuid, geom, inTriggerList);
            // First-time prefab graph dump for the user to read off TriggerShape dimensions.
            PrefabInspector.DumpAbilityCastOnce(abilityGroupGuid, abilityCastGuid);
            // Runtime component dumps: hunting a replicated rotation/aim target so the melee
            // gate can read where the body will end up instead of extrapolating its turn.
            PrefabInspector.DumpLiveEntityOnce(caster, "caster");
            PrefabInspector.DumpLiveEntityOnce(abilityEntity, "abilityCast");
        }

        // Shadow mode (master toggle off, debug log on): the whole decision pipeline still
        // runs and records what it *would* have pressed, but nothing reaches the game. The
        // enemy hit then lands or misses untouched, which is the only way to label a cast
        // against reality instead of against our own geometry guess.
        if (!Config.Extras.AutoCounter.Enabled && !ShadowMode) return;
        if (!inTriggerList) return;
        if (!TryResolveHitArc(abilityGroupGuid, abilityCastGuid, out var arc)) return;

        // Resolve windup first so we can scale the dash lookahead before the immediate-fire
        // gate check (otherwise abilities whose dash overruns the windup would over-project
        // their hit volume and flip the gate result wrong way at cast start).
        var windup = ResolveWindupSeconds(abilityCastGuid);
        RegisterOutcome(caster, abilityGroupGuid, abilityCastGuid, windup > 0f ? windup : 0.35, local);

        // Pistol-style: never immediate-fire. Arm pending alongside any sibling Cast01/02/03
        // already tracked for this caster — overwriting used to drop Cast01 when Cast02 began.
        // Sword E uses WaitForCastCommit: same arming, but fire only on PreCastFinished.
        if (arc.DecideNearWindupEnd)
        {
            if (windup <= 0f) windup = 0.35f;
            var startedAtLate = Time.realtimeSinceStartupAsDouble;
            Pending.Add(new PendingCast
            {
                Caster = caster,
                GroupGuid = abilityGroupGuid,
                CastGuid = abilityCastGuid,
                StartedAt = startedAtLate,
                ExpiresAt = startedAtLate + windup,
                LockedBodyForward = Vector3.zero,
                HasLockedForward = false,
                DecideNearWindupEnd = true,
                AwaitCastCommit = arc.WaitForCastCommit,
                LateGatePassFrames = 0,
            });
            if (Config.Extras.AutoCounterDebugLog.Value)
            {
                var kind = arc.WaitForCastCommit ? "wait-commit" : "pending-late";
                Plugin.Logger.LogInfo(
                    $"AutoCounterGate {kind} group={abilityGroupGuid.GuidHash} cast={abilityCastGuid.GuidHash} " +
                    $"windup={windup:F2}s dist={(geom.Valid ? geom.Distance.ToString("F1") : "?")}m " +
                    $"bodyAngle={(geom.Valid ? geom.BodyAngle.ToString("F0") : "?")}° pending={Pending.Count}");
            }
            return;
        }

        var castStartArc = ScaleDashForCast(arc, windup, elapsed: 0.0);

        // Immediate-fire only when BODY already covers us — that is the rotation the hit
        // collider will spawn with. Arcs that gate on the rotation goal skip this entirely:
        // a body covering us at cast start means nothing when the cast is about to rotate
        // away, and the pending path fires on the goal a frame or two later anyway.
        var bodyCoversNow = geom.Valid && !UsesLeadGate(castStartArc, geom) &&
                            PassesHitArcGateWithForward(geom, geom.BodyForward, castStartArc, out _);
        if (bodyCoversNow)
        {
            if (Config.Extras.AutoCounterDebugLog.Value)
                Plugin.Logger.LogInfo(
                    $"AutoCounterGate immediate-fire group={abilityGroupGuid.GuidHash} " +
                    $"dist={geom.Distance:F1}m bodyAngle={geom.BodyAngle:F0}° " +
                    $"aimAngle={(geom.HasAim ? geom.AimAngle.ToString("F0") : "n/a")}°");
            NoteDecision(caster, abilityCastGuid, "Immediate", geom, Time.realtimeSinceStartupAsDouble);
            FireCounter(abilityGroupGuid, abilityCastGuid);
            return;
        }

        // Caster wasn't aimed at us at cast start. Queue for per-frame rotation tracking;
        // the per-frame recheck in TickPending fires the moment the gate passes, capped by
        // the fire deadline (windup minus counter activation latency).
        if (windup <= 0f) return;

        // Melee-style: only one pending per caster (latest cast wins).
        RemovePendingForCaster(caster);

        var startedAt = Time.realtimeSinceStartupAsDouble;
        Pending.Add(new PendingCast
        {
            Caster = caster,
            GroupGuid = abilityGroupGuid,
            CastGuid = abilityCastGuid,
            StartedAt = startedAt,
            ExpiresAt = startedAt + windup,
            LockedBodyForward = geom.Valid ? geom.BodyForward : Vector3.zero,
            HasLockedForward = arc.LockForwardAtCastStart && geom.Valid,
            DecideNearWindupEnd = false,
            LateGatePassFrames = 0,
        });
        if (Config.Extras.AutoCounterDebugLog.Value)
        {
            var pendingGatePass = PassesHitArcGate(geom, castStartArc, out var gateReason);
            Plugin.Logger.LogInfo(
                $"AutoCounterGate pending-add group={abilityGroupGuid.GuidHash} windup={windup:F2}s " +
                $"dist={(geom.Valid ? geom.Distance.ToString("F1") : "?")}m " +
                $"reason='{(pendingGatePass ? "awaiting body turn" : (string.IsNullOrEmpty(gateReason) ? "gate fail" : gateReason))}'");
        }
    }

    private static void RemovePendingForCaster(Entity caster)
    {
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            if (Pending[i].Caster == caster)
                Pending.RemoveAt(i);
        }
    }

    /// <summary>
    /// Called every frame from <see cref="AutoCounterController"/>. Drives the pending
    /// recheck and releases the synthetic key press once its hold window expires. Also
    /// acts as the "release on disable" hook — if the user toggles AutoCounter off
    /// mid-press we let the key go on the next tick.
    /// </summary>
    internal static void Tick()
    {
        TickPending();
        TickOutcomes();

        if (_heldKey == KeyCode.None) return;

        var now = Time.realtimeSinceStartupAsDouble;
        if (now < _heldUntil && Config.Extras.AutoCounter.Enabled) return;

        InputSimulator.Release(_heldKey);
        _heldKey = KeyCode.None;
    }

    internal static void Reset()
    {
        if (_heldKey != KeyCode.None)
        {
            InputSimulator.Release(_heldKey);
            _heldKey = KeyCode.None;
        }
        _heldUntil = 0.0;
        _suppressUntil = 0.0;
        Pending.Clear();
        Outcomes.Clear();
        CastWindupCache.Clear();
        _serverTimeQueryReady = false;
        _serverTimeQuery = default;
        _liveSlasherArc = null;
        _liveSlasherResolved = false;
        PrefabInspector.Reset();
    }

    // ===== Pending recheck ==================================================

    /// <summary>
    /// Walk every pending cast each frame and re-evaluate the hit-arc gate. Drops entries
    /// when the fire deadline has passed, the caster goes invalid, or we successfully fire.
    /// Pistol Cast01/02/03 may all be pending for one caster at once.
    /// </summary>
    private static void TickPending()
    {
        if (Pending.Count == 0) return;
        if (!Config.Extras.AutoCounter.Enabled && !ShadowMode)
        {
            Pending.Clear();
            return;
        }

        var local = EntityList.LocalCharacter;
        if (local == Entity.Null || !local.Exists())
        {
            Pending.Clear();
            return;
        }

        var now = Time.realtimeSinceStartupAsDouble;
        PendingRemoveScratch.Clear();
        Entity firedForCaster = Entity.Null;

        for (var i = 0; i < Pending.Count; i++)
        {
            var pending = Pending[i];
            var caster = pending.Caster;

            if (firedForCaster != Entity.Null && caster == firedForCaster)
            {
                PendingRemoveScratch.Add(i);
                continue;
            }

            if (!caster.Exists() || caster.IsDisabled() || !caster.IsAlive())
            {
                if (Config.Extras.AutoCounterDebugLog.Value)
                    Plugin.Logger.LogInfo($"AutoCounterGate pending-drop group={pending.GroupGuid.GuidHash} reason='caster invalid/disabled/dead'");
                PendingRemoveScratch.Add(i);
                continue;
            }

            // Pistol decision: live-aim only, and only in the last NearWindupDecisionSeconds
            // of windup. Prefab tracks ProjectileAimDirection during cast, so cast-start lock
            // was rejecting real hits where they track onto you; early-open was pressing on
            // brief mid-windup snaps that never became the shot.
            // Sword E (AwaitCastCommit): fire only on BlockBuff / PreCastFinished.
            // No late-fire — mid-windup presses false-fire interrupted casts.
            if (pending.DecideNearWindupEnd)
            {
                if (pending.AwaitCastCommit)
                {
                    // Commit spawn can arrive slightly after maxCast due to replication lag.
                    const double CommitGraceSeconds = 0.35;
                    if (now > pending.ExpiresAt + CommitGraceSeconds)
                    {
                        if (Config.Extras.AutoCounterDebugLog.Value)
                            Plugin.Logger.LogInfo(
                                $"AutoCounterGate pending-drop group={pending.GroupGuid.GuidHash} " +
                                $"cast={pending.CastGuid.GuidHash} reason='commit timeout (t={now - pending.StartedAt:F2}s)'");
                        PendingRemoveScratch.Add(i);
                        continue;
                    }

                    var liveGeomWait = ComputeHitGeometry(caster, local);
                    if (!liveGeomWait.Valid) continue;
                    MaybeTracePending(i, ref pending, liveGeomWait, now);

                    // Sword E: fire only on BlockBuff / commit — never late-fire here.
                    continue;
                }

                var liveGeomLate = ComputeHitGeometry(caster, local);
                if (!liveGeomLate.Valid)
                {
                    if (now > pending.ExpiresAt)
                        PendingRemoveScratch.Add(i);
                    continue;
                }
                if (!TryResolveHitArc(pending.GroupGuid, pending.CastGuid, out var lateArc))
                {
                    PendingRemoveScratch.Add(i);
                    continue;
                }

                MaybeTracePending(i, ref pending, liveGeomLate, now);

                var windup = (float)(pending.ExpiresAt - pending.StartedAt);
                var decisionWindow = LeaveAimDecisionLead(liveGeomLate.Distance, windup);
                var settleAt = pending.StartedAt + NearWindupSettleSeconds;
                var decisionAt = pending.ExpiresAt - decisionWindow;
                if (decisionAt < settleAt) decisionAt = settleAt;

                if (now > pending.ExpiresAt)
                {
                    if (Config.Extras.AutoCounterDebugLog.Value)
                    {
                        PassesHitArcGate(liveGeomLate, lateArc, out var endReason);
                        Plugin.Logger.LogInfo(
                            $"AutoCounterGate pending-drop group={pending.GroupGuid.GuidHash} " +
                            $"cast={pending.CastGuid.GuidHash} reason='windup ended without fire " +
                            $"(t={now - pending.StartedAt:F2}s)' lastGate='{(string.IsNullOrEmpty(endReason) ? "pass" : endReason)}' " +
                            $"dist={liveGeomLate.Distance:F1}m body={liveGeomLate.BodyAngle:F0}° " +
                            $"aim={(liveGeomLate.HasAim ? liveGeomLate.AimAngle.ToString("F0") : "n/a")}°");
                    }
                    PendingRemoveScratch.Add(i);
                    continue;
                }

                if (now < decisionAt) continue;

                if (!PassesHitArcGate(liveGeomLate, lateArc, out _))
                {
                    if (pending.LateGatePassFrames != 0)
                    {
                        pending.LateGatePassFrames = 0;
                        Pending[i] = pending;
                    }

                    continue;
                }

                // Body sweeping past: angle climbing during confirms → reset.
                // PreferAimForward arcs (ExplosiveShot): aim is the leave direction — don't
                // reset on body jitter (logs showed 20→46° body with stable aim ~17°).
                const float SweepAngleRiseDeg = 2.5f;
                if (!lateArc.PreferAimForward &&
                    pending.LateGatePassFrames > 0 &&
                    liveGeomLate.BodyAngle > pending.LastConfirmAngle + SweepAngleRiseDeg)
                {
                    pending.LateGatePassFrames = 0;
                    pending.LastConfirmAngle = liveGeomLate.BodyAngle;
                    Pending[i] = pending;
                    continue;
                }

                pending.LateGatePassFrames++;
                pending.LastConfirmAngle = liveGeomLate.BodyAngle;
                Pending[i] = pending;
                if (pending.LateGatePassFrames < NearWindupConfirmFrames) continue;

                if (Config.Extras.AutoCounterDebugLog.Value)
                    Plugin.Logger.LogInfo(
                        $"AutoCounterGate late-fire group={pending.GroupGuid.GuidHash} " +
                        $"cast={pending.CastGuid.GuidHash} elapsed={now - pending.StartedAt:F2}s " +
                        $"dist={liveGeomLate.Distance:F1}m bodyAngle={liveGeomLate.BodyAngle:F0}° " +
                        $"aimAngle={(liveGeomLate.HasAim ? liveGeomLate.AimAngle.ToString("F0") : "n/a")}°");
                NoteDecision(caster, pending.CastGuid, "Late", liveGeomLate, now);
                FireCounter(pending.GroupGuid, pending.CastGuid);
                PendingRemoveScratch.Add(i);
                firedForCaster = caster;
                continue;
            }

            if (!TryResolveHitArc(pending.GroupGuid, pending.CastGuid, out var arc))
            {
                PendingRemoveScratch.Add(i);
                continue;
            }

            // Fire deadline: anything later than this won't activate counter in time to
            // parry the incoming hit, so firing is just a cooldown waste. Latency is a
            // user-tunable estimate of the SendInput→server→activate chain.
            // Ultra-short windups (lag ≥ windup): only the first snap window is useful.
            // Dashing melee boxes: the rotation goal can land on us in the last ~40ms, and a
            // press that only sometimes parries still beats never pressing — keep accepting
            // until windup end so deadline drops don't eat real incoming hits.
            var activationLatency = (double)Config.Extras.AutoCounterActivationLatencySeconds.Value;
            var windupLen = pending.ExpiresAt - pending.StartedAt;
            var fireDeadline = pending.ExpiresAt - activationLatency;
            if (arc.Shape == HitShape.ForwardBox && arc.DashDistance > 0f && !arc.DecideNearWindupEnd)
                fireDeadline = pending.ExpiresAt;
            else if (windupLen <= activationLatency + 0.05)
                fireDeadline = pending.StartedAt + 0.035;
            if (now > fireDeadline)
            {
                if (Config.Extras.AutoCounterDebugLog.Value)
                    Plugin.Logger.LogInfo(
                        $"AutoCounterGate pending-drop group={pending.GroupGuid.GuidHash} " +
                        $"reason='fire deadline elapsed (now={now - pending.StartedAt:F2}s past start, deadline={fireDeadline - pending.StartedAt:F2}s)'");
                PendingRemoveScratch.Add(i);
                continue;
            }

            var liveGeom = ComputeHitGeometry(caster, local);
            if (!liveGeom.Valid) continue;

            var geom = pending.HasLockedForward
                ? liveGeom.WithBodyForward(pending.LockedBodyForward)
                : liveGeom;

            var castWindup = pending.ExpiresAt - pending.StartedAt;
            var elapsed = now - pending.StartedAt;
            var decayedArc = ScaleDashForCast(arc, castWindup, elapsed);

            MaybeTracePending(i, ref pending, geom, now);

            var turnRate = SampleTurnRate(ref pending, geom, now);
            var leadRate = SampleLeadRate(ref pending, geom, now);
            var via = ResolvePendingGateVia(geom, decayedArc, arc, elapsed, turnRate,
                (float)(pending.ExpiresAt - now));

            if (via == PendingGateVia.None)
            {
                pending.LateGatePassFrames = 0;
                Pending[i] = pending;
                continue;
            }

            // Body cover fires at once, and so does a predicted cover — its turn rate is
            // already measured across a full window. The rotation goal (Lead) fires at once
            // too: it is a replicated target rather than a derived guess, and every frame
            // spent confirming is a frame the counter no longer has to activate in. A measured
            // hit was lost to exactly that wait — the goal landed on us 0.01s inside the
            // deadline and the second confirmation frame never arrived in time.
            // Aim-lead still rests on a single cursor sample, so it needs consecutive frames
            // and resets if the signal swings back off us (cursor dragged across us = no hit).
            if (via == PendingGateVia.AimLead)
            {
                const float SweepAwayRiseDeg = 2.5f;
                var confirmAngle = geom.BodyAngle;
                if (pending.LateGatePassFrames > 0 &&
                    confirmAngle > pending.LastConfirmAngle + SweepAwayRiseDeg)
                {
                    pending.LateGatePassFrames = 0;
                    pending.LastConfirmAngle = confirmAngle;
                    Pending[i] = pending;
                    continue;
                }

                pending.LateGatePassFrames++;
                pending.LastConfirmAngle = confirmAngle;
                Pending[i] = pending;
                if (pending.LateGatePassFrames < AimLeadConfirmFrames) continue;
            }

            if (Config.Extras.AutoCounterDebugLog.Value)
                Plugin.Logger.LogInfo(
                    $"AutoCounterGate pending-fire group={pending.GroupGuid.GuidHash} " +
                    $"elapsed={(now - pending.StartedAt):F2}s anchor={decayedArc.EffectiveForward:F2}m " +
                    $"(offset={decayedArc.ForwardOffset:F2}+dashRem={decayedArc.DashRemaining:F2}) " +
                    $"bodyAngle={geom.BodyAngle:F0}° aimAngle={(geom.HasAim ? geom.AimAngle.ToString("F0") : "n/a")}° " +
                    $"lead={(geom.HasLead ? $"{geom.LeadAngle:F0}°({geom.LeadSource})" : "n/a")} " +
                    $"turn={turnRate:F0}°/s leadTurn={leadRate:F0}°/s via={via}");
            NoteDecision(caster, pending.CastGuid, via.ToString(), geom, now);
            FireCounter(pending.GroupGuid, pending.CastGuid);
            PendingRemoveScratch.Add(i);
            firedForCaster = caster;
        }

        // Remove highest indices first so earlier indices stay valid.
        PendingRemoveScratch.Sort();
        for (var r = PendingRemoveScratch.Count - 1; r >= 0; r--)
            Pending.RemoveAt(PendingRemoveScratch[r]);
        PendingRemoveScratch.Clear();

        // After a successful fire, drop remaining pendings for that caster (same pistol burst).
        if (firedForCaster != Entity.Null)
            RemovePendingForCaster(firedForCaster);
    }

    /// <summary>
    /// Per-frame body trace for one pending cast, emitted only when the body angle changes
    /// meaningfully since the last log. Throttled to ~3° deltas so a 60fps recheck doesn't
    /// flood the log with near-duplicates of an unchanging value.
    /// </summary>
    private const float TraceAngleDeltaThresholdDeg = 3f;

    private static void MaybeTracePending(int index, ref PendingCast pending, HitGeometry geom, double now)
    {
        if (!Config.Extras.AutoCounterDebugLog.Value) return;
        if (!geom.Valid) return;

        if (pending.HasTraced &&
            Mathf.Abs(geom.BodyAngle - pending.LastTracedBodyAngle) < TraceAngleDeltaThresholdDeg &&
            Mathf.Abs(geom.LeadAngle - pending.LastTracedLeadAngle) < TraceAngleDeltaThresholdDeg)
            return;

        var aimStr = geom.HasAim ? $"{geom.AimAngle:F0}°" : "n/a";
        var leadStr = geom.HasLead ? $"{geom.LeadAngle:F0}°({geom.LeadSource})" : "n/a";
        Plugin.Logger.LogInfo(
            $"AutoCounterTrace group={pending.GroupGuid.GuidHash} cast={pending.CastGuid.GuidHash} " +
            $"t={now - pending.StartedAt:F2}s dist={geom.Distance:F1}m bodyAngle={geom.BodyAngle:F0}° " +
            $"aimAngle={aimStr} lead={leadStr}");

        pending.LastTracedBodyAngle = geom.BodyAngle;
        pending.LastTracedLeadAngle = geom.LeadAngle;
        pending.HasTraced = true;
        Pending[index] = pending;
    }

    // ===== Outcome tracking =================================================

    /// <summary>
    /// Post-mortem for one observed cast. Every gate tweak so far was calibrated against our
    /// own geometric guess of whether a cast would land — never against what actually
    /// happened. This records the real label (did the local player's health drop?) next to
    /// what each signal claimed and when, so the gate can be fitted to reality.
    ///
    /// Run it in shadow mode (AutoCounter off, debug log on): the pipeline decides but never
    /// presses, so the enemy hit resolves untouched and the label is trustworthy. In live
    /// mode a correctly countered hit deals no damage, which is indistinguishable from a
    /// false fire — the verdict wording says so.
    /// </summary>
    private struct CastOutcome
    {
        public Entity Caster;
        public PrefabGUID GroupGuid;
        public PrefabGUID CastGuid;
        public double StartedAt;
        public double EndsAt;
        public double JudgeAt;
        public double Windup;
        public float StartHealth;
        public float MinHealth;

        public bool Decided;
        public double DecidedAt;
        public string DecidedVia;
        public float DecidedBodyAngle;
        public float DecidedLeadAngle;
        public float DecidedDistance;

        public float MinBodyAngle;
        public float MinLeadAngle;
        public bool BodyCovered;
        public double BodyCoveredAt;
        public bool LeadCovered;
        public double LeadCoveredAt;

        public bool HasEndSample;
        public float EndBodyAngle;
        public float EndLeadAngle;
        public float EndDistance;
        public bool EndBodyCovers;
    }

    private static readonly List<CastOutcome> Outcomes = new();
    // Damage resolves server-side at windup end and the health drop reaches us about half a
    // ping later; judge well after that so a real hit is never scored as a miss.
    private const double OutcomeJudgeDelaySeconds = 0.75;
    private const float OutcomeHealthDropThreshold = 1f;
    private const float OutcomeAngleUnset = 999f;

    private static bool ShadowMode =>
        !Config.Extras.AutoCounter.Enabled && Config.Extras.AutoCounterDebugLog.Value;

    private static float ReadHealth(Entity local) =>
        local.TryGetComponent<Health>(out var health) ? health.Value : -1f;

    private static void RegisterOutcome(Entity caster, PrefabGUID groupGuid, PrefabGUID castGuid,
        double windup, Entity local)
    {
        if (!Config.Extras.AutoCounterDebugLog.Value) return;

        var startedAt = Time.realtimeSinceStartupAsDouble;
        var hp = ReadHealth(local);
        Outcomes.Add(new CastOutcome
        {
            Caster = caster,
            GroupGuid = groupGuid,
            CastGuid = castGuid,
            StartedAt = startedAt,
            EndsAt = startedAt + windup,
            JudgeAt = startedAt + windup + OutcomeJudgeDelaySeconds,
            Windup = windup,
            StartHealth = hp,
            MinHealth = hp,
            MinBodyAngle = OutcomeAngleUnset,
            MinLeadAngle = OutcomeAngleUnset,
            EndBodyAngle = OutcomeAngleUnset,
            EndLeadAngle = OutcomeAngleUnset,
            DecidedVia = string.Empty,
        });
    }

    /// <summary>Attach the gate's fire decision to the open post-mortem for this cast.</summary>
    private static void NoteDecision(Entity caster, PrefabGUID castGuid, string via, HitGeometry geom, double now)
    {
        if (!Config.Extras.AutoCounterDebugLog.Value) return;

        // Newest first, and only a cast still inside its windup. A record stays open for
        // ~0.75s past impact to catch the delayed health drop, so the same enemy recasting the
        // same ability used to hand its decision to the previous cast's record — which crossed
        // the labels, printing a fire time of 1.17s and marking the fired cast as no-fire.
        for (var i = Outcomes.Count - 1; i >= 0; i--)
        {
            var outcome = Outcomes[i];
            if (outcome.Decided || outcome.Caster != caster ||
                outcome.CastGuid.GuidHash != castGuid.GuidHash ||
                now > outcome.EndsAt) continue;

            outcome.Decided = true;
            outcome.DecidedAt = now - outcome.StartedAt;
            outcome.DecidedVia = via;
            outcome.DecidedBodyAngle = geom.Valid ? geom.BodyAngle : OutcomeAngleUnset;
            outcome.DecidedLeadAngle = geom.Valid && geom.HasLead ? geom.LeadAngle : OutcomeAngleUnset;
            outcome.DecidedDistance = geom.Valid ? geom.Distance : -1f;
            Outcomes[i] = outcome;
            return;
        }
    }

    private static void TickOutcomes()
    {
        if (Outcomes.Count == 0) return;
        if (!Config.Extras.AutoCounterDebugLog.Value)
        {
            Outcomes.Clear();
            return;
        }

        var local = EntityList.LocalCharacter;
        if (local == Entity.Null || !local.Exists())
        {
            Outcomes.Clear();
            return;
        }

        var now = Time.realtimeSinceStartupAsDouble;
        var hp = ReadHealth(local);

        for (var i = Outcomes.Count - 1; i >= 0; i--)
        {
            var outcome = Outcomes[i];
            if (hp >= 0f && (outcome.MinHealth < 0f || hp < outcome.MinHealth)) outcome.MinHealth = hp;

            SampleOutcomeGeometry(ref outcome, local, now);

            if (now < outcome.JudgeAt)
            {
                Outcomes[i] = outcome;
                continue;
            }

            LogOutcome(outcome);
            Outcomes.RemoveAt(i);
        }
    }

    /// <summary>
    /// Per-frame record of what each signal claimed during the windup, plus a snapshot at the
    /// impact frame — the hit collider spawns with the body rotation the caster holds then,
    /// with the dash fully spent, so that sample is the closest we get to the real hit volume.
    /// </summary>
    private static void SampleOutcomeGeometry(ref CastOutcome outcome, Entity local, double now)
    {
        var caster = outcome.Caster;
        if (!caster.Exists() || caster.IsDisabled()) return;
        if (!TryResolveHitArc(outcome.GroupGuid, outcome.CastGuid, out var arc)) return;

        var elapsed = now - outcome.StartedAt;
        if (elapsed > outcome.Windup + 0.10) return;

        var geom = ComputeHitGeometry(caster, local);
        if (!geom.Valid) return;

        if (geom.BodyAngle < outcome.MinBodyAngle) outcome.MinBodyAngle = geom.BodyAngle;
        if (geom.HasLead && geom.LeadAngle < outcome.MinLeadAngle) outcome.MinLeadAngle = geom.LeadAngle;

        var decayed = ScaleDashForCast(arc, outcome.Windup, Math.Min(elapsed, outcome.Windup));
        if (!outcome.BodyCovered && PassesHitArcGateWithForward(geom, geom.BodyForward, decayed, out _))
        {
            outcome.BodyCovered = true;
            outcome.BodyCoveredAt = elapsed;
        }

        if (!outcome.LeadCovered && geom.HasLead &&
            PassesHitArcGateWithForward(geom, geom.LeadForward, decayed, out _))
        {
            outcome.LeadCovered = true;
            outcome.LeadCoveredAt = elapsed;
        }

        if (outcome.HasEndSample || now < outcome.EndsAt) return;

        outcome.HasEndSample = true;
        outcome.EndBodyAngle = geom.BodyAngle;
        outcome.EndLeadAngle = geom.HasLead ? geom.LeadAngle : OutcomeAngleUnset;
        outcome.EndDistance = geom.Distance;
        outcome.EndBodyCovers = PassesHitArcGateWithForward(geom, geom.BodyForward,
            ScaleDashForCast(arc, outcome.Windup, outcome.Windup), out _);
    }

    private static void LogOutcome(CastOutcome outcome)
    {
        var drop = outcome.StartHealth > 0f && outcome.MinHealth >= 0f
            ? outcome.StartHealth - outcome.MinHealth
            : 0f;
        var hit = drop > OutcomeHealthDropThreshold;
        var shadow = ShadowMode;

        string verdict;
        if (outcome.Decided)
            verdict = hit
                ? (shadow ? "TRUE-FIRE" : "FIRED-TOO-LATE")
                : (shadow ? "FALSE-FIRE" : "FIRED(blocked-or-false)");
        else
            verdict = hit ? "MISSED-HIT" : "TRUE-SKIP";

        var decision = outcome.Decided
            ? $"fire@{outcome.DecidedAt:F2}s({outcome.DecidedVia} body={FormatOutcomeAngle(outcome.DecidedBodyAngle)}° " +
              $"lead={FormatOutcomeAngle(outcome.DecidedLeadAngle)}° dist={outcome.DecidedDistance:F1}m)"
            : "no-fire";

        Plugin.Logger.LogInfo(
            $"AutoCounterOutcome mode={(shadow ? "shadow" : "live")} verdict={verdict} " +
            $"group={outcome.GroupGuid.GuidHash} hp-drop={drop:F0}({(hit ? "HIT" : "no-hit")}) " +
            $"decision={decision} " +
            $"body(min={FormatOutcomeAngle(outcome.MinBodyAngle)}° end={FormatOutcomeAngle(outcome.EndBodyAngle)}° " +
            $"cover={FormatCoverTime(outcome.BodyCovered, outcome.BodyCoveredAt)} " +
            $"endCover={(outcome.EndBodyCovers ? "yes" : "no")}) " +
            $"lead(min={FormatOutcomeAngle(outcome.MinLeadAngle)}° end={FormatOutcomeAngle(outcome.EndLeadAngle)}° " +
            $"cover={FormatCoverTime(outcome.LeadCovered, outcome.LeadCoveredAt)}) " +
            $"endDist={(outcome.HasEndSample ? outcome.EndDistance.ToString("F1") : "?")}m");
    }

    private static string FormatOutcomeAngle(float angle) =>
        angle >= OutcomeAngleUnset || angle < 0f ? "?" : angle.ToString("F0");

    private static string FormatCoverTime(bool covered, double at) =>
        covered ? $"@{at:F2}s" : "never";

    // ===== Press path =======================================================

    /// <summary>
    /// Shared press path used by both the immediate trigger and the windup recheck.
    /// Honours the held-key invariant and skips when a protective buff is already up.
    /// </summary>
    private static void FireCounter(PrefabGUID abilityGroupGuid, PrefabGUID abilityCastGuid)
    {
        var now = Time.realtimeSinceStartupAsDouble;

        // Shadow mode: the decision is already recorded, the press must not happen — the
        // whole point is letting the enemy hit resolve so the outcome log can label it.
        if (ShadowMode)
        {
            Plugin.Logger.LogInfo(
                $"AutoCounterGate shadow-fire group={abilityGroupGuid.GuidHash} " +
                $"(master toggle off — decision recorded, nothing pressed)");
            return;
        }

        // Dynamically resolve which key to press by scanning the local player's equipped
        // abilities. If no counter ability is in a spell slot, we do nothing.
        var local = EntityList.LocalCharacter;
        if (local == Entity.Null || !local.Exists())
        {
            if (Config.Extras.AutoCounterDebugLog.Value)
                Plugin.Logger.LogInfo($"AutoCounterGate fire-skipped group={abilityGroupGuid.GuidHash} reason='no local character'");
            return;
        }

        if (HasActiveProtectiveBuff(local, out var activeBuff))
        {
            if (Config.Extras.AutoCounterDebugLog.Value)
                Plugin.Logger.LogInfo(
                    $"AutoCounterGate fire-skipped group={abilityGroupGuid.GuidHash} " +
                    $"reason='active protective buff ({activeBuff})'");
            return;
        }

        if (now < _suppressUntil)
        {
            if (Config.Extras.AutoCounterDebugLog.Value)
                Plugin.Logger.LogInfo(
                    $"AutoCounterGate fire-skipped group={abilityGroupGuid.GuidHash} " +
                    $"reason='local protective cast suppress ({(_suppressUntil - now):F2}s left)'");
            return;
        }

        if (!TryResolveCounterKey(local, out var counter))
        {
            if (Config.Extras.AutoCounterDebugLog.Value)
                Plugin.Logger.LogInfo(
                    $"AutoCounterGate fire-skipped group={abilityGroupGuid.GuidHash} " +
                    $"reason='{DescribeCounterSlotBlock(local)}'");
            return;
        }

        var key = counter.Key;

        // Release any still-held key first — otherwise the OS sees a stuck key and the
        // second "down" never registers as a fresh edge.
        if (_heldKey != KeyCode.None)
        {
            InputSimulator.Release(_heldKey);
            _heldKey = KeyCode.None;
        }

        // Arm suppress before SendInput so a same-frame sibling cast (spear Cast01→02)
        // cannot double-press before the buff/CD checks catch up.
        _suppressUntil = now + LocalProtectiveCastSuppressSeconds;

        if (!InputSimulator.Press(key))
        {
            _suppressUntil = now; // press failed — don't block a retry
            if (Config.Extras.AutoCounterDebugLog.Value)
                Plugin.Logger.LogInfo($"AutoCounterGate fire-skipped group={abilityGroupGuid.GuidHash} reason='InputSimulator.Press({key}) returned false'");
            return;
        }

        _heldKey = key;
        _heldUntil = now + HoldDurationSeconds;

        if (Config.Extras.LogAbilityCasts.Value > 0 || Config.Extras.AutoCounterDebugLog.Value)
        {
            var abilityName = VWorld.PrefabLookupMap.GetName(abilityCastGuid);
            Plugin.Logger.LogInfo($"AutoCounter pressed '{key}' (counter='{counter.AbilityName}') against '{abilityName}' (group hash={abilityGroupGuid.GuidHash})");
        }
    }

    // ===== Geometry / gate ==================================================

    /// <summary>
    /// How far before projectile leave we open the late-fire gate.
    /// Close range: large lead (bullet arrives ~dist/28s after leave; BloodRite needs
    /// headroom). Far range: smaller lead — travel time already buys activation latency.
    /// Short windups are capped so we don't decide on the first frame.
    /// </summary>
    private static float LeaveAimDecisionLead(float distance, float windup)
    {
        var travel = distance > 0.1f ? distance / LeaveAimProjectileSpeed : 0f;
        var activationLag = Mathf.Max(0f, Config.Extras.AutoCounterActivationLatencySeconds.Value);

        // Ideal lead so counter is active at hit: activationLag + buffer - travel.
        // Close (small travel) → large lead; far → shrinks toward the far floor.
        var lead = activationLag + 0.08f - travel;
        // Blend in the close/far floors so face-range always opens earlier than 0.18s.
        var rangeT = Mathf.InverseLerp(3f, 10f, distance); // 0 at ≤3m, 1 at ≥10m
        var floor = Mathf.Lerp(NearWindupCloseDecisionSeconds, NearWindupDecisionSeconds, rangeT);
        lead = Mathf.Max(lead, floor);

        // Don't open before settle; leave at least a sliver of windup for confirm frames.
        var maxLead = Mathf.Max(0f, windup - NearWindupSettleSeconds - 0.02f);
        // Short casts (0.20s): allow up to ~70% lead so Cast02/03 can fire ~0.08–0.10s.
        maxLead = Mathf.Min(maxLead, Mathf.Max(windup * 0.70f, NearWindupDecisionSeconds));
        return Mathf.Clamp(lead, NearWindupDecisionSeconds * 0.5f, maxLead);
    }

    // EntityAimData.AimPosition is a world point: once the caster starts the 2.50m dash the
    // derived direction swings wildly (logs show aim 160°→178° while body turns onto us).
    // Aim is therefore only trusted in the first frames of the cast; after that the body,
    // which snaps onto the leave aim at 1500°/s, is extrapolated to hit time instead.
    private const float PreferAimLeadMaxDeg = 20f;
    private const double AimLeadEarlyWindowSeconds = 0.15;
    private const int AimLeadConfirmFrames = 2;
    private const float PredictMinTurnRateDegPerSec = 250f;
    private const float PredictMaxBodyAngleDeg = 140f;
    private const double TurnRateWindowSeconds = 0.06;

    private enum PendingGateVia
    {
        None,
        Body,
        Lead,
        AimLead,
        Predict,
    }

    /// <summary>
    /// Test the local character against the cast's hit volume: body forward, or cursor aim
    /// within the lead cap for <see cref="HitArc.PreferAimForward"/> arcs.
    /// </summary>
    private static bool PassesHitArcGate(HitGeometry geom, HitArc arc, out string reason)
    {
        if (PassesHitArcGateWithForward(geom, geom.BodyForward, arc, out reason))
            return true;

        if (!arc.PreferAimForward || !geom.HasAim)
            return false;

        var aimCap = arc.MaxHalfAngleDeg > 0f ? arc.MaxHalfAngleDeg : PreferAimLeadMaxDeg;
        if (geom.AimAngle > aimCap)
        {
            reason = $"aim lead rejected (aim={geom.AimAngle:F0}° > {aimCap:F0}°, body={geom.BodyAngle:F0}°)";
            return false;
        }

        return PassesHitArcGateWithForward(geom, geom.AimForward, arc, out reason);
    }

    /// <summary>
    /// Which signal, if any, says the cast will cover us at impact.
    /// Body cover is ground truth (the hit collider spawns with the owner rotation).
    /// Aim lead only counts in the first frames, before dash movement corrupts the cursor
    /// direction. Otherwise extrapolate the measured turn rate to the impact moment — this
    /// is what catches a flick that only lands on us at the very end of the windup.
    /// </summary>
    private static PendingGateVia ResolvePendingGateVia(HitGeometry geom, HitArc decayedArc, HitArc arc,
        double elapsed, float turnRateDegPerSec, float secondsToHit)
    {
        // Rotation goal known (Twinblade): it decides where the box points AND where the dash
        // goes at impact, so it is the only signal worth gating on, and only once it actually
        // covers us. Extrapolating the goal's own swing was measured at 2 correct fires out of
        // 9 — a fast drag past us reads identically to a drag onto us, and every false fire in
        // the live session came from that guess. The body turn-rate guess is no better here.
        if (UsesLeadGate(arc, geom))
        {
            return PassesHitArcGateWithForward(geom, geom.LeadForward, decayedArc, out _)
                ? PendingGateVia.Lead
                : PendingGateVia.None;
        }

        if (PassesHitArcGateWithForward(geom, geom.BodyForward, decayedArc, out _))
            return PendingGateVia.Body;

        if (!arc.PreferAimForward) return PendingGateVia.None;

        var aimCap = arc.MaxHalfAngleDeg > 0f ? arc.MaxHalfAngleDeg : PreferAimLeadMaxDeg;
        if (geom.HasAim && elapsed <= AimLeadEarlyWindowSeconds && geom.AimAngle <= aimCap &&
            PassesHitArcGateWithForward(geom, geom.AimForward, decayedArc, out _))
            return PendingGateVia.AimLead;

        if (arc.Shape == HitShape.ForwardBox && secondsToHit > 0f &&
            turnRateDegPerSec >= PredictMinTurnRateDegPerSec &&
            geom.BodyAngle <= PredictMaxBodyAngleDeg)
        {
            var predicted = TurnForwardToward(geom.BodyForward, geom.ToLocal, turnRateDegPerSec * secondsToHit);
            if (PassesHitArcGateWithForward(geom, predicted, decayedArc, out _))
                return PendingGateVia.Predict;
        }

        return PendingGateVia.None;
    }

    /// <summary>
    /// True when the caster exposes a rotation goal and the arc rotates onto it during the
    /// cast — those gate on the goal alone, never on the in-progress body sweep.
    /// </summary>
    private static bool UsesLeadGate(HitArc arc, HitGeometry geom) =>
        arc.PreferAimForward && arc.Shape == HitShape.ForwardBox && geom.HasLead;

    /// <summary>
    /// Rotate <paramref name="forward"/> toward <paramref name="toLocal"/> in the horizontal
    /// plane by at most <paramref name="degrees"/> — the caster's rotation snap stops once it
    /// reaches the leave aim, so overshoot past the target is clamped away.
    /// </summary>
    private static Vector3 TurnForwardToward(Vector3 forward, Vector3 toLocal, float degrees)
    {
        if (degrees <= 0f || forward.sqrMagnitude < 1e-6f) return forward;
        if (toLocal.x * toLocal.x + toLocal.z * toLocal.z < 1e-6f) return forward;

        var fromDeg = Mathf.Atan2(forward.z, forward.x) * Mathf.Rad2Deg;
        var toDeg = Mathf.Atan2(toLocal.z, toLocal.x) * Mathf.Rad2Deg;
        var delta = toDeg - fromDeg;
        while (delta > 180f) delta -= 360f;
        while (delta < -180f) delta += 360f;

        var step = Mathf.Min(degrees, Mathf.Abs(delta)) * Mathf.Sign(delta);
        var resultRad = (fromDeg + step) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(resultRad), 0f, Mathf.Sin(resultRad));
    }

    /// <summary>
    /// Degrees per second the caster's body is closing onto us (negative = turning away),
    /// measured over a window wide enough to span at least one replication update.
    /// </summary>
    private static float SampleTurnRate(ref PendingCast pending, HitGeometry geom, double now) =>
        SampleAngleRate(ref pending.HasPrevSample, ref pending.PrevBodyAngle, ref pending.PrevSampleAt,
            ref pending.LastTurnRate, geom.BodyAngle, now);

    /// <summary>Degrees per second the caster's rotation goal is closing onto us.</summary>
    private static float SampleLeadRate(ref PendingCast pending, HitGeometry geom, double now) =>
        SampleAngleRate(ref pending.HasPrevLeadSample, ref pending.PrevLeadAngle, ref pending.PrevLeadSampleAt,
            ref pending.LastLeadTurnRate, geom.LeadAngle, now);

    /// <summary>
    /// Closing rate of an angle, sampled over <see cref="TurnRateWindowSeconds"/>. Remote
    /// angles only move on network snapshots, so a frame-to-frame delta reads 0 on most
    /// ticks — the window is only rolled once it is wide enough, holding the last rate
    /// in between.
    /// </summary>
    private static float SampleAngleRate(ref bool hasPrev, ref float prevAngle, ref double prevAt,
        ref float lastRate, float angle, double now)
    {
        if (!hasPrev)
        {
            hasPrev = true;
            prevAngle = angle;
            prevAt = now;
            lastRate = 0f;
            return 0f;
        }

        var span = now - prevAt;
        if (span < TurnRateWindowSeconds) return lastRate;

        lastRate = (prevAngle - angle) / (float)span;
        prevAngle = angle;
        prevAt = now;
        return lastRate;
    }

    private static bool PassesHitArcGateWithForward(HitGeometry geom, Vector3 forward, HitArc arc, out string reason)
    {
        reason = string.Empty;
        return arc.Shape switch
        {
            HitShape.ForwardCapsule => PassesForwardCapsuleGate(geom.ToLocal, forward, arc, out reason),
            HitShape.ForwardBox => PassesForwardBoxGate(geom.ToLocal, forward, arc, out reason),
            HitShape.Circle => PassesCircleGate(geom.ToLocal, arc, out reason),
            _ => PassesConeGate(geom.ToLocal, forward, arc, out reason),
        };
    }

    /// <summary>
    /// Owner-centered circle AoE (Reaper TendonSwing). Horizontal distance from caster only —
    /// facing / bodyAngle is ignored. Prefab offset.y is vertical and does not affect the gate.
    /// </summary>
    private static bool PassesCircleGate(Vector3 toLocal, HitArc arc, out string reason)
    {
        reason = string.Empty;
        var targetRadius = Mathf.Max(0f, Config.Extras.AutoCounterTargetColliderRadius.Value);
        var effectiveRadius = arc.Range + targetRadius;
        var distance = toLocal.magnitude;

        if (arc.Range > 0f && distance > effectiveRadius)
        {
            reason = $"out of circle ({distance:F1}m > {effectiveRadius:F1}m, raw={arc.Range:F1}+pad={targetRadius:F1})";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Cone gate (Slasher Secondary = 120° full / 60° half — <b>not</b> 180°).
    /// Distance uses the dash-lookahead vertex (<c>caster + EffectiveForward</c>) so range
    /// matches post-dash reach. Angle uses the caster→local body bearing (not the angle from
    /// the offset vertex): projecting the vertex forward artificially inflates side angles
    /// (e.g. bodyAngle 38° → "80° > 60°") and misses real side-of-cone hits until late dash decay.
    /// </summary>
    private static bool PassesConeGate(Vector3 toLocal, Vector3 forward, HitArc arc, out string reason)
    {
        reason = string.Empty;
        var targetRadius = Mathf.Max(0f, Config.Extras.AutoCounterTargetColliderRadius.Value);

        // Project the cone vertex forward by the effective anchor (constant offset + remaining dash).
        var effectiveToLocal = toLocal;
        var anchor = arc.EffectiveForward;
        if (anchor > 0f && forward.sqrMagnitude > 1e-6f)
            effectiveToLocal -= forward * anchor;

        var effectiveDistance = effectiveToLocal.magnitude;
        // Half pad on cone tip — full Target Radius over-extended Slasher after the 3.3m nerf.
        var rangePad = targetRadius * 0.5f;
        var effectiveRange = arc.Range + rangePad;

        if (arc.Range > 0f && effectiveDistance > effectiveRange)
        {
            reason = $"out of range ({effectiveDistance:F1}m > {effectiveRange:F1}m, raw={arc.Range:F1}+pad={rangePad:F1})";
            return false;
        }

        // Sitting on top of the (post-dash) cone vertex — no meaningful direction; assume hit.
        if (effectiveDistance < 0.05f) return true;

        var halfAngle = arc.HalfAngleDeg;
        if (halfAngle >= 180f) return true;
        if (halfAngle <= 0f) halfAngle = 1f;

        if (forward.sqrMagnitude < 1e-6f) return true;

        // Body bearing from caster (same as AutoCounterDebug bodyAngle), not vertex-relative.
        var bodyDist = toLocal.magnitude;
        if (bodyDist < 0.05f) return true;
        var cos = Mathf.Clamp(Vector3.Dot(forward, toLocal / bodyDist), -1f, 1f);
        var bodyAngle = Mathf.Acos(cos) * Mathf.Rad2Deg;

        if (bodyAngle > halfAngle)
        {
            reason = $"outside cone ({bodyAngle:F0}° > {halfAngle:F0}°)";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Forward-capsule gate: local must lie within <see cref="HitArc.Range"/> + target pad
    /// of the forward segment, and must not be past the projectile's max travel
    /// (<see cref="HitArc.ForwardLength"/>). Closest-point-only tests used to treat 8.4m
    /// head-on as a hit against an 8m bullet (perp to segment end ≈ 0.4m).
    /// </summary>
    private static bool PassesForwardCapsuleGate(Vector3 toLocal, Vector3 forward, HitArc arc, out string reason)
    {
        reason = string.Empty;

        // No forward direction — we can't define the segment, so don't gate.
        if (forward.sqrMagnitude < 1e-6f) return true;

        var targetRadius = arc.ResolveTargetRadius();
        var effectiveRadius = arc.Range + targetRadius;

        var alongForward = Vector3.Dot(toLocal, forward);
        var segStart = arc.EffectiveForward;
        var segEnd = segStart + arc.ForwardLength;

        // Far/near planes include the hit-circle end-cap (radius + target pad). A projectile
        // whose origin stops at ForwardLength can still clip a target up to ~radius past the
        // tip. Small epsilon absorbs F1-round edge cases (perp=1.0 > radius=1.0 in logs).
        const float CapsuleEpsilon = 0.05f;
        var endPad = effectiveRadius;
        if (alongForward > segEnd + endPad + CapsuleEpsilon)
        {
            reason = $"beyond range (along={alongForward:F1}m > end={segEnd:F1}+pad={endPad:F1})";
            return false;
        }
        if (alongForward < segStart - endPad - CapsuleEpsilon)
        {
            reason = $"behind capsule (along={alongForward:F1}m < start={segStart:F1}-pad={endPad:F1})";
            return false;
        }

        var clampedAlong = Mathf.Clamp(alongForward, segStart, segEnd);
        var closest = forward * clampedAlong;
        var perp = (toLocal - closest).magnitude;

        if (perp > effectiveRadius + CapsuleEpsilon)
        {
            reason = $"outside capsule (perp={perp:F1}m > radius={effectiveRadius:F1}m [raw={arc.Range:F1}+pad={targetRadius:F1}], along={alongForward:F1}m of [{segStart:F1},{segEnd:F1}])";
            return false;
        }

        // Optional absolute half-angle cap (projectile leave-aim). Capsule alone admits a
        // huge angle at short range; e.g. effectiveRadius 0.9m at 3.5m ≈ 15°.
        if (arc.MaxHalfAngleDeg > 0f)
        {
            var dist = toLocal.magnitude;
            var halfAngle = AngleBetween(forward, toLocal, dist);
            if (halfAngle > arc.MaxHalfAngleDeg)
            {
                reason = $"bodyAngle {halfAngle:F0}° > max {arc.MaxHalfAngleDeg:F0}° (dist={dist:F1}m)";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Forward-box gate: oriented box centered at <c>caster + EffectiveForward*forward</c>,
    /// extending <c>±ForwardLength/2</c> along the supplied forward and <c>±Width/2</c>
    /// perpendicular. Both axes use the target-collider pad. A small near-plane slack pulls
    /// the box toward the caster so standing in their face (along &lt; raw near) is not lost to
    /// a one-pixel dash-decay race against the fire deadline.
    /// Y is ignored — geometry already flattened to the horizontal plane.
    /// </summary>
    private static bool PassesForwardBoxGate(Vector3 toLocal, Vector3 forward, HitArc arc, out string reason)
    {
        reason = string.Empty;

        // No forward direction — box orientation undefined, don't gate.
        if (forward.sqrMagnitude < 1e-6f) return true;

        var targetRadius = arc.ResolveTargetRadius();
        // Near slack: close "on toes" stands. Tip slack covers F1 tip stands (~8.7m) once
        // aim/body snaps; aim-lead no longer immediate-fires so tip pad is safe again.
        const float NearPlaneSlack = 1.20f;
        const float TipSlack = 0.40f;

        var alongForward = Vector3.Dot(toLocal, forward);
        var perpVec = toLocal - forward * alongForward;
        var perp = perpVec.magnitude;

        var center = arc.EffectiveForward;
        var halfLength = arc.ForwardLength * 0.5f + targetRadius;
        var halfWidth = arc.Width * 0.5f + targetRadius;
        var boxNear = center - halfLength - NearPlaneSlack;
        if (boxNear < 0f) boxNear = 0f;
        var boxFar = center + halfLength + TipSlack;

        if (alongForward < boxNear || alongForward > boxFar)
        {
            reason = $"outside box along (along={alongForward:F1}m not in [{boxNear:F1},{boxFar:F1}], raw={arc.ForwardLength*0.5f:F1}+pad={targetRadius:F1})";
            return false;
        }
        if (perp > halfWidth)
        {
            reason = $"outside box perp (perp={perp:F1}m > halfWidth={halfWidth:F1}m [raw={arc.Width*0.5f:F1}+pad={targetRadius:F1}], along={alongForward:F1}m)";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Compute the per-frame decay state of the dash lookahead. Two compounding factors:
    ///   1. <em>Hit-time fraction of full dash</em>: when <see cref="HitArc.DashManualDuration"/>
    ///      exceeds the cast windup, the caster only physically covers <c>windup/manualDuration</c>
    ///      of the full dash before the hit fires. Predicting the box's hit-time anchor must
    ///      use that capped distance, not the full <see cref="HitArc.DashDistance"/>.
    ///   2. <em>Per-frame remaining</em>: linear decay through the windup so by the recheck
    ///      at time <c>t</c>, the predicted remaining dash is <c>(1 - t/windup)</c> of the
    ///      hit-time-capped value above. (Real dash uses MovementCurve and isn't strictly
    ///      linear, but linear is a fine first-order approximation.)
    /// Returns an arc with <see cref="HitArc.DashRemaining"/> set to <c>DashDistance * combined</c>.
    /// </summary>
    private static HitArc ScaleDashForCast(HitArc arc, double windup, double elapsed)
    {
        if (arc.DashDistance <= 0f || windup <= 0.0) return arc;

        var hitTimeFraction = arc.DashManualDuration > 0f && (double)arc.DashManualDuration > windup
            ? (float)(windup / arc.DashManualDuration)
            : 1f;

        var remainingFraction = Mathf.Clamp01(1f - (float)(elapsed / windup));
        return arc.WithDashScaled(hitTimeFraction * remainingFraction);
    }

    private const int SlasherCamouflageSecondaryCastHash = 576817260;
    private static HitArc? _liveSlasherArc;
    private static bool _liveSlasherResolved;

    /// <summary>
    /// Pick the right <see cref="HitArc"/> for an observed cast. Slasher Secondary prefers a
    /// one-shot live read from the cast prefab graph (keeps pace with balance patches).
    /// Other skills use <see cref="DefaultHitArcs"/>.
    /// </summary>
    private static bool TryResolveHitArc(PrefabGUID groupGuid, PrefabGUID castGuid, out HitArc arc)
    {
        if (groupGuid.GuidHash == SlasherCamouflageSecondaryHash ||
            castGuid.GuidHash == SlasherCamouflageSecondaryCastHash)
        {
            if (!_liveSlasherResolved)
            {
                _liveSlasherResolved = true;
                // Cast prefab owns MoveDuringCast; walker finds Cone on the spawned hit prefab.
                if (PrefabInspector.TryReadConeHitArc(castGuid, out var radius, out var halfAngle, out var dash, out var dashManual))
                {
                    _liveSlasherArc = HitArc.MakeCone(radius, halfAngle, forwardOffset: 0f, dash, dashManual);
                    Plugin.Logger.LogInfo(
                        $"AutoCounter: live Slasher Secondary HitArc range={radius:F2}m " +
                        $"halfAngle={halfAngle:F0}° dash={dash:F2}m dashManual={dashManual:F2}s");
                }
            }

            if (_liveSlasherArc.HasValue)
            {
                arc = _liveSlasherArc.Value;
                return true;
            }
        }

        if (DefaultHitArcs.TryGetValue(groupGuid.GuidHash, out arc)) return true;
        if (DefaultHitArcs.TryGetValue(castGuid.GuidHash, out arc)) return true;
        WarnMissingArcOnce(groupGuid, castGuid);
        return false;
    }

    /// <summary>
    /// One-shot warning when a trigger hash has no <see cref="DefaultHitArcs"/> entry. We
    /// only warn the first time per session per hash so the log doesn't fill up if the user
    /// added a new hash and forgot the matching arc.
    /// </summary>
    private static readonly HashSet<int> WarnedMissingArcs = new();
    private static void WarnMissingArcOnce(PrefabGUID groupGuid, PrefabGUID castGuid)
    {
        if (!WarnedMissingArcs.Add(groupGuid.GuidHash)) return;
        Plugin.Logger.LogWarning(
            $"AutoCounter: trigger hash group={groupGuid.GuidHash} cast={castGuid.GuidHash} " +
            $"('{VWorld.PrefabLookupMap.GetName(groupGuid)}') has no DefaultHitArcs entry — " +
            $"counter will not fire for this ability. Add a HitArc.MakeForwardBox/Cone/Capsule " +
            $"entry in AutoCounter.cs to enable counters for it.");
    }

    /// <summary>
    /// Compute horizontal-plane geometry for the caster→local pair: body forward
    /// (<see cref="LocalToWorld"/>) plus optional cursor aim from <see cref="EntityAimData"/>.
    /// </summary>
    private static HitGeometry ComputeHitGeometry(Entity caster, Entity local)
    {
        if (!caster.TryGetComponent<LocalToWorld>(out var casterL2W) ||
            !local.TryGetComponent<LocalToWorld>(out var localL2W))
            return HitGeometry.Invalid();

        var casterPos = (Vector3)casterL2W.Position;
        var localPos = (Vector3)localL2W.Position;
        var toLocal = localPos - casterPos;
        toLocal.y = 0f;
        var distance = toLocal.magnitude;

        // LocalToWorld stores basis vectors directly; column 2 (Value.c2) is forward.
        var f = casterL2W.Value.c2.xyz;
        var bodyForward = new Vector3(f.x, 0f, f.z);
        if (bodyForward.sqrMagnitude > 1e-6f) bodyForward.Normalize();
        else bodyForward = Vector3.zero;

        var bodyAngle = AngleBetween(bodyForward, toLocal, distance);

        var hasAim = false;
        var aimForward = Vector3.zero;
        var aimAngle = 0f;
        if (caster.TryGetComponent<EntityAimData>(out var aimData))
        {
            var aimPos = (Vector3)aimData.AimPosition;
            var aimDir = aimPos - casterPos;
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude > 1e-4f)
            {
                aimForward = aimDir.normalized;
                aimAngle = AngleBetween(aimForward, toLocal, distance);
                hasAim = true;
            }
        }

        var hasLead = TryReadLeadForward(caster, out var leadForward, out var leadSource);
        var leadAngle = hasLead ? AngleBetween(leadForward, toLocal, distance) : 0f;

        return new HitGeometry(true, toLocal, bodyForward, distance, bodyAngle, hasAim, aimForward, aimAngle,
            hasLead, leadForward, leadAngle, leadSource);
    }

    /// <summary>
    /// Read the caster's rotation goal as a direction vector. <c>ModifyRotationDuringCast</c>
    /// turns the body toward this at 1500°/s, so it tells us where the hit box will point
    /// while the body is still mid-turn — the flick information the body itself only reveals
    /// too late. Prefer the ability-forced direction, then the aim, then the current target.
    /// </summary>
    private static bool TryReadLeadForward(Entity caster, out Vector3 forward, out string source)
    {
        if (caster.TryGetComponent<TargetDirection>(out var target))
        {
            if (TryFlattenDirection(target.AimDirection, out forward))
            {
                source = "tgtAim";
                return true;
            }

            if (TryFlattenDirection(target.Direction, out forward))
            {
                source = "tgtDir";
                return true;
            }
        }

        if (caster.TryGetComponent<EntityInput>(out var input) &&
            TryFlattenDirection(input.AimDirection, out forward))
        {
            source = "input";
            return true;
        }

        forward = Vector3.zero;
        source = string.Empty;
        return false;
    }

    private static bool TryFlattenDirection(float3 raw, out Vector3 forward)
    {
        forward = new Vector3(raw.x, 0f, raw.z);
        if (forward.sqrMagnitude < 1e-4f)
        {
            forward = Vector3.zero;
            return false;
        }

        forward.Normalize();
        return true;
    }

    /// <summary>
    /// Horizontal-plane angle in degrees between <paramref name="forward"/> (unit) and
    /// <paramref name="toLocal"/>. Returns 0 for degenerate inputs (zero forward or local
    /// sitting on top of caster) since there's no meaningful angle to report.
    /// </summary>
    private static float AngleBetween(Vector3 forward, Vector3 toLocal, float distance)
    {
        if (distance < 0.05f || forward.sqrMagnitude < 1e-6f) return 0f;
        var cos = Mathf.Clamp(Vector3.Dot(forward, toLocal / distance), -1f, 1f);
        return Mathf.Acos(cos) * Mathf.Rad2Deg;
    }

    // ===== Windup cache =====================================================

    /// <summary>
    /// Hardcoded sanity cap for the pending-recheck window when a cast prefab has no
    /// readable <see cref="AbilityCastTimeData"/>. Most casts we'd want to counter have one,
    /// so this is just a safety net to keep <see cref="Pending"/> from accumulating entries
    /// with no natural deadline. 1 second is well past any plausible melee windup.
    /// </summary>
    private const float WindupFallbackSeconds = 1.0f;

    /// <summary>
    /// Per-cast windup window, taken directly from the prefab's
    /// <see cref="AbilityCastTimeData.MaxCastTime"/>. No user slider — the prefab is
    /// authoritative for how long the cast actually takes. Returns the fallback when the
    /// prefab can't be resolved or has no cast-time component.
    /// </summary>
    private static float ResolveWindupSeconds(PrefabGUID castGuid)
    {
        if (TryGetCastMaxCastTime(castGuid, out var perAbility) && perAbility > 0f) return perAbility;
        return WindupFallbackSeconds;
    }

    private static bool TryGetCastMaxCastTime(PrefabGUID castGuid, out float seconds)
    {
        if (CastWindupCache.TryGetValue(castGuid.GuidHash, out seconds)) return seconds > 0f;

        seconds = 0f;
        var prefabMap = VWorld.PrefabLookupMap;
        if (!prefabMap.TryGetValue(castGuid, out var prefab) || prefab == Entity.Null || !prefab.Exists())
        {
            // Negative-cache the miss so we don't keep doing the lookup. Sentinel 0 = "no data".
            CastWindupCache[castGuid.GuidHash] = 0f;
            return false;
        }

        if (prefab.TryGetComponent<AbilityCastTimeData>(out var ct))
            seconds = ct.MaxCastTime.Value;

        CastWindupCache[castGuid.GuidHash] = seconds;
        return seconds > 0f;
    }

    // ===== Debug log ========================================================

    /// <summary>
    /// One-line tuning log per cast, prefixed <c>AutoCounterDebug</c> for easy grepping.
    /// Casts farther than <c>AutoCounterDebugLogRadius</c> are dropped to keep the log
    /// readable in busy fights — the radius gate is debug-only and never affects gating.
    /// </summary>
    private static void MaybeLogDebug(Entity caster, PrefabGUID groupGuid, PrefabGUID castGuid, HitGeometry geom, bool inTriggerList)
    {
        var debugRadius = Config.Extras.AutoCounterDebugLogRadius.Value;
        if (geom.Valid && debugRadius > 0f && geom.Distance > debugRadius) return;

        var abilityName = VWorld.PrefabLookupMap.GetName(castGuid);
        var groupName = VWorld.PrefabLookupMap.GetName(groupGuid);
        var casterName = caster.TryGetComponent<PlayerCharacter>(out var pc)
            ? pc.Name.ToString()
            : caster.GetName();

        var distStr = geom.Valid ? $"{geom.Distance:F1}m" : "?";
        var bodyAngleStr = geom.Valid ? $"{geom.BodyAngle:F0}°" : "?";

        Plugin.Logger.LogInfo(
            $"AutoCounterDebug caster='{casterName}' ability='{abilityName}' group='{groupName}' " +
            $"groupHash={groupGuid.GuidHash} castHash={castGuid.GuidHash} " +
            $"dist={distStr} bodyAngle={bodyAngleStr} inTriggerList={inTriggerList}");
    }

    // ===== Trigger-hash config refresh ======================================

    private static void RefreshTriggerHashes()
    {
        var raw = Config.Extras.AutoCounterTriggerHashes.Value;
        var slasher = Config.Extras.AutoCounterSlasherE.Value;
        var slasherQ = Config.Extras.AutoCounterSlasherQ.Value;
        var whipQ = Config.Extras.AutoCounterWhipQ.Value;
        var swordE = Config.Extras.AutoCounterSwordE.Value;
        var spear = Config.Extras.AutoCounterSpearQ.Value;
        var twinbladeE = Config.Extras.AutoCounterTwinbladeE.Value;
        var reaperQ = Config.Extras.AutoCounterReaperQ.Value;
        var pistolPrimary = Config.Extras.AutoCounterPistolPrimary.Value;
        var pistolE = Config.Extras.AutoCounterPistolE.Value;
        var crossbowSnapshot = Config.Extras.AutoCounterCrossbowSnapshot.Value;
        var crossbowPrimary = Config.Extras.AutoCounterCrossbowPrimary.Value;
        var cacheKey = $"{raw}|{slasher}|{slasherQ}|{whipQ}|{swordE}|{spear}|{twinbladeE}|{reaperQ}|{pistolPrimary}|{pistolE}|{crossbowSnapshot}|{crossbowPrimary}";
        if (cacheKey == _lastConfiguredHashes) return;
        _lastConfiguredHashes = cacheKey;

        TriggerHashes.Clear();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var tokens = raw.Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (int.TryParse(tokens[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash))
                    TriggerHashes.Add(hash);
            }
        }

        if (slasher) TriggerHashes.Add(SlasherCamouflageSecondaryHash);
        else TriggerHashes.Remove(SlasherCamouflageSecondaryHash);

        if (slasherQ)
        {
            TriggerHashes.Add(SlasherElusiveStrikeGroupHash);
            TriggerHashes.Add(SlasherElusiveStrikeCastHash);
        }
        else
        {
            TriggerHashes.Remove(SlasherElusiveStrikeGroupHash);
            TriggerHashes.Remove(SlasherElusiveStrikeCastHash);
        }

        if (whipQ)
        {
            TriggerHashes.Add(WhipDashGroupHash);
            TriggerHashes.Add(WhipDashCastHash);
        }
        else
        {
            TriggerHashes.Remove(WhipDashGroupHash);
            TriggerHashes.Remove(WhipDashCastHash);
        }

        if (swordE)
        {
            TriggerHashes.Add(SwordShockwaveGroupHash);
            TriggerHashes.Add(SwordShockwaveCastHash);
        }
        else
        {
            TriggerHashes.Remove(SwordShockwaveGroupHash);
            TriggerHashes.Remove(SwordShockwaveCastHash);
        }

        if (spear) TriggerHashes.Add(SpearAThousandSpearsStabHash);
        else TriggerHashes.Remove(SpearAThousandSpearsStabHash);

        if (twinbladeE)
        {
            TriggerHashes.Add(TwinbladeSweepingStrikeGroupHash);
            TriggerHashes.Add(TwinbladeSweepingStrikeCastHash);
        }
        else
        {
            TriggerHashes.Remove(TwinbladeSweepingStrikeGroupHash);
            TriggerHashes.Remove(TwinbladeSweepingStrikeCastHash);
        }

        if (reaperQ)
        {
            TriggerHashes.Add(ReaperTendonSwingTwistGroupHash);
            TriggerHashes.Add(ReaperTendonSwingTwistCastHash);
        }
        else
        {
            TriggerHashes.Remove(ReaperTendonSwingTwistGroupHash);
            TriggerHashes.Remove(ReaperTendonSwingTwistCastHash);
        }

        if (pistolPrimary)
        {
            TriggerHashes.Add(PistolPrimaryGroupHash);
            TriggerHashes.Add(PistolPrimaryCast01Hash);
            TriggerHashes.Add(PistolPrimaryCast02Hash);
            TriggerHashes.Add(PistolPrimaryCast03Hash);
        }
        else
        {
            TriggerHashes.Remove(PistolPrimaryGroupHash);
            TriggerHashes.Remove(PistolPrimaryCast01Hash);
            TriggerHashes.Remove(PistolPrimaryCast02Hash);
            TriggerHashes.Remove(PistolPrimaryCast03Hash);
        }

        if (pistolE)
        {
            TriggerHashes.Add(PistolExplosiveShotShotGroupHash);
            TriggerHashes.Add(PistolExplosiveShotShotCastHash);
            TriggerHashes.Add(PistolExplosiveShotRecastGroupHash);
            TriggerHashes.Add(PistolExplosiveShotRecastCastHash);
        }
        else
        {
            TriggerHashes.Remove(PistolExplosiveShotShotGroupHash);
            TriggerHashes.Remove(PistolExplosiveShotShotCastHash);
            TriggerHashes.Remove(PistolExplosiveShotRecastGroupHash);
            TriggerHashes.Remove(PistolExplosiveShotRecastCastHash);
        }

        if (crossbowSnapshot)
        {
            TriggerHashes.Add(CrossbowSnapshotGroupHash);
            TriggerHashes.Add(CrossbowSnapshotCastHash);
        }
        else
        {
            TriggerHashes.Remove(CrossbowSnapshotGroupHash);
            TriggerHashes.Remove(CrossbowSnapshotCastHash);
        }

        if (crossbowPrimary)
        {
            TriggerHashes.Add(CrossbowPrimaryGroupHash);
            TriggerHashes.Add(CrossbowPrimaryCastHash);
        }
        else
        {
            TriggerHashes.Remove(CrossbowPrimaryGroupHash);
            TriggerHashes.Remove(CrossbowPrimaryCastHash);
        }
    }
}

/// <summary>
/// Per-frame driver for <see cref="AutoCounter.Tick"/>. Lives as a MonoBehaviour because
/// the release timing has to happen on the Unity main thread (Time.realtimeSinceStartupAsDouble
/// + InputSimulator's Win32 SendInput call), and there's no other natural per-frame hook
/// in this plugin we could piggyback on without inflating its responsibilities.
/// </summary>
public class AutoCounterController : MonoBehaviour
{
    private void Update() => AutoCounter.Tick();
}
