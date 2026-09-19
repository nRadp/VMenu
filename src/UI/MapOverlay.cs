using System.Collections.Generic;
using ExtrasensoryPerception.API;
using ExtrasensoryPerception.ESP;
using ExtrasensoryPerception.Utils;
using ProjectM;
using ProjectM.UI;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ExtrasensoryPerception.UI;

internal static class MapOverlay
{
    private static readonly OverlaySurfaceState MinimapSurface = new("Minimap", new Vector2(24f, 24f));
    private static readonly OverlaySurfaceState WorldMapSurface = new("WorldMap");
    private static readonly List<MarkerRequest> MarkerRequests = [];
    private static readonly MarkerCollector[] MarkerCollectors =
    [
        AppendPlayerMarkers,
        AppendHighQualityBloodMarkers,
        AppendCastleHeartMarkers
    ];

    private delegate void MarkerCollector(FrameContext frameContext);

    private sealed class OverlaySurfaceState(string name, Vector2? defaultMarkerSize = null)
    {
        internal string Name { get; } = name;
        internal Vector2? DefaultMarkerSize { get; } = defaultMarkerSize;
        internal List<Image> MarkerPool { get; } = [];
        internal Object? Owner { get; set; }
        internal RectTransform? MarkerContainer { get; set; }
        internal Image? MarkerTemplate { get; set; }
        internal Vector2 TemplateSize { get; set; }
    }

    private readonly struct FrameContext(
        OverlaySurfaceState surface,
        Entity localCharacter,
        Matrix4x4 worldToAnchoredSpace,
        Vector2 anchoredOrigin,
        float clampDistance,
        float rotationAdjustment)
    {
        internal OverlaySurfaceState Surface { get; } = surface;
        internal Entity LocalCharacter { get; } = localCharacter;
        internal Matrix4x4 WorldToAnchoredSpace { get; } = worldToAnchoredSpace;
        internal Vector2 AnchoredOrigin { get; } = anchoredOrigin;
        internal float ClampDistance { get; } = clampDistance;
        internal float RotationAdjustment { get; } = rotationAdjustment;
    }

    private readonly struct MarkerStyle(
        Color color,
        string namePrefix,
        Vector2? sizeOverride = null,
        string? label = null,
        bool showIcon = true)
    {
        internal Color Color { get; } = color;
        internal string NamePrefix { get; } = namePrefix;
        internal Vector2? SizeOverride { get; } = sizeOverride;
        internal string? Label { get; } = label;
        internal bool ShowIcon { get; } = showIcon;
    }

    private readonly struct MarkerRequest(
        Vector2 anchoredPosition,
        Quaternion rotation,
        Color color,
        Vector2? sizeOverride,
        string name,
        string? label = null,
        bool showIcon = true)
    {
        internal Vector2 AnchoredPosition { get; } = anchoredPosition;
        internal Quaternion Rotation { get; } = rotation;
        internal Color Color { get; } = color;
        internal Vector2? SizeOverride { get; } = sizeOverride;
        internal string Name { get; } = name;
        internal string? Label { get; } = label;
        internal bool ShowIcon { get; } = showIcon;
    }

    internal static void ResetState()
    {
        ResetSurface(MinimapSurface);
        ResetSurface(WorldMapSurface);
        MarkerRequests.Clear();
    }

    internal static void UpdateMinimap(MiniMapHUDSystem miniMapHudSystem)
    {
        if (!TryCreateMinimapFrameContext(miniMapHudSystem, out var frameContext))
        {
            HideAllMarkers(MinimapSurface);
            return;
        }

        UpdateSurface(frameContext);
    }

    internal static void UpdateWorldMap(MapMenuMapper mapMenuMapper)
    {
        if (!TryCreateWorldMapFrameContext(mapMenuMapper, out var frameContext))
        {
            HideAllMarkers(WorldMapSurface);
            return;
        }

        UpdateSurface(frameContext);
    }

    private static void UpdateSurface(FrameContext frameContext)
    {
        MarkerRequests.Clear();
        CollectMarkers(frameContext);
        RenderMarkers(frameContext.Surface);
    }

    private static bool TryCreateMinimapFrameContext(MiniMapHUDSystem miniMapHudSystem, out FrameContext frameContext)
    {
        frameContext = default;
        if (!CanRenderOverlay()) return false;

        var miniMapParent = miniMapHudSystem._MiniMapParent;
        if (miniMapParent == null || miniMapParent.MinimapMarkerPrefab == null) return false;

        var markerContainer = miniMapParent.MinimapMarkerPrefab.rectTransform.parent as RectTransform;
        if (markerContainer == null)
            markerContainer = miniMapParent.Mask;
        if (markerContainer == null)
            markerContainer = miniMapParent.MapMarkerNode1;
        if (markerContainer == null) return false;
        if (!TryInitializeSurface(MinimapSurface, miniMapParent, markerContainer, miniMapParent.MinimapMarkerPrefab)) return false;

        var localCharacter = EntityList.LocalCharacter;
        if (localCharacter == Entity.Null || !IsRenderableEntity(localCharacter)) return false;

        var currentZoneData = miniMapHudSystem._CurrentZoneData;
        if (!currentZoneData.HasTextureData) return false;

        var clampDistance = miniMapParent.ClampDist;
        if (clampDistance <= 0f) return false;

        var localPosition = localCharacter.GetPosition();
        var worldToAnchoredSpace = currentZoneData.WorldToAnchoredSpace;
        var localAnchored = MapUtils.GetAnchoredPositionFromWorldPos2d(worldToAnchoredSpace, new float2(localPosition.x, localPosition.z));
        var rotationAdjustment = MiniMapHUDSystem.LockRotation ? 0f : miniMapHudSystem.GetCameraRotation();
        frameContext = new FrameContext(
            MinimapSurface,
            localCharacter,
            worldToAnchoredSpace,
            new Vector2(localAnchored.x, localAnchored.y),
            clampDistance,
            rotationAdjustment);
        return true;
    }

    private static bool TryCreateWorldMapFrameContext(MapMenuMapper mapMenuMapper, out FrameContext frameContext)
    {
        frameContext = default;
        if (!CanRenderOverlay()) return false;

        var mapMenu = mapMenuMapper._MapMenu;
        if (mapMenu == null || !mapMenu.gameObject.activeInHierarchy) return false;
        if (mapMenu.MapNode == null || !mapMenu.MapNode.gameObject.activeInHierarchy) return false;
        if (mapMenu.PlayerMarker == null) return false;
        if (!mapMenuMapper.HasSetProjectionMatrix) return false;

        var markerContainer = mapMenu.PlayerMarker.rectTransform.parent as RectTransform;
        if (markerContainer == null)
            markerContainer = mapMenu.MapMarkerNode1;
        if (markerContainer == null) return false;
        if (!TryInitializeSurface(WorldMapSurface, mapMenu, markerContainer, mapMenu.PlayerMarker)) return false;

        var localCharacter = EntityList.LocalCharacter;
        if (localCharacter == Entity.Null || !IsRenderableEntity(localCharacter)) return false;

        frameContext = new FrameContext(
            WorldMapSurface,
            localCharacter,
            mapMenuMapper._WorldToAnchoredSpace,
            Vector2.zero,
            0f,
            0f);
        return true;
    }

    private static bool CanRenderOverlay()
    {
        return Config.ModToggle.Value && Plugin.IsInGame;
    }

    private static bool TryInitializeSurface(OverlaySurfaceState surface, Object owner, RectTransform markerContainer, Image markerTemplate)
    {
        if (surface.Owner == owner && surface.MarkerContainer == markerContainer && surface.MarkerTemplate == markerTemplate)
            return true;

        ClearMarkers(surface);
        surface.Owner = owner;
        surface.MarkerContainer = markerContainer;
        surface.MarkerTemplate = markerTemplate;
        surface.TemplateSize = markerTemplate.rectTransform.sizeDelta;
        return true;
    }

    private static void CollectMarkers(FrameContext frameContext)
    {
        for (var i = 0; i < MarkerCollectors.Length; i++)
        {
            MarkerCollectors[i](frameContext);
        }
    }

    private static void AppendPlayerMarkers(FrameContext frameContext)
    {
        if (!Config.ESP.MinimapPlayers.Enabled) return;

        EntityList.Players.ForEach(entity =>
        {
            if (entity == frameContext.LocalCharacter || !IsRenderablePlayer(entity)) return;

            var style = new MarkerStyle(
                entity.IsAlly(frameContext.LocalCharacter)
                    ? Color.green
                    : ColorOptions.GetColor(Config.ESP.MinimapPlayers.Color),
                "ESP_PlayerMarker");

            AddEntityMarker(entity, frameContext, style);
        });
    }

    private static void AppendHighQualityBloodMarkers(FrameContext frameContext)
    {
        if (!Config.ESP.HighQualityBlood.Enabled) return;

        EntityList.Mobs.ForEach(entity =>
        {
            if (!IsRenderableEntity(entity) || !entity.TryGetComponent<BloodConsumeSource>(out var bloodConsumeSource)) return;
            if (!BloodTypes.MeetsQualityThreshold(bloodConsumeSource.BloodQuality, Config.ESP.HighQualityBlood.MinimumQuality)) return;

            var bloodType = BloodTypes.GetName(bloodConsumeSource.UnitBloodType);
            if (!BloodTypes.MatchesFilter(bloodType, Config.ESP.HighQualityBloodTypeFilter.Value)) return;

            var style = new MarkerStyle(
                ColorOptions.GetColor(Config.ESP.HighQualityBlood.Color),
                "ESP_HighQualityBloodMarker");

            AddEntityMarker(entity, frameContext, style);
        });
    }

    private static void AppendCastleHeartMarkers(FrameContext frameContext)
    {
        // Pinned heart: always render from tracker position, regardless of entity range or ESP toggle
        var pinned = CastleHeartTracker.GetSelected();
        if (pinned != null)
        {
            var pinnedLabel = $"★  {pinned.CurrentRemainingText}";
            var pinnedStyle = new MarkerStyle(
                new Color(1f, 0.82f, 0.28f, 1f),
                "ESP_CastleHeartPinned",
                new Vector2(26f, 26f),
                pinnedLabel,
                showIcon: false);
            AddMarker(pinned.Position, Quaternion.identity, frameContext, pinnedStyle);
        }

        if (!Config.ESP.CastleHearts.Enabled) return;

        EntityList.CastleHearts.ForEach(entity =>
        {
            if (!entity.Exists() || entity.IsDisabled() || !entity.Has<LocalToWorld>()) return;
            // Skip if this is the pinned entity — already drawn above with the highlighted style
            if (pinned != null && entity.Index == pinned.EntityIndex) return;

            var label = Logic.TryGetCastleHeartRemainingText(entity, out var remainingText) ? remainingText : "?";
            var style = new MarkerStyle(
                Color.yellow,
                "ESP_CastleHeartMarker",
                new Vector2(20f, 20f),
                label,
                showIcon: false);

            AddEntityMarker(entity, frameContext, style);
        });
    }

    private static void AddEntityMarker(Entity entity, FrameContext frameContext, MarkerStyle style)
    {
        AddMarker(entity.GetPosition(), entity.GetRotation(), frameContext, style);
    }

    private static void AddMarker(Vector3 worldPosition, Quaternion worldRotation, FrameContext frameContext, MarkerStyle style)
    {
        var anchoredPosition = GetRelativeAnchoredPosition(worldPosition, frameContext);
        if (frameContext.ClampDistance > 0f && anchoredPosition.sqrMagnitude > frameContext.ClampDistance * frameContext.ClampDistance)
            anchoredPosition = anchoredPosition.normalized * frameContext.ClampDistance;

        MarkerRequests.Add(new MarkerRequest(
            anchoredPosition,
            MapUtils.WorldRotationToIconRotation(worldRotation, frameContext.RotationAdjustment),
            style.Color,
            style.SizeOverride,
            $"{style.NamePrefix}_{MarkerRequests.Count}",
            style.Label,
            style.ShowIcon));
    }

    private static Vector2 GetRelativeAnchoredPosition(Vector3 worldPosition, FrameContext frameContext)
    {
        var anchored = MapUtils.GetAnchoredPositionFromWorldPos2d(
            frameContext.WorldToAnchoredSpace,
            new float2(worldPosition.x, worldPosition.z));
        return new Vector2(anchored.x, anchored.y) - frameContext.AnchoredOrigin;
    }

    private static void RenderMarkers(OverlaySurfaceState surface)
    {
        for (var i = 0; i < MarkerRequests.Count; i++)
        {
            var marker = GetMarker(surface, i);
            ApplyMarker(surface, marker, MarkerRequests[i]);
        }

        HideUnusedMarkers(surface, MarkerRequests.Count);
    }

    private static bool IsRenderablePlayer(Entity entity)
    {
        return IsRenderableEntity(entity) && entity.Has<PlayerCharacter>();
    }

    private static bool IsRenderableEntity(Entity entity)
    {
        return entity.Exists() &&
               !entity.IsDisabled() &&
               entity.IsAlive() &&
               entity.Has<LocalToWorld>();
    }

    private static Image GetMarker(OverlaySurfaceState surface, int index)
    {
        while (surface.MarkerPool.Count <= index)
        {
            var marker = Object.Instantiate(surface.MarkerTemplate!, surface.MarkerContainer, false);
            marker.raycastTarget = false;
            marker.rectTransform.localScale = Vector3.one;
            marker.gameObject.SetActive(false);
            surface.MarkerPool.Add(marker);
        }

        return surface.MarkerPool[index];
    }

    private static void ApplyMarker(OverlaySurfaceState surface, Image marker, MarkerRequest markerRequest)
    {
        marker.name = markerRequest.Name;
        marker.rectTransform.anchoredPosition = markerRequest.AnchoredPosition;
        marker.rectTransform.localRotation = markerRequest.Rotation;
        marker.rectTransform.sizeDelta = markerRequest.SizeOverride ?? surface.DefaultMarkerSize ?? surface.TemplateSize;
        marker.canvasRenderer.SetAlpha(markerRequest.ShowIcon ? 1f : 0f);
        marker.enabled = markerRequest.ShowIcon;
        marker.color = markerRequest.Color;
        marker.gameObject.SetActive(true);
        ApplyMarkerLabel(marker, markerRequest.Label, markerRequest.Color);
    }

    private static void ApplyMarkerLabel(Image marker, string? label, Color color)
    {
        var labelText = marker.GetComponentInChildren<TextMeshProUGUI>(true);
        if (string.IsNullOrEmpty(label))
        {
            if (labelText != null) labelText.gameObject.SetActive(false);
            return;
        }

        if (labelText == null)
        {
            var go = new GameObject("ESP_Label");
            go.transform.SetParent(marker.transform, false);
            labelText = go.AddComponent<TextMeshProUGUI>();
            labelText.raycastTarget = false;
            labelText.enableWordWrapping = false;
            labelText.overflowMode = TextOverflowModes.Overflow;
            labelText.alignment = TextAlignmentOptions.Center;
            var rt = labelText.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -2f);
            rt.sizeDelta = new Vector2(80f, 16f);
        }

        labelText.gameObject.SetActive(true);
        labelText.text = label;
        labelText.color = color;
        labelText.fontSize = 11f;
    }

    private static void HideUnusedMarkers(OverlaySurfaceState surface, int activeCount)
    {
        for (var i = activeCount; i < surface.MarkerPool.Count; i++)
        {
            var marker = surface.MarkerPool[i];
            if (marker != null) marker.gameObject.SetActive(false);
        }
    }

    private static void HideAllMarkers(OverlaySurfaceState surface)
    {
        HideUnusedMarkers(surface, 0);
    }

    private static void ClearMarkers(OverlaySurfaceState surface)
    {
        for (var i = 0; i < surface.MarkerPool.Count; i++)
        {
            var marker = surface.MarkerPool[i];
            if (marker != null) Object.Destroy(marker.gameObject);
        }

        surface.MarkerPool.Clear();
    }

    private static void ResetSurface(OverlaySurfaceState surface)
    {
        ClearMarkers(surface);
        surface.Owner = null;
        surface.MarkerContainer = null;
        surface.MarkerTemplate = null;
        surface.TemplateSize = default;
    }
}
