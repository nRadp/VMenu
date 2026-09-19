using ExtrasensoryPerception.API;
using ExtrasensoryPerception.Utils;
using HarmonyLib;
using ProjectM;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ExtrasensoryPerception.Patches;

/// <summary>
/// Overrides the top-down camera zoom limits once when entering a game so the
/// player can zoom out (or in) further than the game's default settings allow.
///
/// The game stores zoom/pitch limits in a <see cref="ZoomSettings"/> struct inside
/// the <see cref="TopdownCamera"/> ECS component. <c>TopdownCameraSystem.CameraUpdateJob</c>
/// reads those values (plus any <c>ZoomModifier</c> layers) to clamp the zoom level.
///
/// By writing our desired <c>MaxZoom</c> / <c>MinZoom</c> into the component just
/// before the system runs, we effectively override the hard cap without touching
/// any native code. The same is done for <c>TopdownCameraState.ZoomSettings</c>
/// which is the live copy the camera lerp evaluates against.
/// </summary>
[HarmonyPatch(typeof(TopdownCameraSystem), nameof(TopdownCameraSystem.OnUpdate))]
public static class CameraZoomPatch
{
    private static EntityQuery? _query;
    private static World? _queryWorld;
    private static bool _loggedDefaults;
    private static bool _zoomApplied;
    private static bool _hasSavedDefaults;
    private static ZoomSettings _originalStandardZoom;
    private static ZoomSettings _originalBuildModeZoom;
    private static ZoomSettings _originalStateZoom;

    internal static void ResetState()
    {
        _query = null;
        _queryWorld = null;
        _loggedDefaults = false;
        _zoomApplied = false;
        _hasSavedDefaults = false;
    }

    [HarmonyPrefix]
    static void Prefix()
    {
        if (!Config.Camera.ExtendedZoom.Enabled)
        {
            if (_zoomApplied && _hasSavedDefaults && _query != null)
                RestoreDefaults();

            _zoomApplied = false;
            return;
        }

        if (_zoomApplied) return;

        var em = VWorld.EntityManager;

        // Rebuild the query if the world was recreated (disconnect/reconnect).
        if (_query == null || _queryWorld != VWorld.Game)
        {
            _queryWorld = VWorld.Game;
            _loggedDefaults = false;
            _zoomApplied = false;
            _hasSavedDefaults = false;
            _query = em.CreateEntityQuery(
                ComponentType.ReadWrite<TopdownCamera>(),
                ComponentType.ReadWrite<TopdownCameraState>()
            );
        }

        var entities = _query.Value.ToEntityArray(Allocator.Temp);
        try
        {
            var maxZoom = Config.Camera.MaxZoomDistance.Value;
            var minZoom = Config.Camera.MinZoomDistance.Value;

            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];

                var cam = em.GetComponentData<TopdownCamera>(entity);

                // Log the game's original values once before we touch anything.
                if (!_loggedDefaults)
                {
                    _loggedDefaults = true;
                    Plugin.Logger.LogInfo(
                        $"[CameraZoom] Game defaults - " +
                        $"Standard: MinZoom={cam.StandardZoomSettings.MinZoom}, MaxZoom={cam.StandardZoomSettings.MaxZoom}, " +
                        $"MinPitch={cam.StandardZoomSettings.MinPitch}, MaxPitch={cam.StandardZoomSettings.MaxPitch}, " +
                        $"ZoomDistance={cam.StandardZoomDistance} | " +
                        $"BuildMode: MinZoom={cam.BuildModeZoomSettings.MinZoom}, MaxZoom={cam.BuildModeZoomSettings.MaxZoom}, " +
                        $"MinPitch={cam.BuildModeZoomSettings.MinPitch}, MaxPitch={cam.BuildModeZoomSettings.MaxPitch}, " +
                        $"ZoomDistance={cam.BuildModeZoomDistance}, ZoomSpeed={cam.ZoomSpeed}");
                }

                if (!_hasSavedDefaults)
                {
                    var stateBefore = em.GetComponentData<TopdownCameraState>(entity);
                    _originalStandardZoom = cam.StandardZoomSettings;
                    _originalBuildModeZoom = cam.BuildModeZoomSettings;
                    _originalStateZoom = stateBefore.ZoomSettings;
                    _hasSavedDefaults = true;
                }

                // --- Override the TopdownCamera blueprint values ---
                cam.StandardZoomSettings.MaxZoom = maxZoom;
                cam.StandardZoomSettings.MinZoom = minZoom;
                cam.BuildModeZoomSettings.MaxZoom = maxZoom;
                cam.BuildModeZoomSettings.MinZoom = minZoom;

                em.SetComponentData(entity, cam);

                // --- Override the live state copy so the current frame respects it ---
                var state = em.GetComponentData<TopdownCameraState>(entity);

                state.ZoomSettings.MaxZoom = maxZoom;
                state.ZoomSettings.MinZoom = minZoom;

                em.SetComponentData(entity, state);
            }

            if (entities.Length > 0)
                _zoomApplied = true;
        }
        finally
        {
            entities.Dispose();
        }
    }

    private static void RestoreDefaults()
    {
        var em = VWorld.EntityManager;
        var entities = _query!.Value.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];

                var cam = em.GetComponentData<TopdownCamera>(entity);
                cam.StandardZoomSettings = _originalStandardZoom;
                cam.BuildModeZoomSettings = _originalBuildModeZoom;
                cam.StandardZoomDistance = Mathf.Clamp(
                    cam.StandardZoomDistance,
                    _originalStandardZoom.MinZoom,
                    _originalStandardZoom.MaxZoom);
                cam.BuildModeZoomDistance = Mathf.Clamp(
                    cam.BuildModeZoomDistance,
                    _originalBuildModeZoom.MinZoom,
                    _originalBuildModeZoom.MaxZoom);
                em.SetComponentData(entity, cam);

                var state = em.GetComponentData<TopdownCameraState>(entity);
                state.ZoomSettings = _originalStateZoom;
                ClampLerpZoom(ref state.Current, _originalStateZoom);
                ClampLerpZoom(ref state.Target, _originalStateZoom);
                ClampLerpZoom(ref state.LastTarget, _originalStateZoom);
                em.SetComponentData(entity, state);
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    private static void ClampLerpZoom(ref TopdownCameraState.LerpVariables lerp, ZoomSettings settings)
    {
        lerp.Zoom = Mathf.Clamp(lerp.Zoom, settings.MinZoom, settings.MaxZoom);
    }
}
