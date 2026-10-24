using System.Runtime.InteropServices;

namespace GrumphySwarm.App.Interop;

/// <summary>
/// The small set of Win32 APIs the notch host needs. Everything else is handled
/// by WPF. Kept internal and in one place so window-style behavior is reviewable.
/// </summary>
internal static class NativeMethods
{
    // ---- window styles -----------------------------------------------------

    internal const int GWL_EXSTYLE = -20;

    /// <summary>Window never becomes the foreground window on click/hover.</summary>
    internal const int WS_EX_NOACTIVATE = 0x0800_0000;

    /// <summary>Hides the window from Alt+Tab and the taskbar.</summary>
    internal const int WS_EX_TOOLWINDOW = 0x0000_0080;

    /// <summary>Keeps the notch above ordinary windows (combined with Topmost).</summary>
    internal const int WS_EX_TOPMOST = 0x0000_0008;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOOWNERZORDER = 0x0200;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_FRAMECHANGED = 0x0020;

    // ---- messages ----------------------------------------------------------

    internal const int WM_NCHITTEST = 0x0084;
    internal const int WM_MOUSEACTIVATE = 0x0021;
    internal const int WM_ACTIVATEAPP = 0x001C;
    internal const int WM_DISPLAYCHANGE = 0x007E;
    internal const int WM_DPICHANGED = 0x02E0;

    /// <summary>Let the mouse event fall through to windows underneath.</summary>
    internal const int HTTRANSPARENT = -1;

    /// <summary>Do not activate the window, but still deliver the mouse click.</summary>
    internal const int MA_NOACTIVATE = 3;

    /// <summary> lParam helpers for screen coordinates packed into a IntPtr. </summary>
    internal static int GetSignedLowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));

    internal static int GetSignedHighWord(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

    /// <summary> wParam helper: low word (e.g. the new DPI in WM_DPICHANGED). </summary>
    internal static int GetUnsignedLowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));

    // ---- monitor geometry --------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    internal const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // ---- low-level mouse hook (outside-click dismissal while pinned) --------

    internal const int WH_MOUSE_LL = 14;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_MBUTTONDOWN = 0x0207;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    internal delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);
}
