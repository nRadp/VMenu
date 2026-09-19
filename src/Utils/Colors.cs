using System.Collections.Generic;
using UnityEngine;

namespace ExtrasensoryPerception.Utils;

internal class ColorOption(string name, Color value)
{
    public string Name { get; } = name;
    public Color Value { get; } = value;

    public override string ToString()
    {
        return Name;
    }
}

internal static class ColorOptions
{
    internal static readonly List<ColorOption> AllColors =
    [
        new("White", Color.white),
        new("Red", Color.red),
        new("Green", Color.green),
        new("Blue", Color.blue),
        new("Cyan", Color.cyan),
        new("Magenta", Color.magenta),
        new("Yellow", Color.yellow),
        new("Orange", new Color(1.0f, 0.5f, 0.0f)),
        new("Purple", new Color(0.5f, 0.0f, 0.5f)),
        new("Crimson", new Color(0.863f, 0.078f, 0.235f)),
        new("Cognac", new Color(0.514f, 0.262f, 0.2f)),
        new("Black", Color.black),
        new("Lime", new Color(0.5f, 1.0f, 0.0f)),
        new("Teal", new Color(0.0f, 0.5f, 0.5f)),
        new("Maroon", new Color(0.5f, 0.0f, 0.0f)),
        new("Olive", new Color(0.5f, 0.5f, 0.0f)),
        new("Pink", new Color(1.0f, 0.75f, 0.8f)),
        new("Coral", new Color(1.0f, 0.5f, 0.31f)),
        new("Indigo", new Color(0.29f, 0.0f, 0.51f)),
        new("Silver", new Color(0.75f, 0.75f, 0.75f)),
        new("Violet", new Color(0.93f, 0.51f, 0.93f))
    ];

    internal static Color GetColor(int index)
    {
        if (index >= 0 && index < AllColors.Count) return AllColors[index].Value;
        return Color.white;
    }

    internal static string GetColorName(int colorIndex)
    {
        if (colorIndex >= 0 && colorIndex < AllColors.Count) return AllColors[colorIndex].Name;
        return "Unknown";
    }
}