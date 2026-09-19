using System;
using ExtrasensoryPerception.ESP;
using ExtrasensoryPerception.Utils;
using UnityEngine;

namespace ExtrasensoryPerception.UI;

public class Overlay : MonoBehaviour
{
    private void OnGUI()
    {
        // Making sure we're drawing once per frame
        if (Event.current.type != EventType.Repaint) return;
        TMP.BeginFrame();
        IconOverlay.BeginFrame();
        if (!Config.ModToggle.Value) return;
        
        if (Plugin.IsInGame && !Plugin.IsMenuOpen) RenderQueue.DrawQueued();
        RenderQueue.Clear();
    }

    private void Update()
    {
        if (!Config.ModToggle.Value || !Plugin.IsInGame) return;

        try
        {
            Logic.ProcessAllEntities();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Overlay.LateUpdate() failed with: {e}");
            throw;
        }
    }
}