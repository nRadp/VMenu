using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ExtrasensoryPerception.Utils;

public static class MouseSimulator
{
    [DllImport("user32.dll")]
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcursorpos
    private static extern bool GetCursorPos(out CursorPos lpPoint);
    
    [DllImport("user32.dll")]
    // https://learn.microsoft.com/pt-br/windows/win32/api/winuser/nf-winuser-sendinput
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    
    [DllImport("user32.dll")]
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getsystemmetrics
    private static extern int GetSystemMetrics(int nIndex);

    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct CursorPos 
    { 
        public int X; 
        public int Y; 
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
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

    public static Vector2 CursorPosition => GetCursorPos(out var position) ? new Vector2(position.X, position.Y) : Vector2.zero;

    public static void LeftClick()
    {
        var inputs = new INPUT[2];
        
        // Left Button down
        inputs[0] = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_LEFTDOWN,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
        
        // Left Button up
        inputs[1] = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_LEFTUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
        
        _ = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void SetPos(int x, int y)
    {
        x = Mathf.Clamp(x, 0, Screen.width);
        y = Mathf.Clamp(y, 0, Screen.height);
        // Convert screen coordinates to absolute coordinates (0-65535 range)
        // https://learn.microsoft.com/pt-br/windows/win32/api/winuser/ns-winuser-mouseinput
        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        
        var absoluteX = x * 65536 / screenWidth;
        var absoluteY = y * 65536 / screenHeight;
        
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = absoluteX,
                dy = absoluteY,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
        
        _ = SendInput(1, [input], Marshal.SizeOf(typeof(INPUT)));
    }

    public static void SetPos(Vector2 screenPoint) => SetPos((int)screenPoint.x, (int)screenPoint.y);

    public static void Move(int dx, int dy)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = dx,
                dy = dy,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_MOVE,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        _ = SendInput(1, [input], Marshal.SizeOf(typeof(INPUT)));
    }
}