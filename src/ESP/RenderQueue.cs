using System.Collections.Generic;
using UnityEngine;

namespace ExtrasensoryPerception.ESP;

public static class RenderQueue
{
    private readonly struct QString(Vector2 position, string text, Color color, float fontSize = 12, bool centered = true, bool forceOutline = false)
    {
        public void Draw()
        {
            if (TMP.IsActive) TMP.DrawString(position, text, color, fontSize, centered, forceOutline);
            else Primitives.DrawString(position, text, color, (int)fontSize, centered, forceOutline);
        }
    }
    
    private readonly struct QBox(Vector2 position, Vector2 size, Color color, float thickness = 1f)
    {
        public void Draw() => Primitives.SetupBox(position, size, color, thickness);
    }
    
    private readonly struct QCircle(Vector2 position, float radius, Color color, int vertices = 128)
    {
        public void Draw() => Primitives.SetupCircle(position, radius, color, vertices);
    }

    private readonly struct QFilledRect(Vector2 position, Vector2 size, Color color)
    {
        public void Draw() => Primitives.SetupFilledRect(position, size, color);
    }

    private readonly struct QIcon(Vector2 position, Vector2 size, Sprite sprite, Color tint)
    {
        public void Draw() => IconOverlay.Draw(position, size, sprite, tint);
    }
    
    private static readonly List<QString> StringQueue = [];
    private static readonly List<QBox> BoxQueue = [];
    private static readonly List<QCircle> CircleQueue = [];
    private static readonly List<QFilledRect> FilledRectQueue = [];
    private static readonly List<QIcon> IconQueue = [];
    
    // Strings
    // forceOutline: draw a black outline regardless of Config.ESP.Outlines, used to make a
    // specific label (e.g. enemy cooldown stack) stand out without forcing every label to.
    public static void String(Vector2 position, string text, Color color, float fontSize = 12, bool centered = true, bool forceOutline = false)
    {
        StringQueue.Add(new QString(position, text, color, fontSize, centered, forceOutline));
    }
    
    // Boxes
    public static void Box(Vector2 position, Vector2 size, Color color, float thickness = 1f)
    {
        if (Primitives.DrawOutlines)
            BoxQueue.Add(new QBox(position, size, Color.black, thickness + 2));
        BoxQueue.Add(new QBox(position, size, color, thickness));
    }
    
    public static void Circle(Vector2 position, float radius, Color color, int vertices = 64)
    {
        if (Primitives.DrawOutlines)
            CircleQueue.Add(new QCircle(position, radius, color, vertices));
        CircleQueue.Add(new QCircle(position, radius, color, vertices));
    }

    /// <summary>
    /// Draw a filled rectangle centered at <paramref name="position"/>.
    /// No outline — pips are too small for a separate background rect to look clean.
    /// </summary>
    public static void FilledRect(Vector2 position, Vector2 size, Color color)
    {
        FilledRectQueue.Add(new QFilledRect(position, size, color));
    }

    /// <summary>
    /// Draw an ability icon via the UI <see cref="IconOverlay"/> (live Sprite, HDRP-safe).
    /// </summary>
    public static void Icon(Vector2 position, Vector2 size, Sprite sprite, Color tint)
    {
        if (sprite == null) return;
        IconQueue.Add(new QIcon(position, size, sprite, tint));
    }

    internal static void Clear()
    {
        StringQueue.Clear();
        BoxQueue.Clear();
        CircleQueue.Clear();
        FilledRectQueue.Clear();
        IconQueue.Clear();
    }

    private static void DrawQuads()
    {
        Primitives.GLBegin(GL.QUADS);
        foreach (var item in FilledRectQueue) item.Draw();
        foreach (var item in BoxQueue) item.Draw();
        foreach (var item in CircleQueue) item.Draw();
        Primitives.GLEnd();
    }
    
    public static void DrawQueued()
    {
        foreach (var item in IconQueue) item.Draw();
        DrawQuads();
        foreach (var item in StringQueue) item.Draw();
    }
}
