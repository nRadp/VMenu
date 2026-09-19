using System.Collections.Generic;
using System.Text;
using Il2CppSystem.Reflection;
using ExtrasensoryPerception.API;
using ProjectM;
using ProjectM.Network;
using ProjectM.Physics;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace ExtrasensoryPerception.Utils;

/// <summary>
/// One-shot prefab dumper used by Auto Counter's debug log to extract per-ability hit-volume
/// dimensions. The IL2CPP dump only carries C# class definitions, not the prefab numeric data
/// (cone radii, box sizes, etc.) — those live on prefab entities loaded at runtime. This
/// walker prints every interesting component we know how to read off a cast prefab and the
/// prefabs it spawns, so the user can read off <c>Cone_Radius</c> / <c>Cone_Angle</c> /
/// <c>Box_Length</c> values from the log and either hardcode them or feed them into a future
/// per-ability table.
///
/// Walk strategy (capped at <see cref="MaxDepth"/> via a per-dump visited set so we never
/// chase a cycle):
///   - depth 0 (cast prefab): dump own data, then fan out via every known link
///     (<see cref="AbilitySpawnPrefabOnCast"/>, <see cref="LinkedEntityGroup"/>,
///     <see cref="SpawnPrefabOnGameplayEvent"/>).
///   - depth 1 (cast spawn target, e.g. a Channel buff): dump own data, then continue down
///     <em>only</em> through <see cref="SpawnPrefabOnGameplayEvent"/> — that's the chain that
///     typically leads from a channel/state buff to its actual hit prefab. Channels rarely
///     hit directly: the cast prefab spawns a buff, the buff spawns the hit prefab on a
///     gameplay event (cast end, channel tick, etc.). Without this extra hop we'd never see
///     where the real <see cref="HitColliderCast"/> lives for those abilities.
///   - depth 2 (terminal hit prefab): dump own data and stop. Most hit volumes live here
///     (cone/circle/box on the spawned hit entity).
/// </summary>
internal static class PrefabInspector
{
    /// <summary>Per-session dedupe set so we only dump each cast hash once per game.</summary>
    private static readonly HashSet<int> _dumpedCastHashes = new();

    /// <summary>Per-session dedupe set for live-entity dumps, keyed by dump label.</summary>
    private static readonly HashSet<string> _dumpedLiveLabels = new();

    /// <summary>
    /// Maximum walker depth from the cast prefab. 0=cast, 1=cast's spawn target (often a
    /// channel buff), 2=that target's spawn (often the actual hit prefab). Going further
    /// risks mapping out the entire ability graph (status buffs, follow-up effects, etc.)
    /// without adding new info for hit-volume tuning.
    /// </summary>
    private const int MaxDepth = 2;

    internal static void Reset()
    {
        _dumpedCastHashes.Clear();
        _dumpedLiveLabels.Clear();
    }

    /// <summary>
    /// Dump every component on a <em>live</em> (non-prefab) entity, once per label per
    /// session. Prefab dumps only show authoring data; the runtime caster/cast entities are
    /// where replicated per-cast state lives. We are hunting for a replicated rotation or
    /// aim target: <c>ModifyRotationDuringCast</c> turns the body toward an aim direction
    /// the server knows, and if any component carries that target we can read where the
    /// body will end up instead of extrapolating its turn rate.
    ///
    /// Candidate component types also get their field/property names printed, so a reader
    /// can be written against them without a second round trip.
    /// </summary>
    internal static void DumpLiveEntityOnce(Entity entity, string label)
    {
        if (entity == Entity.Null || !entity.Exists()) return;
        if (!_dumpedLiveLabels.Add(label)) return;

        NativeArray<ComponentType> types = default;
        try
        {
            types = entity.GetAllComponents();
            var names = new List<string>(types.Length);
            var candidateTypes = new List<Il2CppSystem.Type>();
            for (var i = 0; i < types.Length; i++)
            {
                var managed = types[i].GetManagedType();
                var n = managed?.Name;
                if (string.IsNullOrEmpty(n)) continue;
                names.Add(n!);
                if (managed != null && IsRotationCandidate(n!)) candidateTypes.Add(managed);
            }
            names.Sort();

            Plugin.Logger.LogInfo($"LiveDump {label} entity={entity.Index}:{entity.Version} components={names.Count}");

            const int PerLine = 8;
            for (var i = 0; i < names.Count; i += PerLine)
            {
                var take = System.Math.Min(PerLine, names.Count - i);
                Plugin.Logger.LogInfo($"LiveDump {label} [{i:D3}] {string.Join(", ", names.GetRange(i, take))}");
            }

            foreach (var t in candidateTypes)
                Plugin.Logger.LogInfo($"LiveDump {label} fields {t.Name} {{ {DescribeMembers(t)} }}");
        }
        finally
        {
            if (types.IsCreated) types.Dispose();
        }
    }

    private static bool IsRotationCandidate(string name) =>
        name.Contains("Aim") || name.Contains("Rotat") || name.Contains("Direction") ||
        name.Contains("Input") || name.Contains("Target") || name.Contains("Heading") ||
        name.Contains("Facing") || name.Contains("Cast");

    /// <summary>Field and property names of an interop component type, for writing readers.</summary>
    private static string DescribeMembers(Il2CppSystem.Type type)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
        var parts = new List<string>();
        try
        {
            var fields = type.GetFields(Flags);
            for (var i = 0; i < fields.Length; i++)
                parts.Add($"{fields[i].Name}:{fields[i].FieldType.Name}");

            var props = type.GetProperties(Flags);
            for (var i = 0; i < props.Length; i++)
                parts.Add($"{props[i].Name}:{props[i].PropertyType.Name}");
        }
        catch (System.Exception e)
        {
            parts.Add($"<reflection failed: {e.Message}>");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Walk cast → spawn → hit (same graph as the dump) and read the first Cone
    /// <see cref="HitColliderCast"/> plus the cast's <see cref="MoveDuringCastData"/>.
    /// Used by Auto Counter to keep Slasher Secondary (etc.) in sync after balance patches.
    /// </summary>
    internal static bool TryReadConeHitArc(PrefabGUID castGuid,
        out float coneRadius, out float coneHalfAngleDeg, out float dashDistance, out float dashManualDuration)
    {
        coneRadius = 0f;
        coneHalfAngleDeg = 0f;
        dashDistance = 0f;
        dashManualDuration = 0f;

        var prefabMap = VWorld.PrefabLookupMap;
        if (!prefabMap.TryGetValue(castGuid, out var castPrefab) ||
            castPrefab == Entity.Null || !castPrefab.Exists())
            return false;

        if (castPrefab.TryGetComponent<MoveDuringCastData>(out var move))
        {
            dashDistance = move.ForceMovementLength;
            if (move.UseManualDuration) dashManualDuration = move.ManualDuration;
        }

        var visited = new HashSet<int>();
        if (TryFindCone(castPrefab, depth: 0, visited, out coneRadius, out var fullAngle))
        {
            coneHalfAngleDeg = fullAngle * 0.5f;
            return coneRadius > 0f && coneHalfAngleDeg > 0f;
        }

        return false;
    }

    private static bool TryFindCone(Entity prefab, int depth, HashSet<int> visited,
        out float radius, out float fullAngleDeg)
    {
        radius = 0f;
        fullAngleDeg = 0f;
        var guid = prefab.GetPrefabGuid();
        if (!visited.Add(guid.GuidHash)) return false;

        if (TryReadConeOnEntity(prefab, out radius, out fullAngleDeg))
            return true;

        if (depth >= MaxDepth) return false;

        var prefabMap = VWorld.PrefabLookupMap;

        if (prefab.HasBuffer<SpawnPrefabOnGameplayEvent>())
        {
            var buf = prefab.ReadBuffer<SpawnPrefabOnGameplayEvent>();
            for (var i = 0; i < buf.Length; i++)
            {
                if (TryResolveAndFindCone(buf[i].SpawnPrefab, depth, visited, prefabMap, out radius, out fullAngleDeg))
                    return true;
            }
        }
        else if (prefab.TryGetComponent<SpawnPrefabOnGameplayEvent>(out var sp) &&
                 TryResolveAndFindCone(sp.SpawnPrefab, depth, visited, prefabMap, out radius, out fullAngleDeg))
            return true;

        if (depth == 0)
        {
            if (prefab.HasBuffer<AbilitySpawnPrefabOnCast>())
            {
                var buf = prefab.ReadBuffer<AbilitySpawnPrefabOnCast>();
                for (var i = 0; i < buf.Length; i++)
                {
                    if (TryResolveAndFindCone(buf[i].SpawnPrefab, depth, visited, prefabMap, out radius, out fullAngleDeg))
                        return true;
                }
            }
            else if (prefab.TryGetComponent<AbilitySpawnPrefabOnCast>(out var asp) &&
                     TryResolveAndFindCone(asp.SpawnPrefab, depth, visited, prefabMap, out radius, out fullAngleDeg))
                return true;
        }

        return false;
    }

    private static bool TryResolveAndFindCone(PrefabGUID guid, int parentDepth, HashSet<int> visited,
        PrefabLookupMap prefabMap, out float radius, out float fullAngleDeg)
    {
        radius = 0f;
        fullAngleDeg = 0f;
        if (guid.GuidHash == 0) return false;
        if (!prefabMap.TryGetValue(guid, out var spawned) || spawned == Entity.Null || !spawned.Exists())
            return false;
        return TryFindCone(spawned, parentDepth + 1, visited, out radius, out fullAngleDeg);
    }

    private static bool TryReadConeOnEntity(Entity prefab, out float radius, out float fullAngleDeg)
    {
        radius = 0f;
        fullAngleDeg = 0f;

        if (prefab.HasBuffer<HitColliderCast>())
        {
            var buf = prefab.ReadBuffer<HitColliderCast>();
            for (var i = 0; i < buf.Length; i++)
            {
                var s = buf[i].Shape;
                if (s.Type != TriggerShapeType.Cone) continue;
                radius = s.Cone_Radius;
                fullAngleDeg = s.Cone_Angle;
                if (radius > 0f) return true;
            }
        }

        if (prefab.TryGetComponent<HitColliderCast>(out var hc) && hc.Shape.Type == TriggerShapeType.Cone)
        {
            radius = hc.Shape.Cone_Radius;
            fullAngleDeg = hc.Shape.Cone_Angle;
            return radius > 0f;
        }

        return false;
    }

    /// <summary>
    /// Dump the prefab graph for the given ability cast guid, deduping per-session. Safe to
    /// call from hot paths — the first call for a hash takes a few EM lookups, subsequent
    /// calls are a hashset hit.
    /// </summary>
    internal static void DumpAbilityCastOnce(PrefabGUID abilityGroupGuid, PrefabGUID abilityCastGuid)
    {
        if (!_dumpedCastHashes.Add(abilityCastGuid.GuidHash)) return;

        var sb = new StringBuilder();
        sb.Append("PrefabDump cast='").Append(VWorld.PrefabLookupMap.GetName(abilityCastGuid))
          .Append("' castHash=").Append(abilityCastGuid.GuidHash)
          .Append(" group='").Append(VWorld.PrefabLookupMap.GetName(abilityGroupGuid))
          .Append("' groupHash=").Append(abilityGroupGuid.GuidHash);

        var prefabMap = VWorld.PrefabLookupMap;
        if (!prefabMap.TryGetValue(abilityCastGuid, out var castPrefab) || castPrefab == Entity.Null || !castPrefab.Exists())
        {
            sb.Append(" -- cast prefab not resolved");
            Plugin.Logger.LogInfo(sb.ToString());
            return;
        }

        Plugin.Logger.LogInfo(sb.ToString());
        var visited = new HashSet<int>();
        DumpPrefabEntity(castPrefab, depth: 0, label: "cast", visited);
    }

    /// <summary>
    /// Print the interesting bits of one prefab entity. <paramref name="depth"/> caps recursion
    /// (see <see cref="MaxDepth"/>) and <paramref name="visited"/> dedupes prefab GUIDs across
    /// the whole walk so a diamond fan-out doesn't print the same target twice. At depth 0 we
    /// follow every fan-out link; at deeper levels we follow only <see cref="SpawnPrefabOnGameplayEvent"/>
    /// which is the channel→hit chain we care about for hit-volume tuning.
    /// </summary>
    private static void DumpPrefabEntity(Entity prefab, int depth, string label, HashSet<int> visited)
    {
        var guid = prefab.GetPrefabGuid();
        var indent = new string(' ', 2 + depth * 2);
        if (!visited.Add(guid.GuidHash))
        {
            Plugin.Logger.LogInfo($"{indent}[{label}] guidHash={guid.GuidHash} (already dumped, skipping)");
            return;
        }

        var name = prefab.GetName();
        Plugin.Logger.LogInfo($"{indent}[{label}] entity={prefab.Index}:{prefab.Version} name='{name}' guidHash={guid.GuidHash}");

        var bodyIndent = indent + "  ";
        DumpHitColliders(prefab, bodyIndent);
        DumpAimPreview(prefab, bodyIndent);
        DumpCastTime(prefab, bodyIndent);
        DumpMoveDuringCast(prefab, bodyIndent);
        DumpRotateDuringCast(prefab, bodyIndent);
        DumpModifyRotationDuringCast(prefab, bodyIndent);
        DumpProjectile(prefab, bodyIndent);
        DumpGetTranslationOnSpawn(prefab, bodyIndent);
        DumpOffsetTranslationOnSpawn(prefab, bodyIndent);
        DumpRotationSourceTags(prefab, bodyIndent);
        DumpComponentTypeSummary(prefab, bodyIndent);

        if (depth >= MaxDepth) return;

        // SpawnPrefabOnGameplayEvent is the channel→hit chain — always followed within depth.
        DumpSpawnedPrefabs(prefab, bodyIndent, depth, visited);

        // Other fan-outs only at depth 0: they almost never lead to new hit volumes deeper,
        // and following them would explode the log with linked status-buff prefabs etc.
        if (depth == 0)
        {
            DumpAbilitySpawnPrefabOnCast(prefab, bodyIndent, depth, visited);
            DumpLinkedChildren(prefab, bodyIndent, visited);
        }
    }

    /// <summary>
    /// <see cref="AbilityCastAimPreview"/> stores the cone/quad dimensions used for the on‑
    /// screen aim hint, which for most directional melee skills (spear stab, slasher dash)
    /// matches the server-side hit shape closely enough to drive our gate. Values printed
    /// here are the most useful direct read for tuning per-ability range/cone.
    /// </summary>
    private static void DumpAimPreview(Entity prefab, string indent)
    {
        if (!prefab.TryGetComponent<AbilityCastAimPreview>(out var ap)) return;
        Plugin.Logger.LogInfo(
            $"{indent}AbilityCastAimPreview radius={ap.Radius:F2} length={ap.Length:F2} " +
            $"coneAngle={ap.ConeAngle:F1}° quad=({ap.QuadSize.x:F2}x{ap.QuadSize.y:F2}) " +
            $"previewPrefab={ap.AimPreviewPrefab.GuidHash}");
    }

    /// <summary>
    /// Walk every <see cref="AbilitySpawnPrefabOnCast"/> entry on the cast prefab and recurse
    /// one level into the spawn target. Cast prefabs frequently hold the spawn metadata as a
    /// buffer (the authoring component is <c>AllowMultipleComponents</c>); we try the buffer
    /// first and fall back to the single-component shape for safety.
    /// </summary>
    private static void DumpAbilitySpawnPrefabOnCast(Entity prefab, string indent, int depth, HashSet<int> visited)
    {
        var prefabMap = VWorld.PrefabLookupMap;

        if (prefab.HasBuffer<AbilitySpawnPrefabOnCast>())
        {
            var buf = prefab.ReadBuffer<AbilitySpawnPrefabOnCast>();
            for (var i = 0; i < buf.Length; i++)
                ResolveAndDump(buf[i].SpawnPrefab, indent, $"AbilitySpawnPrefabOnCast[{i}]", prefabMap, depth, visited);
        }
        else if (prefab.TryGetComponent<AbilitySpawnPrefabOnCast>(out var sp))
        {
            ResolveAndDump(sp.SpawnPrefab, indent, "AbilitySpawnPrefabOnCast", prefabMap, depth, visited);
        }
    }

    /// <summary>
    /// Print every <see cref="HitColliderCast"/> attached to the prefab — buffer first (the
    /// common shape), then a single-component fallback in case the prefab uses the IComponentData
    /// variant directly. <see cref="TriggerShape"/> readers resolve the right field per shape
    /// (cone vs box vs circle, etc.) so the log shows the human-meaningful dimension names.
    /// </summary>
    private static void DumpHitColliders(Entity prefab, string indent)
    {
        if (prefab.HasBuffer<HitColliderCast>())
        {
            var buf = prefab.ReadBuffer<HitColliderCast>();
            for (var i = 0; i < buf.Length; i++) LogShape(indent, $"HitColliderCast[{i}]", buf[i]);
        }
        else if (prefab.TryGetComponent<HitColliderCast>(out var hc))
        {
            LogShape(indent, "HitColliderCast", hc);
        }
    }

    private static void LogShape(string indent, string label, HitColliderCast hc)
    {
        var s = hc.Shape;
        string dims = s.Type switch
        {
            TriggerShapeType.Cone => $"Cone radius={s.Cone_Radius:F2} angle={s.Cone_Angle:F1}\u00b0",
            TriggerShapeType.Box => $"Box w={s.Box_Width:F2} h={s.Box_Height:F2} l={s.Box_Length:F2}",
            TriggerShapeType.Circle => $"Circle radius={s.Circle_Radius:F2}",
            TriggerShapeType.Donut => $"Donut outer={s.Donut_OuterRadius:F2} inner={s.Donut_InnerRadius:F2}",
            TriggerShapeType.Cylinder => $"Cylinder radius={s.Cylinder_Radius:F2} h={s.Cylinder_Height:F2}",
            _ => $"Shape={s.Type}"
        };
        Plugin.Logger.LogInfo(
            $"{indent}{label} {dims} offset=({hc.Offset.x:F2},{hc.Offset.y:F2},{hc.Offset.z:F2}) " +
            $"trigger={hc.CollisionCheckType} primary={hc.PrimaryTargets_Count}");
    }

    private static void DumpCastTime(Entity prefab, string indent)
    {
        if (!prefab.TryGetComponent<AbilityCastTimeData>(out var ct)) return;
        Plugin.Logger.LogInfo($"{indent}AbilityCastTimeData maxCast={ct.MaxCastTime.Value:F2}s postCast={ct.PostCastTime.Value:F2}s total={ct.TotalCastTime:F2}s");
    }

    /// <summary>
    /// <see cref="MoveDuringCastData.ForceMovementLength"/> is the headline number for any
    /// cast that displaces the caster (slasher dash, batform charge, etc.). Combine with
    /// the cast prefab's <see cref="HitColliderCast"/> radius to get an effective reach —
    /// e.g. slasher Camouflage Secondary's true range = ForceMovementLength + cone radius.
    /// <see cref="ForceMoveDuringCastType"/> tells us whether the dash uses aim direction,
    /// cast direction, or input — useful when deciding whether the caster's facing at cast
    /// start (what we gate on) actually predicts the displacement direction.
    /// </summary>
    private static void DumpMoveDuringCast(Entity prefab, string indent)
    {
        if (!prefab.TryGetComponent<MoveDuringCastData>(out var move)) return;
        Plugin.Logger.LogInfo(
            $"{indent}MoveDuringCastData forceMovementLength={move.ForceMovementLength:F2}m " +
            $"manualDuration={move.ManualDuration:F2}s useManualDuration={move.UseManualDuration} " +
            $"forceMoveType={move.ForceMoveType} moveType={move.MoveType} " +
            $"ignoreMovementImpair={move.IgnoreMovementImpair}");
    }

    /// <summary>
    /// <see cref="Projectile.Range"/> is the travel distance for projectile-based abilities
    /// (spear AThousandSpears ball lightning, ranged spells, etc.) — the headline number to
    /// pair with the projectile's <see cref="HitColliderCast"/> radius for a forward-capsule
    /// gate (see <c>AutoCounter.HitArc.MakeForwardCapsule</c>). <c>Speed</c> is logged for
    /// context (range/speed = lifetime in seconds, useful for sanity-checking against the
    /// cast's <c>AbilityCastTimeData</c>).
    /// </summary>
    private static void DumpProjectile(Entity prefab, string indent)
    {
        if (!prefab.TryGetComponent<Projectile>(out var p)) return;
        Plugin.Logger.LogInfo(
            $"{indent}Projectile range={p.Range:F2}m speed={p.Speed:F2}m/s minRange={p.MinRange:F2}m " +
            $"travelToCursor={p.TravelToMouseCursor} cursorOffset={p.TravelToMouseCursorLengthOffset:F2}m " +
            $"overrideLifetime={p.OverrideLifeTime:F2}s delayLifetime={p.DelayLifeTime:F2}s");
    }

    /// <summary>
    /// <see cref="OffsetTranslationOnSpawn.Offset"/> shifts a spawn-time projectile/hit
    /// volume from the caster's transform by a fixed local-space vector. Matters when the
    /// hit "originates" some distance ahead of the caster (e.g. the projectile being spawned
    /// at the tip of the spear rather than the caster's chest) — in those cases the forward-
    /// capsule's <c>ForwardOffset</c> needs to include this on top of any dash distance.
    /// </summary>
    private static void DumpOffsetTranslationOnSpawn(Entity prefab, string indent)
    {
        if (!prefab.TryGetComponent<OffsetTranslationOnSpawn>(out var o)) return;
        Plugin.Logger.LogInfo($"{indent}OffsetTranslationOnSpawn offset=({o.Offset.x:F2},{o.Offset.y:F2},{o.Offset.z:F2})");
    }

    /// <summary>
    /// <see cref="GetTranslationOnSpawn.TranslationSource"/> picks WHERE the spawned hit
    /// volume is anchored in world space. CRITICAL for the hit-arc gate: <c>Owner</c> means
    /// "caster position", but <c>SpellTarget</c> / <c>BuffTarget</c> means "target entity's
    /// position" — in which case the box lands on top of the target regardless of caster
    /// orientation, and gating on caster forward + offset is geometrically wrong.
    /// <c>SnapToGround</c> tells us whether the spawn Y is forced to terrain height.
    /// </summary>
    private static void DumpGetTranslationOnSpawn(Entity prefab, string indent)
    {
        if (!prefab.TryGetComponent<GetTranslationOnSpawn>(out var g)) return;
        Plugin.Logger.LogInfo($"{indent}GetTranslationOnSpawn source={g.TranslationSource} snapToGround={g.SnapToGround}");
    }

    /// <summary>
    /// <see cref="RotateTowardsAimDirectionDuringCastData"/> drives the body rotation snap
    /// during a cast. <c>RotationAngle</c>/<c>MinDegrees</c>/<c>MaxDegrees</c> bound how far
    /// the body rotates; <c>ManualDuration</c> is how long the rotation takes (compare to
    /// <c>AbilityCastTimeData.MaxCastTime</c> to know whether the body is fully snapped by
    /// hit-time). <c>TargetRotationCanChangeDuringCast</c> tells us whether the rotation
    /// target is locked at cast start (false) or follows live aim updates (true).
    /// </summary>
    private static void DumpRotateDuringCast(Entity prefab, string indent)
    {
        if (!prefab.TryGetComponent<RotateTowardsAimDirectionDuringCastData>(out var r)) return;
        Plugin.Logger.LogInfo(
            $"{indent}RotateTowardsAimDirectionDuringCastData rotationAngle={r.RotationAngle:F1}° " +
            $"min={r.MinDegrees:F1}° max={r.MaxDegrees:F1}° manualDuration={r.ManualDuration:F2}s " +
            $"useManualDuration={r.UseManualDuration} clockwise={r.Clockwise} " +
            $"targetCanChangeDuringCast={r.TargetRotationCanChangeDuringCast}");
    }

    /// <summary>
    /// <see cref="ModifyRotationDuringCast"/> wraps two <see cref="ModifyRotation"/> blocks
    /// (active during cast / post-cast). The headline field is
    /// <see cref="ModifyRotation.TargetDirectionType"/>: <c>AimDirection</c> means body
    /// rotates toward the cursor; <c>TowardsSpellTarget</c>/<c>TowardsAbilityTarget</c> means
    /// body rotates toward the locked-on entity (probably the player getting countered, which
    /// would explain why our aim-direction gate keeps disagreeing with body forward).
    /// </summary>
    private static void DumpModifyRotationDuringCast(Entity prefab, string indent)
    {
        if (!prefab.TryGetComponent<ModifyRotationDuringCast>(out var m)) return;
        Plugin.Logger.LogInfo($"{indent}ModifyRotationDuringCast.cast {DescribeModifyRotation(m.CastRotationData)}");
        Plugin.Logger.LogInfo($"{indent}ModifyRotationDuringCast.postCast {DescribeModifyRotation(m.PostCastRotationData)}");
    }

    private static string DescribeModifyRotation(ModifyRotation r) =>
        $"target={r.TargetDirectionType} type={r.Type} value={r.Value:F2} " +
        $"activeTimeline=[{r.ActiveTimeline.MinPadding:F2},{r.ActiveTimeline.MaxPadding:F2}] " +
        $"snapToDirection={r.SnapToDirection} " +
        $"hasOffset={r.OffsetRotation.HasValue} hasPrevTarget={r.PreviousTargetDirection.HasValue}";

    /// <summary>
    /// Two cheap tag/marker checks: whether the spawn pulls rotation directly from the owner
    /// only at spawn time (<see cref="GetOwnerRotationOnlyOnSpawnTag"/>) or every frame
    /// (<see cref="GetOwnerRotation"/>). Combined with the <see cref="GetTranslationOnSpawn"/>
    /// translation source above, this nails down both position and orientation of the hit
    /// volume in world space.
    /// </summary>
    private static void DumpRotationSourceTags(Entity prefab, string indent)
    {
        var hasOnSpawn = prefab.Has<GetOwnerRotationOnlyOnSpawnTag>();
        var hasContinuous = prefab.Has<GetOwnerRotation>();
        if (!hasOnSpawn && !hasContinuous) return;
        Plugin.Logger.LogInfo($"{indent}RotationSource ownerRotationOnSpawnOnly={hasOnSpawn} continuousOwnerRotation={hasContinuous}");
    }

    /// <summary>
    /// Print just the count + a truncated list of component type names, so the log stays
    /// readable but we can spot interesting components we don't yet specifically decode
    /// (e.g. <c>AbilityProjectileSpawn</c>, <c>DashStateBuff</c>, etc.) and add readers later.
    /// </summary>
    private static void DumpComponentTypeSummary(Entity prefab, string indent)
    {
        NativeArray<ComponentType> types = default;
        try
        {
            types = prefab.GetAllComponents();
            var count = types.Length;
            var names = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var n = types[i].GetManagedType()?.Name;
                if (!string.IsNullOrEmpty(n)) names.Add(n!);
            }
            // Filter to the noteworthy substrings — full list is huge and uninteresting.
            var interesting = names.FindAll(n =>
                n.Contains("Hit") || n.Contains("Cast") || n.Contains("Projectile") ||
                n.Contains("Aim") || n.Contains("Dash") || n.Contains("Spawn") ||
                n.Contains("Cone") || n.Contains("AreaOfEffect") || n.Contains("Buff"));
            Plugin.Logger.LogInfo($"{indent}components total={count} interesting=[{string.Join(", ", interesting)}]");
        }
        finally
        {
            if (types.IsCreated) types.Dispose();
        }
    }

    /// <summary>
    /// Walk <see cref="SpawnPrefabOnGameplayEvent"/> entries (buffer + single-component
    /// shapes) and resolve each spawn target via <see cref="PrefabLookupMap"/>, dumping the
    /// resolved prefab one level deep. This is the most common path to actual hit volumes
    /// for melee skills — the cast prefab itself is just a state holder, the hit data lives
    /// on the spawn target.
    /// </summary>
    private static void DumpSpawnedPrefabs(Entity prefab, string indent, int depth, HashSet<int> visited)
    {
        var prefabMap = VWorld.PrefabLookupMap;

        if (prefab.HasBuffer<SpawnPrefabOnGameplayEvent>())
        {
            var buf = prefab.ReadBuffer<SpawnPrefabOnGameplayEvent>();
            for (var i = 0; i < buf.Length; i++)
                ResolveAndDump(buf[i].SpawnPrefab, indent, $"SpawnPrefabOnGameplayEvent[{i}]", prefabMap, depth, visited);
        }
        else if (prefab.TryGetComponent<SpawnPrefabOnGameplayEvent>(out var sp))
        {
            ResolveAndDump(sp.SpawnPrefab, indent, "SpawnPrefabOnGameplayEvent", prefabMap, depth, visited);
        }
    }

    private static void ResolveAndDump(PrefabGUID guid, string indent, string label, PrefabLookupMap prefabMap, int parentDepth, HashSet<int> visited)
    {
        if (guid.GuidHash == 0) return;

        Plugin.Logger.LogInfo($"{indent}{label} -> guidHash={guid.GuidHash} name='{prefabMap.GetName(guid)}'");
        if (!prefabMap.TryGetValue(guid, out var spawned) || spawned == Entity.Null || !spawned.Exists()) return;
        DumpPrefabEntity(spawned, depth: parentDepth + 1, label: "spawn", visited);
    }

    /// <summary>
    /// Recurse one level into <see cref="LinkedEntityGroup"/> children. Skip the first entry
    /// (LEG conventionally lists the owner entity at index 0) and any entity equal to the
    /// owner to avoid logging the parent twice.
    /// </summary>
    private static void DumpLinkedChildren(Entity prefab, string indent, HashSet<int> visited)
    {
        if (!prefab.HasBuffer<LinkedEntityGroup>()) return;

        var leg = prefab.ReadBuffer<LinkedEntityGroup>();
        for (var i = 0; i < leg.Length; i++)
        {
            var child = leg[i].Value;
            if (child == Entity.Null || child == prefab) continue;
            if (!child.Exists()) continue;
            DumpPrefabEntity(child, depth: 1, label: $"linked[{i}]", visited);
        }
    }
}
