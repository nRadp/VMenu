using System;
using HarmonyLib;
using UnityEngine;

namespace ExtrasensoryPerception.Patches;

// V Rising hides + center-locks the OS cursor while RMB is held (free-look).
// We can't have a real OS cursor that's both visible and stationary while the
// user moves the mouse (the OS draws it asynchronously, so any per-frame snap
// produces jitter). Instead: let the game's normal RMB handling run (OS cursor
// stays hidden + locked, no jitter, camera input unchanged) and paint our own
// cursor texture via OnGUI at the screen position recorded on RMB-down.
//
// The cursor texture is captured from CursorController.Register(...) so it is
// pixel-identical to the game's normal Game_Normal cursor.

// Capture the texture each time Unity's cursor changes. CursorController.Set
// calls this for every cursor type transition, so the most recent non-null
// texture is the one currently being shown by the game (or the one the game
// would show if it weren't hiding the cursor for RMB free-look).
[HarmonyPatch(typeof(Cursor), nameof(Cursor.SetCursor),
    new[] { typeof(Texture2D), typeof(Vector2), typeof(CursorMode) })]
internal static class UnityCursorSetCursorPatch
{
    [HarmonyPrefix]
    private static void Prefix(Texture2D texture, Vector2 hotspot, CursorMode cursorMode)
    {
        if (texture != null)
            CursorVisibilityKeeper.SetTexture(texture, hotspot);
    }
}

public class CursorVisibilityKeeper : MonoBehaviour
{
    private Vector2 _lockScreenPos;
    private bool _locked;

    private static Texture2D? _cursorTexture;
    private static Vector2 _cursorHotspot;

    public static void SetTexture(Texture2D tex, Vector2 hotspot)
    {
        // Avoid spamming logs when the same texture is set repeatedly.
        var changed = !ReferenceEquals(_cursorTexture, tex) || _cursorHotspot != hotspot;
        _cursorTexture = tex;
        _cursorHotspot = hotspot;
        if (changed)
            Plugin.Logger.LogInfo($"[CursorVisibilityKeeper] Captured cursor texture {tex.width}x{tex.height}, hotspot={hotspot}");
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            _lockScreenPos = Input.mousePosition;
            _locked = true;
        }
        else if (!Input.GetMouseButton(1))
        {
            _locked = false;
        }
    }

    private void OnGUI()
    {
        if (!_locked) return;
        if (Event.current.type != EventType.Repaint) return;

        var tex = _cursorTexture != null ? _cursorTexture : GetFallbackCursor();
        if (tex == null) return;

        // Input.mousePosition uses bottom-left origin; GUI uses top-left.
        var x = _lockScreenPos.x - _cursorHotspot.x;
        var y = (Screen.height - _lockScreenPos.y) - _cursorHotspot.y;
        GUI.DrawTexture(new Rect(x, y, tex.width, tex.height), tex);
    }

    private static Texture2D? _fallbackCursor;
    private static Texture2D GetFallbackCursor()
    {
        if (_fallbackCursor != null) return _fallbackCursor;
        const int w = 16, h = 16;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                t.SetPixel(x, y, clear);

        for (int yTop = 0; yTop < h; yTop++)
        {
            int rowEnd = Mathf.Min(w, h - yTop);
            for (int x = 0; x < rowEnd; x++)
            {
                bool border = x == 0 || x == rowEnd - 1 || yTop == 0;
                t.SetPixel(x, h - 1 - yTop, border ? Color.black : Color.white);
            }
        }
        t.Apply();
        _fallbackCursor = t;
        return t;
    }
}
