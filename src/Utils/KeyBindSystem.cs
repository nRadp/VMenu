using System;
using ExtrasensoryPerception.UI;
using UnityEngine;

namespace ExtrasensoryPerception.Utils;

public class KeyBindSystem : MonoBehaviour
{
    private static bool _waitingKeyPress;
    private static int _keyBindBeingChanged;
    private static Action<KeyCode>? _pendingKeyCallback;

    private void Update() => HandleKeyRebinding();
    
    public static bool KeyBindButton<T>(T configEntry, params GUILayoutOption[] options) where T : class
    {
        // Reflection (should work with any ConfigEntry<T>)
        var valueProperty = configEntry.GetType().GetProperty("Value");
        if (valueProperty == null) return false;
        
        var currentKey = (KeyCode)(valueProperty.GetValue(configEntry) ?? throw new InvalidOperationException());
        
        string buttonText;
        var identifier = configEntry.GetHashCode();
        
        if (_waitingKeyPress && _keyBindBeingChanged == identifier)
            buttonText = "Waiting for key...";
        else buttonText = currentKey.ToString();
        
        if (GUILayout.Button(buttonText, MenuTheme.ButtonStyle, options))
        {
            if (!_waitingKeyPress)
            {
                _waitingKeyPress = true;
                _keyBindBeingChanged = identifier;
                _pendingKeyCallback = newKey => valueProperty.SetValue(configEntry, newKey);
            }
        }
        
        return Input.GetKey(currentKey);
    }
    
    public static bool KeyBindButton<T>(T configEntry) where T : class => KeyBindButton(configEntry,  GUILayout.Width(135));

    public static bool KeyBindButton<T>(string label, T configEntry, params GUILayoutOption[] buttonOptions) where T : class
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, MenuTheme.LabelStyle);
        GUILayout.FlexibleSpace();
        var isPressed = KeyBindButton(configEntry, buttonOptions);
        GUILayout.EndHorizontal();
        return isPressed;
    }

    public static bool KeyBindButton<T>(string label, T configEntry) where T : class => KeyBindButton(label, configEntry,  GUILayout.Width(135));
    
    private static void HandleKeyRebinding()
    {
        if (!_waitingKeyPress) return;
        
        foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
        {
            if (Input.GetKeyDown(key))
            {
                _pendingKeyCallback?.Invoke(key);
                _keyBindBeingChanged = 0;
                _pendingKeyCallback = null;
                _waitingKeyPress = false;
                break;
            }
        }
    }
}