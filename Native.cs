using System;
using System.Runtime.InteropServices;

namespace Companion;

// Thin wrapper around the few Win32 calls we need.
// Later milestones will add more here (SetWindowPos for pinning,
// GetLastInputInfo for idle detection, GetForegroundWindow for the guardian).
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int newLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Keeps the window out of Alt+Tab.</summary>
    public static void HideFromAltTab(IntPtr hwnd)
    {
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW);
    }
}
