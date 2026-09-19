using System;
using System.Collections.Generic;
using ExtrasensoryPerception.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ExtrasensoryPerception.ESP;

public static class TMP
{
    internal static bool IsActive => Config.ESP.UseTMP.Enabled;

    private static bool _initialized;
    private static Canvas? _overlayCanvas;
    private static readonly Queue<TextMeshProUGUI> TextPool = new();
    private static readonly List<TextMeshProUGUI> ActiveTexts = [];
    private const int PoolSize = 100; // Pre-allocate pool
    private static TMP_FontAsset? _gameFont;
    private static Material? _gameFontMaterial;
    
    private static void Initialize()
    {
        if (_initialized) return;
        if (!_overlayCanvas)
        {
            var canvas = new GameObject("ESPOverlay");
            _overlayCanvas = canvas.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 1000;
            _overlayCanvas.pixelPerfect = false; // Better performance
            
            Object.DontDestroyOnLoad(canvas);
            
            ExtractGameFontAssets();
            
            // Pre-populate the pool
            for (var i = 0; i < PoolSize; i++) CreatePooledText();
        }
        _initialized = true;
    }
    
    private static void ExtractGameFontAssets()
    {
        var hudCanvas = GameObject.Find("HUDCanvas(Clone)");
        if (!hudCanvas) return;
        
        var characterHuDsCanvas = hudCanvas.transform.Find("CharacterHUDsCanvas");
        if (!characterHuDsCanvas) return;
        
        TextMeshProUGUI[] tmpTexts = characterHuDsCanvas?.GetComponentsInChildren<TextMeshProUGUI>(true) ?? throw new InvalidOperationException();
        if (tmpTexts.Length <= 0) return;
        var first = tmpTexts[0];
        _gameFont = first.font;
        _gameFontMaterial = first.fontMaterial;
    }
    
    private static TextMeshProUGUI CreatePooledText()
    {
        var text = new GameObject("PooledText");
        text.transform.SetParent(_overlayCanvas?.transform, false);
        text.SetActive(false);
        
        var textComponent = text.AddComponent<TextMeshProUGUI>();
        
        textComponent.raycastTarget = false;
        textComponent.maskable = false;
        textComponent.enableAutoSizing = false;
        textComponent.overflowMode = TextOverflowModes.Overflow;
        textComponent.enableWordWrapping = false;
        
        if (_gameFont) textComponent.font = _gameFont;
        if (_gameFontMaterial) textComponent.fontMaterial = _gameFontMaterial;
        
        // In an attempt to avoid reallocations
        textComponent.SetVerticesDirty();
        
        TextPool.Enqueue(textComponent);
        return textComponent;
    }
    
    private static TextMeshProUGUI GetPooledText()
    {
        TextMeshProUGUI text;
        
        if (TextPool.Count > 0)
        {
            text = TextPool.Dequeue();
        }
        else
        {
            // Pool exhausted, create new one
            text = CreatePooledText();
            TextPool.Dequeue(); // Remove it from the pool since we're using it
        }
        
        text.gameObject.SetActive(true);
        ActiveTexts.Add(text);
        return text;
    }
    
    public static void DrawString(Vector2 position, string label, Color color, float fontSize = 12f, bool centered = true, bool forceOutline = false)
    {
        Initialize();
        
        var textComponent = GetPooledText();
        
        if (textComponent.text != label) textComponent.text = label;
        if (!Mathf.Approximately(textComponent.fontSize, fontSize)) textComponent.fontSize = fontSize;
        if (textComponent.color != color) textComponent.color = color;
        
        // Forced outlines render a touch thicker than the default global outline so callers
        // that opt in (e.g. cooldown stack) actually look distinct, not just match the global.
        if (forceOutline)
        {
            textComponent.outlineWidth = 0.3f;
            textComponent.outlineColor = Color.black;
        }
        else if (Config.ESP.Outlines.Enabled)
        {
            textComponent.outlineWidth = 0.2f;
            textComponent.outlineColor = Color.black;
        }
        else textComponent.outlineWidth = 0f;
        
        
        var rectTransform = textComponent.rectTransform;
        // The Canvas starts from the middle and not top-left.
        rectTransform.anchoredPosition = new Vector2(position.x - Screen.width / 2f, Screen.height / 2f - position.y);
        rectTransform.localEulerAngles = Vector3.zero;
        if (centered) textComponent.alignment = TextAlignmentOptions.Center;
        rectTransform.sizeDelta = textComponent.GetPreferredValues();
    }
    
    public static void BeginFrame()
    {
        foreach (var text in ActiveTexts)
        {
            text.gameObject.SetActive(false);
            TextPool.Enqueue(text);
        }
        ActiveTexts.Clear();
    }
}

public static class Primitives
{
    internal static bool DrawOutlines => Config.ESP.Outlines.Enabled;
    // richText = true so <color=...> tags emitted by features like the enemy cooldown tracker
    // render properly under the IMGUI fallback. TMP honors rich text natively without a flag.
    // OutlineStyle also has it on so tags don't render literally on the outline pass; the side
    // effect is the outline picks up the inline color (a colored halo) instead of being pure
    // black on tagged spans — acceptable, and arguably makes warnings pop more.
    private static readonly GUIStyle StringStyle = new()
    {
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        richText = true
    };
    private static readonly GUIStyle OutlineStyle = new()
    {
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        richText = true
    };
    private static readonly GUIContent SharedContent = new();
    
    private static readonly Dictionary<int, Vector2[]> OutlineOffsets = new()
    {
        [0] = [new Vector2(0, 0)],
        [1] = [new Vector2(-1, -1), new Vector2(1, 1)],
        [2] = [new Vector2(0, -1), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, 1)],
        [3] = [new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(-1, 1), new Vector2(0, 1), new Vector2(1, 1)]
    };
    
    private static readonly GUIContent OutlineContent = new();
    private static readonly System.Text.StringBuilder StripTagsBuilder = new();

    public static void DrawString(Vector2 position, string label, Color color, int fontsize = 12, bool centered = true, bool forceOutline = false)
    {
        SharedContent.text = label;
        StringStyle.fontSize = fontsize;
        StringStyle.normal.textColor = color;

        var size = StringStyle.CalcSize(SharedContent);
        position = centered ? position - size / 2f : position;

        var drawOutline = DrawOutlines || forceOutline;
        if (drawOutline)
        {
            OutlineStyle.fontSize = fontsize;
            OutlineStyle.normal.textColor = Color.black;

            // OutlineStyle has richText=true so embedded <color> tags don't render literally,
            // but that means the inline color overrides our black outline color — a red label
            // ends up with a red outline, which kills contrast. Strip tags for the outline
            // pass so the outline always renders in solid black. Width matches because the
            // visible glyph sequence is identical in both passes.
            OutlineContent.text = label.IndexOf('<') >= 0 ? StripRichTextTags(label) : label;

            // Force a chunky 8-directional outline when the caller opted in, regardless of
            // the user's global outline-quality setting; otherwise honor it.
            var quality = forceOutline ? 3 : Config.ESP.Outlines.Option;
            if (OutlineOffsets.TryGetValue(quality, out var offsets))
            {
                foreach (var offset in offsets)
                {
                    GUI.Label(new Rect(position + offset, size), OutlineContent, OutlineStyle);
                }
            }
        }

        GUI.Label(new Rect(position, size), SharedContent, StringStyle);
    }

    /// <summary>
    /// Strip everything between <c>&lt;</c> and <c>&gt;</c> from a rich-text string so it can
    /// be drawn through a style whose tag handling we don't want — specifically the IMGUI
    /// outline pass, which shouldn't honor inline <c>&lt;color&gt;</c> tags. Allocates only
    /// when the input actually contains a tag (the caller's <c>IndexOf('&lt;')</c> guard).
    /// </summary>
    private static string StripRichTextTags(string s)
    {
        StripTagsBuilder.Clear();
        var depth = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '<') depth++;
            else if (c == '>' && depth > 0) depth--;
            else if (depth == 0) StripTagsBuilder.Append(c);
        }
        return StripTagsBuilder.ToString();
    }
    
    public static void DrawBox(Vector2 position, Vector2 size, Color color, float thickness = 1f)
    {
        GLBegin(GL.QUADS);

        if (DrawOutlines) SetupBox(position, size, Color.black, thickness + 2);
        SetupBox(position, size, color, thickness);

        GLEnd();
    }
    
    public static void DrawCircle(Vector2 position, float radius, Color color, int vertices = 128)
    {
        GLBegin(GL.QUADS);

        if (DrawOutlines) SetupCircle(position, radius, Color.black, vertices);
        SetupCircle(position, radius, color, vertices);

        GLEnd();
    }
    
    public static void DrawX(Vector2 position, float size, Color color, float thickness = 1f)
    {
        GLBegin(GL.QUADS);
        SetupX(position, size, color, thickness);
        GLEnd();
    }
    
    internal static void SetupBox(Vector2 position, Vector2 size, Color color, float thickness = 1f)
    {
        GL.Color(color);

        // Using only half, so it expands (equally) around the position
        thickness /= 2;
        size /= 2;
        
        var topLeft = position - size;
        var bottomRight = position + size;

        switch (Config.ESP.Boxes.Option)
        {
            case 1:
                var cornerLength = Mathf.Min(size.x, size.y) / 2; // 1/4 of original size.x/size.y
                if (color == Color.black) cornerLength += 1;
        
                // Top-Left Corner 
                SetupLine(new Vector2(topLeft.x - thickness, topLeft.y), new Vector2(topLeft.x + cornerLength, topLeft.y), thickness);
                SetupLine(new Vector2(topLeft.x, topLeft.y - thickness), new Vector2(topLeft.x, topLeft.y + cornerLength), thickness);
                // Top-Right Corner
                SetupLine(new Vector2(bottomRight.x - cornerLength, topLeft.y), new Vector2(bottomRight.x + thickness, topLeft.y), thickness);
                SetupLine(new Vector2(bottomRight.x, topLeft.y - thickness), new Vector2(bottomRight.x, topLeft.y + cornerLength), thickness);
                // Bottom-Left Corner
                SetupLine(new Vector2(topLeft.x - thickness, bottomRight.y), new Vector2(topLeft.x + cornerLength, bottomRight.y), thickness);
                SetupLine(new Vector2(topLeft.x, bottomRight.y - cornerLength), new Vector2(topLeft.x, bottomRight.y + thickness), thickness);
                // Bottom-Right Corner
                SetupLine(new Vector2(bottomRight.x - cornerLength, bottomRight.y), new Vector2(bottomRight.x + thickness, bottomRight.y), thickness);
                SetupLine(new Vector2(bottomRight.x, bottomRight.y - cornerLength), new Vector2(bottomRight.x, bottomRight.y + thickness), thickness);
                break;
        
            default:
                // Full box
                SetupLine(new Vector2(topLeft.x - thickness, topLeft.y), new Vector2(bottomRight.x + thickness, topLeft.y), thickness);         // Top
                SetupLine(new Vector2(bottomRight.x, topLeft.y - thickness), new Vector2(bottomRight.x, bottomRight.y + thickness), thickness); // Right 
                SetupLine(new Vector2(topLeft.x, topLeft.y - thickness), new Vector2(topLeft.x, bottomRight.y + thickness), thickness);         // Left
                SetupLine(new Vector2(topLeft.x - thickness, bottomRight.y), new Vector2(bottomRight.x + thickness, bottomRight.y), thickness); // Bottom
                break;
        }
    }
    
    /// <summary>
    /// Draw a solid filled rectangle centered at <paramref name="position"/>.
    /// Emits 4 GL.QUADS vertices — must be called between GLBegin/GLEnd.
    /// </summary>
    internal static void SetupFilledRect(Vector2 position, Vector2 size, Color color)
    {
        GL.Color(color);
        var half = size / 2f;
        GL.Vertex3(position.x - half.x, position.y - half.y, 0);
        GL.Vertex3(position.x + half.x, position.y - half.y, 0);
        GL.Vertex3(position.x + half.x, position.y + half.y, 0);
        GL.Vertex3(position.x - half.x, position.y + half.y, 0);
    }

    internal static void SetupCircle(Vector2 center, float radius, Color color, int vertices = 128)
    {
        GL.Color(color);
        
        var angleStep = 2f * Mathf.PI / vertices;
    
        for (var i = 0; i < vertices; i++)
        {
            var angle1 = i * angleStep;
            var angle2 = (i + 1) * angleStep;
        
            var point1 = center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * radius;
            var point2 = center + new Vector2(Mathf.Cos(angle2), Mathf.Sin(angle2)) * radius;
            
            var direction = (point2 - point1).normalized;
            var extendedPoint1 = point1 - direction * 0.5f;
            var extendedPoint2 = point2 + direction * 0.5f;
        
            SetupLine(extendedPoint1, extendedPoint2, 1f);
        }
    }
    
    private static void SetupX(Vector2 center, float size, Color color, float thickness)
    {
        GL.Color(color);

        size /= 2;
    
        // TL to BR
        var topLeft = center + new Vector2(-size, size);
        var bottomRight = center + new Vector2(size, -size);
        SetupLine(topLeft, bottomRight, thickness);
    
        // TR to BL
        var topRight = center + new Vector2(size, size);
        var bottomLeft = center + new Vector2(-size, -size);
        SetupLine(topRight, bottomLeft, thickness);
    }
    
    private static void SetupLine(Vector2 start, Vector2 end, float thickness)
    {
        // Calculate direction and perpendicular vectors
        var direction = end - start;
        var length = direction.magnitude;
    
        if (length > 0)
        {
            direction.Normalize();
            var perpendicular = new Vector2(-direction.y, direction.x) * thickness;
        
            // Draw the quad
            GL.Vertex3(start.x - perpendicular.x, start.y - perpendicular.y, 0);
            GL.Vertex3(end.x - perpendicular.x, end.y - perpendicular.y, 0);
            GL.Vertex3(end.x + perpendicular.x, end.y + perpendicular.y, 0);
            GL.Vertex3(start.x + perpendicular.x, start.y + perpendicular.y, 0);
        }
    }
    
    internal static void GLBegin(int mode)
    {
        CreateLineMaterial();
        GL.PushMatrix();
        GL.LoadPixelMatrix();
        GL.Begin(mode);
    }
    
    internal static void GLEnd()
    {
        GL.End();
        GL.PopMatrix();
    }
    
    private static Material? _lineMaterial;
    private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
    private static readonly int Cull = Shader.PropertyToID("_Cull");
    private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
    private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");

    private static void CreateLineMaterial()
    {
        if (!_lineMaterial)
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            _lineMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _lineMaterial.SetInt(SrcBlend, (int)BlendMode.SrcAlpha);
            _lineMaterial.SetInt(DstBlend, (int)BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt(Cull, (int)CullMode.Off);
            _lineMaterial.SetInt(ZWrite, 0);
        }
        _lineMaterial?.SetPass(0);
    }
}
