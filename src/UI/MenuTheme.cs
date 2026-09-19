using System.Collections.Generic;
using ExtrasensoryPerception.Utils;
using UnityEngine;

namespace ExtrasensoryPerception.UI;

public static class MenuTheme
{
    private static readonly Dictionary<Color, Texture2D> Textures = new();
    private static readonly Dictionary<int, GUIStyle> SwatchStyles = new();
    private static readonly Dictionary<int, GUIStyle> SwatchSelectedStyles = new();

    internal static GUIStyle WindowStyle = new();
    internal static GUIStyle PopupStyle = new();
    internal static GUIStyle ButtonStyle = new();
    internal static GUIStyle SmallButtonStyle = new();
    internal static GUIStyle TabActiveStyle = new();
    internal static GUIStyle TabInactiveStyle = new();
    internal static GUIStyle SubTabActiveStyle = new();
    internal static GUIStyle SubTabInactiveStyle = new();
    internal static GUIStyle ToggleStyle = new();
    internal static GUIStyle ToggleOnStyle = new();
    internal static GUIStyle RadioStyle = new();
    internal static GUIStyle RadioOnStyle = new();
    internal static GUIStyle DropdownButtonStyle = new();
    internal static GUIStyle DropdownItemStyle = new();
    internal static GUIStyle DropdownItemActiveStyle = new();
    internal static GUIStyle BoxStyle = new();
    internal static GUIStyle HeaderStyle = new();
    internal static GUIStyle SubHeaderStyle = new();
    internal static GUIStyle HSliderStyle = new();
    internal static GUIStyle HSliderThumbStyle = new();
    internal static GUIStyle TooltipStyle = new();
    internal static GUIStyle LabelStyle = new();
    internal static GUIStyle ValueLabelStyle = new();
    internal static GUIStyle TextFieldStyle = new();

    // Shared palette
    internal static readonly Color OnColor = new(0.55f, 0.85f, 0.55f, 1f);    // soft green
    internal static readonly Color OffColor = new(0.65f, 0.65f, 0.65f, 1f);   // dim gray
    internal static readonly Color AccentColor = new(0.45f, 0.65f, 0.95f, 1f); // soft blue

    private static RectOffset Pad(int l, int r, int t, int b) =>
        new() { left = l, right = r, top = t, bottom = b };

    internal static void SetupDarkTheme()
    {
        var transparent = new Color(0f, 0f, 0f, 0f);
        var windowBg = new Color(0.08f, 0.08f, 0.10f, 0.95f);
        var popupBg = new Color(0.12f, 0.12f, 0.14f, 0.98f);
        var buttonBg = new Color(0.20f, 0.20f, 0.23f, 1f);
        var buttonHover = new Color(0.30f, 0.30f, 0.35f, 1f);
        var buttonActive = new Color(0.15f, 0.15f, 0.18f, 1f);
        var tabInactiveBg = new Color(0.12f, 0.12f, 0.14f, 1f);
        var tabActiveBg = new Color(0.22f, 0.30f, 0.42f, 1f);
        var tabHoverBg = new Color(0.18f, 0.18f, 0.22f, 1f);
        var headerBg = new Color(0.14f, 0.16f, 0.20f, 0.95f);
        var subHeaderBg = new Color(0.10f, 0.11f, 0.13f, 0.9f);
        var sliderTrack = new Color(0.18f, 0.18f, 0.20f, 1f);
        var sliderThumb = new Color(0.55f, 0.65f, 0.85f, 1f);
        var rowHover = new Color(1f, 1f, 1f, 0.05f);
        var dropdownItemActive = new Color(0.22f, 0.30f, 0.42f, 1f);
        var swatchBorder = new Color(1f, 1f, 1f, 0.3f);
        var swatchSelectedBorder = Color.white;
        var white = Color.white;
        var dimText = new Color(0.85f, 0.85f, 0.85f, 1f);

        // === WINDOW ===
        WindowStyle = GUI.skin.window;
        WindowStyle.padding = Pad(10, 10, 24, 10);
        var winBg = MakeTexture(windowBg);
        WindowStyle.normal.background = winBg;
        WindowStyle.onNormal.background = winBg;
        WindowStyle.hover.background = winBg;
        WindowStyle.onHover.background = winBg;
        WindowStyle.active.background = winBg;
        WindowStyle.onActive.background = winBg;
        WindowStyle.focused.background = winBg;
        WindowStyle.onFocused.background = winBg;
        // Keep title text the same color in every state (no focus/hover color shift).
        WindowStyle.normal.textColor = white;
        WindowStyle.onNormal.textColor = white;
        WindowStyle.hover.textColor = white;
        WindowStyle.onHover.textColor = white;
        WindowStyle.active.textColor = white;
        WindowStyle.onActive.textColor = white;
        WindowStyle.focused.textColor = white;
        WindowStyle.onFocused.textColor = white;
        WindowStyle.fontStyle = FontStyle.Bold;
        WindowStyle.alignment = TextAnchor.UpperCenter;

        // === POPUP WINDOW (no title bar padding) ===
        PopupStyle = new GUIStyle();
        PopupStyle.padding = Pad(4, 4, 4, 4);
        PopupStyle.border = Pad(1, 1, 1, 1);
        PopupStyle.normal.background = MakeTexture(popupBg);
        PopupStyle.onNormal.background = MakeTexture(popupBg);

        // === BUTTON ===
        ButtonStyle = GUI.skin.button;
        ButtonStyle.alignment = TextAnchor.MiddleCenter;
        ButtonStyle.padding = Pad(8, 8, 5, 5);
        ButtonStyle.margin = Pad(2, 2, 2, 2);
        ButtonStyle.fontStyle = FontStyle.Normal;
        ButtonStyle.fixedWidth = 0;
        ButtonStyle.normal.background = MakeTexture(buttonBg);
        ButtonStyle.hover.background = MakeTexture(buttonHover);
        ButtonStyle.active.background = MakeTexture(buttonActive);
        ButtonStyle.focused.background = MakeTexture(buttonBg);
        ButtonStyle.onNormal.background = MakeTexture(buttonBg);
        ButtonStyle.onHover.background = MakeTexture(buttonHover);
        ButtonStyle.onActive.background = MakeTexture(buttonActive);
        ButtonStyle.onFocused.background = MakeTexture(buttonBg);
        ButtonStyle.normal.textColor = dimText;
        ButtonStyle.hover.textColor = white;
        ButtonStyle.active.textColor = white;
        ButtonStyle.focused.textColor = white;
        ButtonStyle.onNormal.textColor = dimText;
        ButtonStyle.onHover.textColor = white;

        SmallButtonStyle = MakeButtonStyle(buttonBg, buttonHover, buttonActive, dimText, white);
        SmallButtonStyle.padding = Pad(6, 6, 3, 3);
        SmallButtonStyle.margin = Pad(2, 2, 2, 2);

        // === TABS ===
        TabInactiveStyle = MakeButtonStyle(tabInactiveBg, tabHoverBg, buttonActive, dimText, white);
        TabInactiveStyle.padding = Pad(8, 8, 6, 6);
        TabInactiveStyle.margin = Pad(0, 0, 0, 0);
        TabInactiveStyle.fontStyle = FontStyle.Normal;

        TabActiveStyle = MakeButtonStyle(tabActiveBg, tabActiveBg, tabActiveBg, white, white);
        TabActiveStyle.padding = Pad(8, 8, 6, 6);
        TabActiveStyle.margin = Pad(0, 0, 0, 0);
        TabActiveStyle.fontStyle = FontStyle.Bold;

        var subTabActiveBg = new Color(0.16f, 0.22f, 0.30f, 1f);
        SubTabInactiveStyle = MakeButtonStyle(new Color(0.10f, 0.10f, 0.12f, 1f), tabHoverBg, buttonActive, dimText, white);
        SubTabInactiveStyle.padding = Pad(6, 6, 4, 4);
        SubTabInactiveStyle.margin = Pad(0, 2, 0, 0);
        SubTabInactiveStyle.fontStyle = FontStyle.Normal;

        SubTabActiveStyle = MakeButtonStyle(subTabActiveBg, subTabActiveBg, subTabActiveBg, white, white);
        SubTabActiveStyle.padding = Pad(6, 6, 4, 4);
        SubTabActiveStyle.margin = Pad(0, 2, 0, 0);
        SubTabActiveStyle.fontStyle = FontStyle.Bold;

        // === PIN BUTTON ===
        var pinBg = new Color(0.18f, 0.18f, 0.20f, 1f);
        var pinActiveBg = new Color(0.50f, 0.38f, 0.05f, 1f);
        var pinActiveHover = new Color(0.60f, 0.46f, 0.08f, 1f);
        var pinGold = new Color(1f, 0.82f, 0.28f, 1f);

        PinStyle = MakeButtonStyle(new Color(0.25f, 0.25f, 0.28f, 1f), buttonHover, buttonActive, new Color(0.80f, 0.80f, 0.85f, 1f), white);
        PinStyle.padding = Pad(4, 4, 3, 3);
        PinStyle.margin = Pad(2, 4, 1, 1);
        PinStyle.fontSize = 13;
        PinStyle.fontStyle = FontStyle.Bold;

        PinActiveStyle = MakeButtonStyle(pinActiveBg, pinActiveHover, pinActiveBg, pinGold, pinGold);
        PinActiveStyle.padding = Pad(4, 4, 3, 3);
        PinActiveStyle.margin = Pad(2, 4, 1, 1);
        PinActiveStyle.fontStyle = FontStyle.Bold;
        PinActiveStyle.fontSize = 13;

        // === PINNED BADGE (inline status label) ===
        PinnedBadgeStyle = new GUIStyle();
        PinnedBadgeStyle.alignment = TextAnchor.MiddleLeft;
        PinnedBadgeStyle.padding = Pad(4, 4, 2, 2);
        PinnedBadgeStyle.normal.textColor = pinGold;
        PinnedBadgeStyle.fontStyle = FontStyle.Bold;

        // === TOGGLE / RADIO (flat rows, prefix indicates state) ===
        ToggleStyle = MakeFlatRowStyle(transparent, rowHover, OffColor, white);
        ToggleOnStyle = MakeFlatRowStyle(transparent, rowHover, OnColor, new Color(0.75f, 1f, 0.75f, 1f));
        RadioStyle = MakeFlatRowStyle(transparent, rowHover, OffColor, white);
        RadioOnStyle = MakeFlatRowStyle(transparent, rowHover, AccentColor, white);

        // === DROPDOWN BUTTON (looks like a select-box) ===
        DropdownButtonStyle = MakeButtonStyle(buttonBg, buttonHover, buttonActive, dimText, white);
        DropdownButtonStyle.padding = Pad(8, 8, 4, 4);
        DropdownButtonStyle.alignment = TextAnchor.MiddleLeft;

        DropdownItemStyle = MakeFlatRowStyle(transparent, rowHover, dimText, white);
        DropdownItemStyle.padding = Pad(8, 8, 4, 4);

        DropdownItemActiveStyle = MakeFlatRowStyle(dropdownItemActive, dropdownItemActive, white, white);
        DropdownItemActiveStyle.padding = Pad(8, 8, 4, 4);

        // === HEADERS ===
        HeaderStyle = new GUIStyle();
        HeaderStyle.alignment = TextAnchor.MiddleLeft;
        HeaderStyle.padding = Pad(8, 8, 5, 5);
        HeaderStyle.margin = Pad(0, 0, 6, 4);
        HeaderStyle.fontStyle = FontStyle.Bold;
        HeaderStyle.stretchWidth = true;
        HeaderStyle.normal.background = MakeTexture(headerBg);
        HeaderStyle.normal.textColor = AccentColor;

        SubHeaderStyle = new GUIStyle();
        SubHeaderStyle.alignment = TextAnchor.MiddleLeft;
        SubHeaderStyle.padding = Pad(6, 6, 3, 3);
        SubHeaderStyle.margin = Pad(0, 0, 4, 2);
        SubHeaderStyle.fontStyle = FontStyle.Bold;
        SubHeaderStyle.stretchWidth = true;
        SubHeaderStyle.normal.background = MakeTexture(subHeaderBg);
        SubHeaderStyle.normal.textColor = dimText;

        BoxStyle = HeaderStyle; // back-compat

        // === LABEL ===
        LabelStyle = new GUIStyle();
        LabelStyle.alignment = TextAnchor.MiddleLeft;
        LabelStyle.padding = Pad(2, 2, 2, 2);
        LabelStyle.margin = Pad(2, 2, 2, 2);
        LabelStyle.normal.textColor = dimText;

        ValueLabelStyle = new GUIStyle();
        ValueLabelStyle.alignment = TextAnchor.MiddleRight;
        ValueLabelStyle.padding = Pad(2, 2, 2, 2);
        ValueLabelStyle.margin = Pad(2, 2, 2, 2);
        ValueLabelStyle.normal.textColor = dimText;

        // === TEXT FIELD ===
        TextFieldStyle = new GUIStyle();
        TextFieldStyle.padding = Pad(6, 6, 4, 4);
        TextFieldStyle.margin = Pad(2, 2, 2, 2);
        TextFieldStyle.normal.background = MakeTexture(new Color(0.14f, 0.14f, 0.16f, 1f));
        TextFieldStyle.focused.background = MakeTexture(new Color(0.18f, 0.18f, 0.22f, 1f));
        TextFieldStyle.hover.background = MakeTexture(new Color(0.16f, 0.16f, 0.19f, 1f));
        TextFieldStyle.normal.textColor = white;
        TextFieldStyle.focused.textColor = white;
        TextFieldStyle.hover.textColor = white;

        // === SLIDER ===
        HSliderStyle = GUI.skin.horizontalSlider;
        HSliderStyle.margin = Pad(4, 4, 8, 4);
        HSliderStyle.fixedHeight = 6;
        HSliderStyle.fixedWidth = 0;
        HSliderStyle.normal.background = MakeTexture(sliderTrack);
        HSliderStyle.hover.background = MakeTexture(sliderTrack);
        HSliderStyle.active.background = MakeTexture(sliderTrack);
        HSliderStyle.focused.background = MakeTexture(sliderTrack);

        HSliderThumbStyle = GUI.skin.horizontalSliderThumb;
        HSliderThumbStyle.fixedWidth = 12;
        HSliderThumbStyle.fixedHeight = 12;
        HSliderThumbStyle.normal.background = MakeTexture(sliderThumb);
        HSliderThumbStyle.hover.background = MakeTexture(new Color(0.7f, 0.8f, 1f, 1f));
        HSliderThumbStyle.active.background = MakeTexture(AccentColor);
        HSliderThumbStyle.focused.background = MakeTexture(sliderThumb);

        // === TOOLTIP ===
        TooltipStyle = new GUIStyle
        {
            padding = Pad(8, 8, 6, 6),
            normal =
            {
                background = MakeTexture(new Color(0.05f, 0.05f, 0.05f, 0.95f)),
                textColor = new Color(0.95f, 0.95f, 0.95f, 1f)
            },
            wordWrap = true
        };
    }

    private static GUIStyle? _rowEvenStyle;
    private static GUIStyle? _rowOddStyle;
    private static GUIStyle? _rowPinnedStyle;
    internal static GUIStyle? PinnedBadgeStyle;
    internal static GUIStyle? PinStyle;
    internal static GUIStyle? PinActiveStyle;

    internal static GUIStyle HeartRowStyle(int index, bool isPinned = false)
    {
        if (isPinned)
        {
            _rowPinnedStyle ??= MakeFlatRowStyle(
                new Color(0.30f, 0.22f, 0.05f, 0.85f),
                new Color(0.40f, 0.30f, 0.08f, 1f),
                new Color(1f, 0.85f, 0.35f, 1f),
                Color.white);
            return _rowPinnedStyle;
        }

        if (index % 2 == 0)
        {
            _rowEvenStyle ??= MakeFlatRowStyle(
                new Color(0.12f, 0.12f, 0.14f, 0.95f),
                new Color(0.22f, 0.30f, 0.42f, 0.95f),
                new Color(0.85f, 0.85f, 0.85f, 1f),
                Color.white);
            return _rowEvenStyle;
        }

        _rowOddStyle ??= MakeFlatRowStyle(
            new Color(0.10f, 0.10f, 0.12f, 0.90f),
            new Color(0.22f, 0.30f, 0.42f, 0.95f),
            new Color(0.85f, 0.85f, 0.85f, 1f),
            Color.white);
        return _rowOddStyle;
    }

    internal static GUIStyle GetSwatchStyle(int colorIndex)
    {
        if (SwatchStyles.TryGetValue(colorIndex, out var s) && s != null) return s;
        s = MakeSwatchStyle(ColorOptions.GetColor(colorIndex), false);
        SwatchStyles[colorIndex] = s;
        return s;
    }

    internal static GUIStyle GetSwatchSelectedStyle(int colorIndex)
    {
        if (SwatchSelectedStyles.TryGetValue(colorIndex, out var s) && s != null) return s;
        s = MakeSwatchStyle(ColorOptions.GetColor(colorIndex), true);
        SwatchSelectedStyles[colorIndex] = s;
        return s;
    }

    private static GUIStyle MakeSwatchStyle(Color color, bool selected)
    {
        var s = new GUIStyle();
        s.padding = Pad(0, 0, 0, 0);
        s.margin = Pad(2, 2, 2, 2);
        s.border = selected ? Pad(2, 2, 2, 2) : Pad(1, 1, 1, 1);

        var bg = MakeBorderedTexture(color, selected ? Color.white : new Color(0f, 0f, 0f, 0.4f), selected ? 2 : 1);
        var bgHover = MakeBorderedTexture(color, Color.white, 2);
        s.normal.background = bg;
        s.hover.background = bgHover;
        s.active.background = bgHover;
        s.focused.background = bg;
        s.onNormal.background = bg;
        s.onHover.background = bgHover;
        return s;
    }

    private static GUIStyle MakeButtonStyle(Color bg, Color hover, Color active, Color text, Color hoverText)
    {
        var s = new GUIStyle();
        s.alignment = TextAnchor.MiddleCenter;
        s.padding = Pad(6, 6, 4, 4);
        s.margin = Pad(2, 2, 2, 2);
        s.normal.background = MakeTexture(bg);
        s.hover.background = MakeTexture(hover);
        s.active.background = MakeTexture(active);
        s.focused.background = MakeTexture(bg);
        s.onNormal.background = MakeTexture(bg);
        s.onHover.background = MakeTexture(hover);
        s.onActive.background = MakeTexture(active);
        s.onFocused.background = MakeTexture(bg);
        s.normal.textColor = text;
        s.hover.textColor = hoverText;
        s.active.textColor = hoverText;
        s.focused.textColor = hoverText;
        s.onNormal.textColor = text;
        s.onHover.textColor = hoverText;
        return s;
    }

    private static GUIStyle MakeFlatRowStyle(Color bg, Color hoverBg, Color text, Color hoverText)
    {
        var s = new GUIStyle();
        s.alignment = TextAnchor.MiddleLeft;
        s.padding = Pad(6, 6, 4, 4);
        s.margin = Pad(0, 0, 1, 1);
        s.stretchWidth = true;
        s.wordWrap = false;
        s.normal.background = MakeTexture(bg);
        s.hover.background = MakeTexture(hoverBg);
        s.active.background = MakeTexture(hoverBg);
        s.focused.background = MakeTexture(bg);
        s.normal.textColor = text;
        s.hover.textColor = hoverText;
        s.active.textColor = hoverText;
        s.focused.textColor = hoverText;
        return s;
    }

    private static Texture2D MakeTexture(Color color)
    {
        if (Textures.TryGetValue(color, out var texture) && texture) return texture;
        texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        Textures[color] = texture;
        return texture;
    }

    private static Texture2D MakeBorderedTexture(Color fill, Color border, int borderWidth)
    {
        const int size = 16;
        var tex = new Texture2D(size, size);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var onBorder = x < borderWidth || x >= size - borderWidth || y < borderWidth || y >= size - borderWidth;
            tex.SetPixel(x, y, onBorder ? border : fill);
        }
        tex.Apply();
        return tex;
    }
}
