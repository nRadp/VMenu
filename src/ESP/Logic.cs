using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ExtrasensoryPerception.API;
using ExtrasensoryPerception.Patches;
using ExtrasensoryPerception.Utils;
using ExtrasensoryPerception.Utils.Prefabs;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace ExtrasensoryPerception.ESP;

internal static class Logic
{
    private static readonly Vector2 ReferenceResolution = new(2560f, 1440f);
    private static readonly string[] MobNamePatterns = [@"CHAR_\w+_"];
    private static readonly string[] GateBossNamePatterns = ["CHAR", "VBlood"];
    private static readonly string[] ContainerNamePatterns = ["TM_", "_Full", @"[0-9]", "Resource"];
    private static readonly string[] OreNamePatterns = ["TM", @"[0-9]", "Stage", "Resource"];
    private static readonly string[] PlantNamePatterns = ["TM", "BP", "Castle", "Chain", "Plant", @"[0-9]", "Pickup"];
    private static readonly string[] HorseNamePatterns = ["CHAR_", "Mount_"];
    private static readonly string[] CarriageNamePatterns = ["CHAR_"];
    private static readonly Dictionary<string, string> MobNameCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> GateBossNameCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ContainerNameCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> OreNameCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> PlantNameCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> HorseNameCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> CarriageNameCache = new(StringComparer.Ordinal);
    private static readonly Regex CamelCaseRegex = new("(?<=[a-z]|[0-9])([A-Z])", RegexOptions.Compiled);

    private static Camera? _mainCamera;
    private static bool _hasLoggedCameraReacquire;
    private static Camera? _frameCamera;
    private static Entity _frameLocalCharacter = Entity.Null;
    private static Vector3 _frameLocalPosition;
    private static Vector3 _frameCameraPosition;
    private static float _frameScreenWidth;
    private static float _frameScreenHeight;
    private static Vector2 _frameDefaultRectSize;
    private static float _frameFontDist;
    private static float _frameFontDistMin;
    private static float _frameFontSize;
    private static float _frameFontSizeMin;

    private static PrefabLookupMap PrefabLookupMap => VWorld.PrefabLookupMap;
    private static EntityQuery _serverTimeQuery;
    private static bool _serverTimeQueryReady;


    public static void ProcessAllEntities()
    {
        Aimbot.Candidates.Clear();
        if (!PrepareFrame()) return;

        ProcessPlayers();
        ProcessVBloodCarriers();
        ProcessMobs();
        ProcessGateBosses();
        if (Config.ESP.Items.Enabled) ProcessItems();
        if (Config.ESP.Containers.Enabled) ProcessContainers();
        if (Config.ESP.Ores.Enabled) ProcessOres();
        if (Config.ESP.Plants.Enabled) ProcessPlants();
        if (Config.ESP.FishingSpots.Enabled) ProcessFishingSpots();
        if (Config.ESP.Horses.Enabled) ProcessHorses();
        if (Config.ESP.Servants.Enabled) ProcessServants();
        if (Config.ESP.Carriages.Enabled) ProcessCarriages();
        ProcessAndTrackCastleHearts();
    }

    private static bool PrepareFrame()
    {
        _frameLocalCharacter = EntityList.LocalCharacter;
        if (_frameLocalCharacter == Entity.Null || !_frameLocalCharacter.Exists() || !_frameLocalCharacter.Has<LocalToWorld>()) return false;
        if (!TryGetMainCamera(out var camera)) return false;

        _frameCamera = camera;
        _frameLocalPosition = _frameLocalCharacter.GetPosition();
        _frameCameraPosition = camera.transform.position;
        _frameScreenWidth = Screen.width;
        _frameScreenHeight = Screen.height;

        var scaleFactor = Mathf.Min(_frameScreenWidth / ReferenceResolution.x, _frameScreenHeight / ReferenceResolution.y);
        _frameDefaultRectSize = new Vector2(40f * (_frameScreenWidth / ReferenceResolution.x), 100f * (_frameScreenHeight / ReferenceResolution.y));
        // Font scale multiplies both size and line spacing so taller text doesn't collide with
        // the box outline. Box size intentionally stays anchored to world units.
        var fontMul = Config.ESP.FontScale.Value;
        _frameFontDist = 12f * scaleFactor * fontMul;
        _frameFontDistMin = 7.2f * scaleFactor * fontMul;
        _frameFontSize = 11f * scaleFactor * fontMul;
        _frameFontSizeMin = 8f * scaleFactor * fontMul;
        return true;
    }

    private static bool TryGetMainCamera(out Camera camera)
    {
        camera = _mainCamera!;
        if (camera && camera.enabled && camera.isActiveAndEnabled)
        {
            _hasLoggedCameraReacquire = false;
            return true;
        }

        if (!_hasLoggedCameraReacquire)
        {
            Plugin.Logger.LogWarning("Reacquiring MainCamera...");
            _hasLoggedCameraReacquire = true;
        }

        _mainCamera = CameraManager.GetCamera();
        camera = _mainCamera!;
        if (camera && camera.enabled && camera.isActiveAndEnabled)
        {
            _hasLoggedCameraReacquire = false;
            return true;
        }

        return false;
    }

    private static void ProcessPlayers()
    {
        if (!Config.ESP.Players.Enabled && !Config.Aimbot.Players.Enabled) return;
        EntityList.Players.ForEach(entity =>
        {
            if (!CheckEntity(entity) || !entity.TryGetComponent<PlayerCharacter>(out var componentData)) return;
            if (entity == _frameLocalCharacter) return;
            if (AdminObserveCheck.IsAdminObserver(entity)) return;
            if (!entity.TryGetComponent<Health>(out var health) || !entity.TryGetComponent<Equipment>(out var equipment)) return;


            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            var isAlly = entity.IsAlly(_frameLocalCharacter);
            var distance = GetDistanceFromPlayer(position);
            if (Config.Aimbot.Players.Enabled && !isAlly)
                Aimbot.TryAddCandidate(entity, screenPoint, distance, Aimbot.EntityType.Player);

            if (!Config.ESP.Players.Enabled) return;

            var color = isAlly ? Color.green : ColorOptions.GetColor(Config.ESP.Players.Color);

            // Build the bottom label from individually-toggled pieces.
            var showName = Config.ESP.PlayerName.Value;
            var showGear = Config.ESP.PlayerGearLevel.Value;
            var showHP = Config.ESP.PlayerHP.Value;
            string? bottomLabel = null;
            if (showName || showGear)
            {
                var currentName = componentData.Name.ToString();
                var steamId = SocialMenuPatch.GetSteamId(currentName);
                var originalName = steamId != 0 ? PlayerDatabase.GetOriginalNameBySteamId(steamId) : null;
                var displayName = originalName != null && originalName != currentName
                    ? $"{originalName} ({currentName})"
                    : currentName;
                var gearScore = (int)(equipment.ArmorLevel.Value + equipment.WeaponLevel.Value + equipment.SpellLevel.Value);
                bottomLabel = showGear && showName ? $"[{gearScore}] {displayName}"
                            : showGear              ? $"[{gearScore}]"
                            :                         displayName;
            }
            if (showHP)
            {
                var hpLabel = $"HP: {(int)health.Value}";
                bottomLabel = bottomLabel != null ? $"{bottomLabel}\n{hpLabel}" : hpLabel;
            }
            GetBoxLabelMetrics(position, out var rectSize, out var fontSize, out var fontDistance);

            if (Config.ESP.Boxes.Enabled)
                RenderQueue.Box(screenPoint - new Vector2(0f, rectSize.y / 2), rectSize, color);

            // Name/HP label below the box.
            if (bottomLabel != null)
                RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), bottomLabel, color, fontSize);

            // Cooldown icons: single horizontal row centered under the native overhead HP bar.
            if (ShouldShowEnemyCooldowns(entity))
            {
                var cooldownMode = Config.Extras.EnemyCooldownTrackerDisplayMode.Value;
                if (cooldownMode == 1)
                {
                    var pips = EnemyCooldownTracker.GetCooldownPips(entity);
                    if (pips.Count > 0)
                    {
                        // Prefer native overhead HUD; fall back to box top when CharacterHUD is missing.
                        Vector2 hudAnchor;
                        if (entity.TryGetComponent<CharacterHUD>(out var hud) &&
                            GetScreenPoint(position + new Vector3(0f, hud.Height.Value, 0f), out var hudScreenPoint))
                            hudAnchor = hudScreenPoint;
                        else
                            hudAnchor = screenPoint - new Vector2(0f, rectSize.y);

                        DrawCooldownPips(hudAnchor, pips, fontSize, fontDistance);
                    }
                }
                else if (entity.TryGetComponent<CharacterHUD>(out var hudText) &&
                         GetScreenPoint(position + new Vector3(0f, hudText.Height.Value, 0f), out var hudTextPoint))
                {
                    var cooldownLine = EnemyCooldownTracker.FormatCooldownLine(entity);
                    if (!string.IsNullOrEmpty(cooldownLine))
                    {
                        var textY = hudTextPoint.y + fontDistance * 0.5f;
                        RenderQueue.String(new Vector2(hudTextPoint.x, textY),
                            cooldownLine, color, fontSize, forceOutline: true);
                    }
                }
            }
        });
    }

    /// <summary>
    /// Show enemy cooldown pips when Always Show HUD or any ESP name/gear/HP toggle is on,
    /// or when all of those are off but the game's native overhead bar is still visible.
    /// </summary>
    private static bool ShouldShowEnemyCooldowns(Entity entity)
    {
        if (!Config.Extras.EnemyCooldownTracker.Enabled) return false;
        if (Config.ESP.AlwaysShowPlayerHUD.Value ||
            Config.ESP.PlayerName.Value ||
            Config.ESP.PlayerGearLevel.Value ||
            Config.ESP.PlayerHP.Value)
            return true;
        return HasNativePlayerHudVisible(entity);
    }

    private static bool HasNativePlayerHudVisible(Entity entity)
    {
        if (!entity.TryGetComponent<CheckOnScreen>(out var check)) return false;
        if (!check.IsOnScreen || !check.HasLineOfSight) return false;
        if (entity.TryGetComponent<Hideable>(out var hideable) && hideable.IsHidden) return false;
        return true;
    }

    /// <summary>
    /// Draw cooldown pips as a horizontal row just below the game's native overhead HP bar.
    /// Prefers the real ability icon when <see cref="AbilityIconResolver"/> can resolve it;
    /// otherwise falls back to a colored square. Ready = full-bright icon; on cooldown =
    /// dimmed icon with the remaining timer drawn underneath (not overlaid).
    /// <paramref name="anchorPoint"/> is the screen-space position of the game's
    /// overhead HUD (projected from entity position + CharacterHUD.Height).
    /// </summary>
    private static void DrawCooldownPips(Vector2 anchorPoint,
        List<EnemyCooldownTracker.CooldownPip> pips, float fontSize, float fontDistance)
    {
        var pipSize = fontSize * 1.7f;
        var spacing = pipSize * 0.2f;
        var totalWidth = pips.Count * pipSize + (pips.Count - 1) * spacing;
        var startX = anchorPoint.x - totalWidth / 2f;
        // Just under the name + HP bar (CharacterHUD.Height anchors near the top of that widget).
        var pipY = anchorPoint.y + fontDistance * 0.55f + pipSize * 0.15f;
        var timerFontSize = fontSize;
        var timerOffsetY = pipSize * 0.5f + fontDistance * 0.35f;

        for (var i = 0; i < pips.Count; i++)
        {
            var pip = pips[i];
            var centerX = startX + i * (pipSize + spacing) + pipSize / 2f;
            var center = new Vector2(centerX, pipY);
            var size = new Vector2(pipSize, pipSize);
            var ready = pip.Remaining <= 0;
            var hasIcon = AbilityIconResolver.TryGetSprite(pip.AbilityHash, out var sprite);

            if (hasIcon)
            {
                var tint = ready ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                RenderQueue.Icon(center, size, sprite, tint);
                RenderQueue.Box(center, size, Color.black, 1f);
            }
            else if (ready)
            {
                // Fallback — bright filled square with border
                RenderQueue.FilledRect(center, size, pip.Color);
                RenderQueue.Box(center, size, Color.black, 1f);
            }

            if (!ready)
            {
                var timerText = pip.Remaining < 5.0
                    ? pip.Remaining.ToString("F1", CultureInfo.InvariantCulture)
                    : ((int)pip.Remaining).ToString(CultureInfo.InvariantCulture);
                var timerColor = hasIcon ? Color.red : pip.Color;
                var timerPos = new Vector2(centerX, pipY + timerOffsetY);
                RenderQueue.String(timerPos, timerText, timerColor, timerFontSize, forceOutline: true);
            }
        }
    }

    private static void ProcessVBloodCarriers()
    {
        if (!Config.ESP.VBloodCarriers.Enabled && !Config.Aimbot.Bosses.Enabled) return;
        EntityList.VBloodCarriers.ForEach(entity =>
        {
            if (!CheckEntity(entity) || !entity.TryGetComponent<VBloodConsumeSource>(out var componentData)) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            var distance = GetDistanceFromPlayer(position);
            if (Config.Aimbot.Bosses.Enabled)
                Aimbot.TryAddCandidate(entity, screenPoint, distance, Aimbot.EntityType.Boss);

            if (!Config.ESP.VBloodCarriers.Enabled) return;

            var name = VBloods.GetName(componentData.Source);
            var hpLabel = $"HP: {(int)entity.Read<Health>().Value}";
            var color = ColorOptions.GetColor(Config.ESP.VBloodCarriers.Color);

            GetBoxLabelMetrics(position, out var rectSize, out var fontSize, out var fontDistance);
            RenderQueue.String(new Vector2(screenPoint.x, screenPoint.y - rectSize.y - fontDistance), name, color, fontSize);
            if (Config.ESP.Boxes.Enabled)
                RenderQueue.Box(screenPoint - new Vector2(0f, rectSize.y / 2), rectSize, color);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{hpLabel}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessMobs()
    {
        if (!Config.ESP.HighQualityBlood.Enabled && !Config.Aimbot.Mobs.Enabled) return;
        EntityList.Mobs.ForEach(entity =>
        {
            if (!CheckEntity(entity) || !entity.TryGetComponent<BloodConsumeSource>(out var componentData)) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            var distance = GetDistanceFromPlayer(position);
            if (Config.Aimbot.Mobs.Enabled)
                Aimbot.TryAddCandidate(entity, screenPoint, distance, Aimbot.EntityType.Mob);

            if (!Config.ESP.HighQualityBlood.Enabled ||
                !BloodTypes.MeetsQualityThreshold(componentData.BloodQuality, Config.ESP.HighQualityBlood.MinimumQuality)) return;

            var bloodType = BloodTypes.GetName(componentData.UnitBloodType);
            if (!BloodTypes.MatchesFilter(bloodType, Config.ESP.HighQualityBloodTypeFilter.Value)) return;

            var name = GetCachedCleanName(MobNameCache, entity.GetName(), MobNamePatterns);
            var bloodLabel = $"{bloodType} ({(int)componentData.BloodQuality}%)";
            var color = ColorOptions.GetColor(Config.ESP.HighQualityBlood.Color);

            GetBoxLabelMetrics(position, out var rectSize, out var fontSize, out var fontDistance);
            RenderQueue.String(new Vector2(screenPoint.x, screenPoint.y - rectSize.y - fontDistance), name, color, fontSize);
            if (Config.ESP.Boxes.Enabled)
                RenderQueue.Box(screenPoint - new Vector2(0f, rectSize.y / 2), rectSize, color);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{bloodLabel}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessGateBosses()
    {
        if (!Config.ESP.GateBosses.Enabled && !Config.Aimbot.Bosses.Enabled) return;
        EntityList.GateBosses.ForEach(entity =>
        {
            if (!CheckEntity(entity)) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            var distance = GetDistanceFromPlayer(position);
            if (Config.Aimbot.Bosses.Enabled)
                Aimbot.TryAddCandidate(entity, screenPoint, distance, Aimbot.EntityType.Boss);

            if (!Config.ESP.GateBosses.Enabled) return;

            var name = GetCachedCleanName(GateBossNameCache, entity.GetName(), GateBossNamePatterns);
            var hpLabel = $"HP: {(int)entity.Read<Health>().Value}";
            var color = ColorOptions.GetColor(Config.ESP.GateBosses.Color);

            GetLabelMetrics(position, out var fontSize, out var fontDistance);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{name}\n{hpLabel}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessContainers()
    {
        EntityList.Containers.ForEach(entity =>
        {
            if (!entity.Exists()) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            var name = GetCachedCleanName(ContainerNameCache, entity.GetName(), ContainerNamePatterns);
            var distance = GetDistanceFromPlayer(position);
            var color = ColorOptions.GetColor(Config.ESP.Containers.Color);

            GetLabelMetrics(position, out var fontSize, out var fontDistance);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{name}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessItems()
    {
        EntityList.Items.ForEach(entity =>
        {
            if (!entity.Exists()) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint) || !entity.TryGetComponent<ItemPickup>(out var itemPickup)) return;

            var name = Items.CleanName(PrefabLookupMap.GetName(itemPickup.ItemId));
            name += !name.Contains("Jewel") && itemPickup.ItemAmount > 1 ? $" ({itemPickup.ItemAmount})" : "";
            var distance = GetDistanceFromPlayer(position);
            var color = Color.white;

            GetLabelMetrics(position, out var fontSize, out var fontDistance);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{name}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessOres()
    {
        EntityList.Ores.ForEach(entity =>
        {
            if (!entity.Exists()) return;

            var name = entity.GetName();
            if (name.Contains("Rock") || !name.Contains("Resource")) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            name = GetCachedCleanName(OreNameCache, name, OreNamePatterns);
            var distance = GetDistanceFromPlayer(position);
            var color = ColorOptions.GetColor(Config.ESP.Ores.Color);

            GetLabelMetrics(position, out var fontSize, out var fontDistance);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{name}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessPlants()
    {
        EntityList.Plants.ForEach(entity =>
        {
            if (!entity.Exists()) return;

            var name = entity.GetName();
            if (!name.Contains("Plant") || name.Contains("fiber") || name.Contains("Cotton")) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            name = GetCachedCleanName(PlantNameCache, name, PlantNamePatterns);
            var distance = GetDistanceFromPlayer(position);
            var color = ColorOptions.GetColor(Config.ESP.Plants.Color);

            GetLabelMetrics(position, out var fontSize, out var fontDistance);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{name}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessFishingSpots()
    {
        EntityList.FishingSpots.ForEach(entity =>
        {
            if (!CheckEntity(entity)) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            const string name = "Fishing Spot";
            var distance = GetDistanceFromPlayer(position);
            var color = ColorOptions.GetColor(Config.ESP.FishingSpots.Color);

            GetLabelMetrics(position, out var fontSize, out var fontDistance);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{name}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessHorses()
    {
        EntityList.Horses.ForEach(entity =>
        {
            if (!CheckEntity(entity) || !entity.TryGetComponent<Mountable>(out var mountable)) return;

            var fraction = Config.ESP.Horses.MinimumQuality / 100;
            var desiredQuality = mountable.Acceleration >= 7f * fraction && mountable.MaxSpeed >= 11f * fraction && mountable.RotationSpeed / 10 >= 14f * fraction;
            var position = entity.GetPosition();

            if (!desiredQuality || !GetScreenPoint(position, out var screenPoint)) return;

            var name = GetCachedCleanName(HorseNameCache, entity.GetName(), HorseNamePatterns);
            var qualityLabel = $"S: {mountable.MaxSpeed} | A: {mountable.Acceleration} | R: {mountable.RotationSpeed / 10}";
            var distance = GetDistanceFromPlayer(position);
            var color = ColorOptions.GetColor(Config.ESP.Horses.Color);

            GetLabelMetrics(position, out var fontSize, out var fontDistance);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{name}\n{qualityLabel}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessServants()
    {
        EntityList.Servants.ForEach(entity =>
        {
            if (!CheckEntity(entity) || !entity.TryGetComponent<ServantPower>(out var servantPower)) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            var upperLabel = $"[{servantPower.GearLevel}] Servant";
            var bottomLabel = $"Expertise: {servantPower.Expertise * 100:F0}% | Power: {servantPower.Power:F1}";
            var distance = GetDistanceFromPlayer(position);
            var color = ColorOptions.GetColor(Config.ESP.Servants.Color);

            GetBoxLabelMetrics(position, out var rectSize, out var fontSize, out var fontDistance);
            RenderQueue.String(new Vector2(screenPoint.x, screenPoint.y - rectSize.y - fontDistance), upperLabel, color, fontSize);
            if (Config.ESP.Boxes.Enabled)
                RenderQueue.Box(screenPoint - new Vector2(0f, rectSize.y / 2), rectSize, color);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{bottomLabel}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessCarriages()
    {
        EntityList.Carriages.ForEach(entity =>
        {
            if (!CheckEntity(entity)) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            var name = GetCachedCleanName(CarriageNameCache, entity.GetName(), CarriageNamePatterns);
            var distance = GetDistanceFromPlayer(position);
            var color = ColorOptions.GetColor(Config.ESP.Carriages.Color);

            GetLabelMetrics(position, out var fontSize, out var fontDistance);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"{name}\n{distance:F1}m", color, fontSize);
        });
    }

    private static void ProcessAndTrackCastleHearts()
    {
        var drawEnabled = Config.ESP.CastleHearts.Enabled;
        EntityList.CastleHearts.ForEach(entity =>
        {
            if (!entity.Exists() || entity.IsDisabled() || !entity.Has<LocalToWorld>()) return;
            CastleHeartTracker.Register(entity);

            if (!drawEnabled) return;

            var position = entity.GetPosition();
            if (!GetScreenPoint(position, out var screenPoint)) return;

            var distance = GetDistanceFromPlayer(position);
            var color = ColorOptions.GetColor(Config.ESP.CastleHearts.Color);
            var timeLabel = TryGetCastleHeartRemainingText(entity, out var remainingText) ? remainingText : "Time: ?";

            GetLabelMetrics(position, out var fontSize, out var fontDistance);
            RenderQueue.String(screenPoint + new Vector2(0, fontDistance * 1.5f), $"Castle Heart\n{timeLabel}\n{distance:F1}m", color, fontSize);
        });
    }

    internal static bool TryGetCastleHeartRemainingText(Entity entity, out string text)
    {
        text = string.Empty;
        if (!TryGetCastleHeartRemainingSeconds(entity, out var remainingSeconds) || remainingSeconds < 0d)
            return false;

        var remaining = TimeSpan.FromSeconds(remainingSeconds);
        var totalDays = (int)remaining.TotalDays;
        var totalHours = (int)remaining.TotalHours;
        text = totalDays > 0
            ? $"{totalDays}d {remaining.Hours}h {remaining.Minutes}m {remaining.Seconds}s"
            : $"{totalHours}h {remaining.Minutes}m {remaining.Seconds}s";
        return true;
    }

    internal static bool TryGetCastleHeartRemainingSeconds(Entity entity, out double remainingSeconds)
    {
        remainingSeconds = 0d;
        if (entity.TryGetComponent<ProjectM.CastleBuilding.CastleHeartVisuals>(out var castleHeartVisuals))
        {
            remainingSeconds = castleHeartVisuals.TotalFuelTimeRemaining;
            if (IsValidCastleHeartRemainingSeconds(remainingSeconds)) return true;
        }

        if (!entity.TryGetComponent<ProjectM.CastleBuilding.CastleHeart>(out var castleHeart)) return false;
        if (!TryGetServerTimeNow(out var serverNow)) return false;

        remainingSeconds = castleHeart.FuelEndTime - serverNow;
        if (IsValidCastleHeartRemainingSeconds(remainingSeconds)) return true;

        var eventRemaining = castleHeart.EventEndTime - serverNow;
        var raidRemaining = castleHeart.RaidProtectionEndTime - serverNow;
        // Pick the larger valid candidate
        var best = eventRemaining >= raidRemaining ? eventRemaining : raidRemaining;
        var worst = eventRemaining >= raidRemaining ? raidRemaining : eventRemaining;
        if (IsValidCastleHeartRemainingSeconds(best)) { remainingSeconds = best; return true; }
        if (IsValidCastleHeartRemainingSeconds(worst)) { remainingSeconds = worst; return true; }

        remainingSeconds = 0d;
        return false;
    }

    internal static bool IsValidCastleHeartRemainingSeconds(double remainingSeconds)
    {
        return remainingSeconds > 0d && remainingSeconds <= 86400d * 60d;
    }

    internal static bool TryGetServerTimeNow(out double serverNow)
    {
        serverNow = Time.timeAsDouble;
        if (!_serverTimeQueryReady)
        {
            try
            {
                _serverTimeQuery = VWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ServerTime>());
                _serverTimeQueryReady = true;
            }
            catch
            {
                return false;
            }
        }
        if (_serverTimeQuery.IsEmpty) return false;
        var serverTimeEntity = _serverTimeQuery.GetSingletonEntity();
        if (!serverTimeEntity.TryGetComponent<ServerTime>(out var serverTime)) return false;
        serverNow = serverTime.Time;
        return true;
    }

    internal static void ResetServerTimeQuery()
    {
        _serverTimeQueryReady = false;
        _serverTimeQuery = default;
    }

    private static string GetCachedCleanName(Dictionary<string, string> cache, string input, string[] patterns)
    {
        if (cache.TryGetValue(input, out var cachedName)) return cachedName;

        var cleanName = CleanAndFormatName(input, patterns);
        cache[input] = cleanName;
        return cleanName;
    }

    private static string CleanAndFormatName(string input, string[] patterns)
    {
        var result = input;
        for (var i = 0; i < patterns.Length; i++)
        {
            result = Regex.Replace(result, patterns[i], "");
        }

        result = Regex.Replace(result, "_", "");
        return CamelCaseRegex.Replace(result, " $1");
    }

    private static bool CheckEntity(Entity entity)
    {
        return entity.Exists() && !entity.IsDisabled() && entity.IsAlive() && entity.Has<LocalToWorld>();
    }

    private static float GetDistanceFromPlayer(Vector3 position)
    {
        return Vector3.Distance(_frameLocalPosition, position);
    }

    private static float GetScale(Vector3 position)
    {
        return 20f / Vector3.Distance(_frameCameraPosition, position);
    }

    private static void GetLabelMetrics(Vector3 position, out float fontSize, out float fontDistance)
    {
        var scale = GetScale(position);
        fontSize = Mathf.Max(_frameFontSizeMin, _frameFontSize * scale);
        fontDistance = Mathf.Max(_frameFontDistMin, _frameFontDist * scale);
    }

    private static void GetBoxLabelMetrics(Vector3 position, out Vector2 rectSize, out float fontSize, out float fontDistance)
    {
        var scale = GetScale(position);
        rectSize = _frameDefaultRectSize * scale;
        fontSize = Mathf.Max(_frameFontSizeMin, _frameFontSize * scale);
        fontDistance = Mathf.Max(_frameFontDistMin, _frameFontDist * scale);
    }

    internal static bool GetScreenPoint(Vector3 position, out Vector2 screenPoint)
    {
        var screenPos3D = _frameCamera!.WorldToScreenPoint(position);
        screenPoint = new Vector2(screenPos3D.x, _frameScreenHeight - screenPos3D.y + 10f); // +10f to sync visuals

        return screenPos3D.z > 0f &&
               screenPoint.x >= 0f && screenPoint.x <= _frameScreenWidth &&
               screenPoint.y >= 0f && screenPoint.y <= _frameScreenHeight;
    }
}
