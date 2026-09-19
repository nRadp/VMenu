using System.Collections.Generic;
using ExtrasensoryPerception.API;
using ExtrasensoryPerception.Patches;
using ExtrasensoryPerception.Utils;
using ProjectM;
using Unity.Entities;
using UnityEngine;

namespace ExtrasensoryPerception.ESP;

public static class Aimbot
{
    internal static bool Active;
    internal static Vector2 CursorPosition;

    private static Entity _currentTarget = Entity.Null;
    private static Vector2 _cachedAimData = Vector2.zero;
    private static float _lastTargetSwitchTime;

    internal enum EntityType
    {
        Player,
        Boss,
        Mob,
    }

    internal readonly struct TargetCandidate(Entity entity, float score)
    {
        public Entity Entity { get; } = entity;
        public float Score { get; } = score;
    }

    internal static readonly List<TargetCandidate> Candidates = [];

    internal static void TryAddCandidate(Entity entity, Vector2 screenPoint, float distance, EntityType type)
    {
        if (distance > Config.Aimbot.MaxDistance.Value) return;

        var cursorDistance = Vector2.Distance(CursorPosition, screenPoint);
        if (cursorDistance > Config.Aimbot.MaxCursorDistance.Value) return;

        Candidates.Add(new TargetCandidate(entity, CalculateTargetScore(entity, distance, cursorDistance, type)));
    }

    private static float CalculateTargetScore(Entity entity, float distance, float screenDistance, EntityType type)
    {
        // Normalize values to 0-1
        var distanceScore = 1f - distance / Config.Aimbot.MaxDistance.Value;
        var screenDistanceScore = 1f - screenDistance / Config.Aimbot.MaxCursorDistance.Value;

        // Health-based priority (lower health = higher priority for finishing off)
        var health = entity.Read<Health>();
        var healthPercent = health.Value / health.MaxHealth;
        var healthScore = healthPercent <= 0.25f ? 1f : 1f - healthPercent;

        // Type priority
        var typeScore = type switch
        {
            EntityType.Player => 1f,
            EntityType.Boss => 0.5f,
            _ => 0.25f
        };

        // Weighted combination
        return distanceScore * 0.5f +
               screenDistanceScore * 0.75f +
               healthScore * 0.25f +
               typeScore * 1.0f;
    }

    internal static void UpdateAimData()
    {
        if (Candidates.Count == 0)
        {
            _currentTarget = Entity.Null;
            _cachedAimData = Vector2.zero;
            return;
        }

        var currentTarget = default(TargetCandidate);
        for (var i = 0; i < Candidates.Count; i++)
        {
            var candidate = Candidates[i];
            if (candidate.Entity != _currentTarget) continue;

            currentTarget = candidate;
            break;
        }

        var currentTime = Time.time;
        if (!IsCurrentTargetValid() || currentTime - _lastTargetSwitchTime > Config.Aimbot.SwitchCooldown.Value)
        {
            // Find the best candidate
            for (var i = 0; i < Candidates.Count; i++)
            {
                var candidate = Candidates[i];
                if (candidate.Score <= currentTarget.Score) continue;

                currentTarget = candidate;
            }

            if (_currentTarget != currentTarget.Entity)
            {
                _currentTarget = currentTarget.Entity;
                _lastTargetSwitchTime = currentTime;
            }
        }

        // Update aim data
        _cachedAimData = GetPredictedScreenPosition(_currentTarget);
    }

    internal static bool IsCurrentTargetValid()
    {
        return _currentTarget.Exists() && !_currentTarget.IsDisabled() && _currentTarget.IsAlive();
    }

    private static Vector2 GetPredictedScreenPosition(Entity entity)
    {
        var playerPos = EntityList.LocalCharacter.GetPosition();
        var targetPos = entity.GetPosition();
        var targetSpeed = (Vector3)entity.Read<Velocity>().Value;
        var toTarget = targetPos - playerPos;
        var projectileSpeed = ProjectileSystemPatch.ProjectileSpeed;

        // Bhaskara's formula (x = (-b ± √(b² - 4ac)) / 2a)
        var a = targetSpeed.sqrMagnitude - projectileSpeed * projectileSpeed;
        var b = 2 * Vector3.Dot(toTarget, targetSpeed);
        var c = toTarget.sqrMagnitude;
        var delta = b * b - 4 * a * c;

        if (delta >= 0 && Mathf.Abs(a) >= 0.001f) // Otherwise, no real solution.
        {
            var t1 = (-b + Mathf.Sqrt(delta)) / (2 * a);
            var t2 = (-b - Mathf.Sqrt(delta)) / (2 * a);

            // Use the *positive*, smaller time
            var time = t1 > 0 && t2 > 0 ? Mathf.Min(t1, t2) : Mathf.Max(t1, t2);
            if (time > 0) // Time should be positive.
            {
                targetPos += targetSpeed * time;
            }
        }

        return Logic.GetScreenPoint(targetPos, out var screenPoint) ? screenPoint : Vector2.zero;
    }

    public static Vector2 GetAimData() => _cachedAimData;
}

public class AimController : MonoBehaviour
{
    private void OnGUI() // once per frame
    {
        if (Event.current.type != EventType.Repaint) return;
        if (!Config.Aimbot.Enabled || !Config.Aimbot.DrawAimPosition.Enabled || !Aimbot.IsCurrentTargetValid()) return;
        var aimData = Aimbot.GetAimData();
        if (aimData != Vector2.zero)
            Primitives.DrawX(aimData, 5f, Color.white);
    }

    private void Update()
    {
        if (!Config.Aimbot.Enabled) return;
        Aimbot.CursorPosition = MouseSimulator.CursorPosition;
        Aimbot.UpdateAimData();

        var key1 = Config.Aimbot.Key.Value;
        var key2 = Config.Aimbot.Key2.Value;
        if (Config.Aimbot.Mode.Value == 1) // Toggle
        {
            if (Input.GetKeyDown(key1) || (key2 != KeyCode.None && Input.GetKeyDown(key2)))
                Aimbot.Active = !Aimbot.Active;
        }
        else
        {
            Aimbot.Active = Input.GetKey(key1) || (key2 != KeyCode.None && Input.GetKey(key2));
        }
    }

    private void LateUpdate()
    {
        if (!Application.isFocused || !Config.Aimbot.Enabled) return;
        if (!Aimbot.Active || !Aimbot.IsCurrentTargetValid()) return;
        var aimData = Aimbot.GetAimData();
        if (aimData != Vector2.zero)
            AimAt(aimData);
    }

    private static void AimAt(Vector2 aimData) => MouseSimulator.SetPos(aimData);
}
