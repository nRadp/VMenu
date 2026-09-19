using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ExtrasensoryPerception.Utils;

internal static class InputSimulator
{
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;

    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;

    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public static bool Press(KeyCode keyCode) => TrySend(keyCode, false);

    public static bool Release(KeyCode keyCode) => TrySend(keyCode, true);

    private static bool TrySend(KeyCode keyCode, bool keyUp)
    {
        if (TryGetMouseInput(keyCode, keyUp, out var mouseInput))
            return Send(mouseInput);

        if (!TryGetVirtualKey(keyCode, out var virtualKey))
            return false;

        var scanCode = (ushort)MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC);
        var flags = KEYEVENTF_SCANCODE;
        if (keyUp) flags |= KEYEVENTF_KEYUP;
        if (IsExtendedKey(keyCode)) flags |= KEYEVENTF_EXTENDEDKEY;

        var keyboardInput = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        if (scanCode != 0 && Send(keyboardInput))
            return true;

        // Fallback for keys where scan-code delivery is not accepted.
        keyboardInput.U.ki = new KEYBDINPUT
        {
            wVk = virtualKey,
            wScan = 0,
            dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        return Send(keyboardInput);
    }

    private static bool Send(INPUT input)
    {
        return SendInput(1, [input], Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    private static bool TryGetMouseInput(KeyCode keyCode, bool keyUp, out INPUT input)
    {
        input = default;
        uint flags;
        uint mouseData = 0;

        switch (keyCode)
        {
            case KeyCode.Mouse0:
                flags = keyUp ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_LEFTDOWN;
                break;
            case KeyCode.Mouse1:
                flags = keyUp ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_RIGHTDOWN;
                break;
            case KeyCode.Mouse2:
                flags = keyUp ? MOUSEEVENTF_MIDDLEUP : MOUSEEVENTF_MIDDLEDOWN;
                break;
            case KeyCode.Mouse3:
                flags = keyUp ? MOUSEEVENTF_XUP : MOUSEEVENTF_XDOWN;
                mouseData = XBUTTON1;
                break;
            case KeyCode.Mouse4:
                flags = keyUp ? MOUSEEVENTF_XUP : MOUSEEVENTF_XDOWN;
                mouseData = XBUTTON2;
                break;
            default:
                return false;
        }

        input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = mouseData,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        return true;
    }

    private static bool IsExtendedKey(KeyCode keyCode)
    {
        return keyCode is KeyCode.RightAlt or KeyCode.RightControl or
            KeyCode.Insert or KeyCode.Delete or KeyCode.Home or KeyCode.End or
            KeyCode.PageUp or KeyCode.PageDown or
            KeyCode.UpArrow or KeyCode.DownArrow or KeyCode.LeftArrow or KeyCode.RightArrow;
    }

    private static bool TryGetVirtualKey(KeyCode keyCode, out ushort virtualKey)
    {
        virtualKey = keyCode switch
        {
            KeyCode.Backspace => 0x08,
            KeyCode.Tab => 0x09,
            KeyCode.Clear => 0x0C,
            KeyCode.Return => 0x0D,
            KeyCode.Pause => 0x13,
            KeyCode.Escape => 0x1B,
            KeyCode.Space => 0x20,
            KeyCode.PageUp => 0x21,
            KeyCode.PageDown => 0x22,
            KeyCode.End => 0x23,
            KeyCode.Home => 0x24,
            KeyCode.LeftArrow => 0x25,
            KeyCode.UpArrow => 0x26,
            KeyCode.RightArrow => 0x27,
            KeyCode.DownArrow => 0x28,
            KeyCode.Insert => 0x2D,
            KeyCode.Delete => 0x2E,
            KeyCode.Keypad0 => 0x60,
            KeyCode.Keypad1 => 0x61,
            KeyCode.Keypad2 => 0x62,
            KeyCode.Keypad3 => 0x63,
            KeyCode.Keypad4 => 0x64,
            KeyCode.Keypad5 => 0x65,
            KeyCode.Keypad6 => 0x66,
            KeyCode.Keypad7 => 0x67,
            KeyCode.Keypad8 => 0x68,
            KeyCode.Keypad9 => 0x69,
            KeyCode.KeypadMultiply => 0x6A,
            KeyCode.KeypadPlus => 0x6B,
            KeyCode.KeypadMinus => 0x6D,
            KeyCode.KeypadPeriod => 0x6E,
            KeyCode.KeypadDivide => 0x6F,
            KeyCode.F1 => 0x70,
            KeyCode.F2 => 0x71,
            KeyCode.F3 => 0x72,
            KeyCode.F4 => 0x73,
            KeyCode.F5 => 0x74,
            KeyCode.F6 => 0x75,
            KeyCode.F7 => 0x76,
            KeyCode.F8 => 0x77,
            KeyCode.F9 => 0x78,
            KeyCode.F10 => 0x79,
            KeyCode.F11 => 0x7A,
            KeyCode.F12 => 0x7B,
            KeyCode.F13 => 0x7C,
            KeyCode.F14 => 0x7D,
            KeyCode.F15 => 0x7E,
            KeyCode.Numlock => 0x90,
            KeyCode.CapsLock => 0x14,
            KeyCode.ScrollLock => 0x91,
            KeyCode.RightShift => 0xA1,
            KeyCode.LeftShift => 0xA0,
            KeyCode.RightControl => 0xA3,
            KeyCode.LeftControl => 0xA2,
            KeyCode.RightAlt => 0xA5,
            KeyCode.LeftAlt => 0xA4,
            KeyCode.Semicolon => 0xBA,
            KeyCode.Equals => 0xBB,
            KeyCode.Comma => 0xBC,
            KeyCode.Minus => 0xBD,
            KeyCode.Period => 0xBE,
            KeyCode.Slash => 0xBF,
            KeyCode.BackQuote => 0xC0,
            KeyCode.LeftBracket => 0xDB,
            KeyCode.Backslash => 0xDC,
            KeyCode.RightBracket => 0xDD,
            KeyCode.Quote => 0xDE,
            _ => 0
        };

        if (virtualKey != 0) return true;

        if (keyCode is >= KeyCode.A and <= KeyCode.Z)
        {
            virtualKey = (ushort)(0x41 + (keyCode - KeyCode.A));
            return true;
        }

        if (keyCode is >= KeyCode.Alpha0 and <= KeyCode.Alpha9)
        {
            virtualKey = (ushort)(0x30 + (keyCode - KeyCode.Alpha0));
            return true;
        }

        return false;
    }
}
