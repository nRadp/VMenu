using System;
using ExtrasensoryPerception.Utils;
using UnityEngine;

namespace ExtrasensoryPerception.UI;

/// <summary>
/// Floating popup overlays (dropdowns, color pickers).
/// Rendered after the main window so they appear on top.
/// Click outside closes them.
/// </summary>
internal static class Popups
{
    private enum PopupKind { None, Dropdown, ColorPicker }

    private static PopupKind _kind = PopupKind.None;
    private static int _ownerId;
    private static Rect _rect;
    private static bool _justOpened;

    // Dropdown state
    private static string[] _options = [];
    private static int _currentIndex;
    private static Action<int>? _onSelectIndex;

    // Color picker state
    private static int _currentColorIndex;
    private static Action<int>? _onSelectColor;

    // Layout
    private const float DropdownItemHeight = 26f;
    private const float DropdownWidth = 160f;
    private const int ColorGridCols = 7;
    private const float SwatchSize = 24f;
    private const float SwatchSpacing = 4f;
    private const float ColorPickerPadding = 8f;

    internal static bool IsOpen => _kind != PopupKind.None;

    internal static void OpenDropdown(int ownerId, string[] options, int currentIndex, Action<int> onSelect, Vector2 screenAnchor)
    {
        // Toggle: click same owner closes it
        if (_kind == PopupKind.Dropdown && _ownerId == ownerId)
        {
            Close();
            return;
        }
        _kind = PopupKind.Dropdown;
        _ownerId = ownerId;
        _options = options;
        _currentIndex = currentIndex;
        _onSelectIndex = onSelect;
        var height = options.Length * DropdownItemHeight + 8f;
        _rect = ClampToScreen(new Rect(screenAnchor.x, screenAnchor.y, DropdownWidth, height));
        _justOpened = true;
    }

    internal static void OpenColorPicker(int ownerId, int currentColorIndex, Action<int> onSelect, Vector2 screenAnchor)
    {
        if (_kind == PopupKind.ColorPicker && _ownerId == ownerId)
        {
            Close();
            return;
        }
        _kind = PopupKind.ColorPicker;
        _ownerId = ownerId;
        _currentColorIndex = currentColorIndex;
        _onSelectColor = onSelect;

        var count = ColorOptions.AllColors.Count;
        var rows = Mathf.CeilToInt(count / (float)ColorGridCols);
        var w = ColorPickerPadding * 2 + ColorGridCols * SwatchSize + (ColorGridCols - 1) * SwatchSpacing;
        var h = ColorPickerPadding * 2 + rows * SwatchSize + (rows - 1) * SwatchSpacing + 22f; // +label
        _rect = ClampToScreen(new Rect(screenAnchor.x, screenAnchor.y, w, h));
        _justOpened = true;
    }

    internal static void Close()
    {
        _kind = PopupKind.None;
        _ownerId = 0;
        _onSelectIndex = null;
        _onSelectColor = null;
    }

    /// <summary>Render the active popup. Call AFTER all main windows in OnGUI.</summary>
    internal static void Draw(int windowId)
    {
        if (_kind == PopupKind.None) return;

        switch (_kind)
        {
            case PopupKind.Dropdown:
                _rect = GUI.Window(windowId, _rect, (GUI.WindowFunction)DrawDropdown, "", MenuTheme.PopupStyle);
                break;
            case PopupKind.ColorPicker:
                _rect = GUI.Window(windowId, _rect, (GUI.WindowFunction)DrawColorPicker, "", MenuTheme.PopupStyle);
                break;
        }

        // Eat clicks outside to close. GUI.Window consumed clicks inside it,
        // so an unconsumed MouseDown here means the user clicked elsewhere.
        if (!_justOpened && Event.current.type == EventType.MouseDown && !_rect.Contains(Event.current.mousePosition))
        {
            Close();
        }
        _justOpened = false;
    }

    private static void DrawDropdown(int id)
    {
        GUILayout.BeginVertical();
        for (var i = 0; i < _options.Length; i++)
        {
            var style = i == _currentIndex ? MenuTheme.DropdownItemActiveStyle : MenuTheme.DropdownItemStyle;
            if (GUILayout.Button(_options[i], style, GUILayout.Height(DropdownItemHeight - 2)))
            {
                _onSelectIndex?.Invoke(i);
                Close();
            }
        }
        GUILayout.EndVertical();
    }

    private static void DrawColorPicker(int id)
    {
        GUILayout.BeginVertical();
        GUILayout.Label("  " + ColorOptions.GetColorName(_currentColorIndex), MenuTheme.LabelStyle);

        var count = ColorOptions.AllColors.Count;
        for (var row = 0; row * ColorGridCols < count; row++)
        {
            GUILayout.BeginHorizontal();
            for (var col = 0; col < ColorGridCols; col++)
            {
                var idx = row * ColorGridCols + col;
                if (idx >= count)
                {
                    GUILayout.Space(SwatchSize + SwatchSpacing);
                    continue;
                }
                var style = idx == _currentColorIndex
                    ? MenuTheme.GetSwatchSelectedStyle(idx)
                    : MenuTheme.GetSwatchStyle(idx);
                if (GUILayout.Button(new GUIContent("", null, ColorOptions.GetColorName(idx)),
                        style, GUILayout.Width(SwatchSize), GUILayout.Height(SwatchSize)))
                {
                    _onSelectColor?.Invoke(idx);
                    Close();
                }
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }

    private static Rect ClampToScreen(Rect r)
    {
        if (r.x + r.width > Screen.width) r.x = Screen.width - r.width - 4;
        if (r.y + r.height > Screen.height) r.y = Screen.height - r.height - 4;
        if (r.x < 0) r.x = 4;
        if (r.y < 0) r.y = 4;
        return r;
    }
}
