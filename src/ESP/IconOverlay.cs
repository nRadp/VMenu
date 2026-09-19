using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ExtrasensoryPerception.ESP;

/// <summary>
/// Pooled UI <see cref="Image"/> drawer on the ESP overlay canvas. Used for ability icons
/// so we render live <see cref="Sprite"/> references (HDRP-safe) instead of GUI texture copies.
/// </summary>
internal static class IconOverlay
{
    private static bool _initialized;
    private static Canvas? _canvas;
    private static readonly Queue<Image> Pool = new();
    private static readonly List<Image> Active = [];
    private const int PoolSize = 64;

    internal static void BeginFrame()
    {
        for (var i = 0; i < Active.Count; i++)
        {
            var img = Active[i];
            if (img != null)
            {
                img.gameObject.SetActive(false);
                Pool.Enqueue(img);
            }
        }
        Active.Clear();
    }

    internal static void Draw(Vector2 screenPosition, Vector2 size, Sprite sprite, Color tint)
    {
        if (sprite == null) return;
        Initialize();

        var image = GetPooled();
        if (image.sprite != sprite) image.sprite = sprite;
        if (image.color != tint) image.color = tint;

        var rt = image.rectTransform;
        rt.anchoredPosition = new Vector2(screenPosition.x - Screen.width / 2f, Screen.height / 2f - screenPosition.y);
        rt.sizeDelta = size;
        rt.localEulerAngles = Vector3.zero;
    }

    private static void Initialize()
    {
        if (_initialized && _canvas) return;

        var canvasGo = new GameObject("ESPIconOverlay");
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Below TMP text overlay (1000) so cooldown timers stay readable on top.
        _canvas.sortingOrder = 999;
        _canvas.pixelPerfect = false;
        Object.DontDestroyOnLoad(canvasGo);

        // CanvasScaler not required — we position in screen pixels via anchoredPosition.
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        for (var i = 0; i < PoolSize; i++)
            CreatePooled();

        _initialized = true;
    }

    private static Image CreatePooled()
    {
        var go = new GameObject("PooledIcon");
        go.transform.SetParent(_canvas!.transform, false);
        go.SetActive(false);

        var image = go.AddComponent<Image>();
        image.raycastTarget = false;
        image.maskable = false;
        image.preserveAspect = true;
        image.type = Image.Type.Simple;

        var rt = image.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localEulerAngles = Vector3.zero;

        Pool.Enqueue(image);
        return image;
    }

    private static Image GetPooled()
    {
        Image image;
        if (Pool.Count > 0)
            image = Pool.Dequeue();
        else
        {
            CreatePooled();
            image = Pool.Dequeue();
        }

        image.gameObject.SetActive(true);
        Active.Add(image);
        return image;
    }
}
