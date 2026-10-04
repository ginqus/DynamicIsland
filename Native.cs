using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DynamicIsland;

static class Native
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_MAXIMIZE = 0x01000000;
    public const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint MONITORINFOF_PRIMARY = 1;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);

    const int VK_CONTROL = 0x11;

    /// <summary>Ctrl is held right now. The island never has the keyboard, so this asks the keys themselves.</summary>
    public static bool CtrlDown => GetAsyncKeyState(VK_CONTROL) < 0;

    /// <summary>What the shell tells the windows that ask for it: a key nobody handled is among it.</summary>
    public const int HSHELL_APPCOMMAND = 12;
    public const int APPCOMMAND_VOLUME_DOWN = 9, APPCOMMAND_VOLUME_UP = 10;
    public const int APPCOMMAND_MEDIA_NEXTTRACK = 11, APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;

    [DllImport("user32.dll")]
    public static extern bool RegisterShellHookWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string name);

    /// <summary>The key behind a shell notice of <see cref="HSHELL_APPCOMMAND"/>.</summary>
    public static int AppCommand(IntPtr lParam) => (int)((lParam.ToInt64() >> 16) & 0xFFF);

    public static void KeepOnTop(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>True when a game / video covers the whole primary monitor.</summary>
    public static bool IsForegroundFullscreen(IntPtr self)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == self || fg == GetDesktopWindow() || fg == GetShellWindow()) return false;

        var cls = new StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow") return false;

        // a maximized window on an auto-hide taskbar also covers the monitor, but it keeps its frame. A browser showing
        // a video full screen, or a borderless game, may be maximized too: it has dropped the frame
        long style = GetWindowLongPtr(fg, GWL_STYLE).ToInt64();
        bool framed = (style & WS_CAPTION) == WS_CAPTION || (style & WS_THICKFRAME) != 0;
        if ((style & WS_MAXIMIZE) != 0 && framed) return false;
        if (!GetWindowRect(fg, out RECT r)) return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0) return false;

        RECT m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    // the shell publishes the profile of "Do not disturb" under this name: 0 off, 1 on (priority only), 2 alarms only
    const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;

    [DllImport("ntdll.dll")]
    static extern int NtQueryWnfStateData(ref ulong name, IntPtr type, IntPtr scope, out uint stamp, out int data, ref uint size);

    /// <summary>"Do not disturb" is on; null when the system does not say.</summary>
    public static bool? DoNotDisturb()
    {
        ulong name = WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED;
        uint size = sizeof(int);
        try
        {
            if (NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, out int profile, ref size) != 0) return null;
            // never set since the system started: nobody has turned it on
            return size >= sizeof(int) && profile != 0;
        }
        catch
        {
            return null;
        }
    }

    public static bool TryGetBattery(out int percent, out bool plugged)
    {
        percent = 0;
        plugged = false;
        if (!GetSystemPowerStatus(out var s)) return false;
        if (s.BatteryFlag == 255 || (s.BatteryFlag & 128) != 0 || s.BatteryLifePercent > 100) return false;
        percent = s.BatteryLifePercent;
        plugged = s.ACLineStatus == 1;
        return true;
    }
}

static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "DynamicIsland";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            string? exe = Environment.ProcessPath;
            return exe != null && key?.GetValue(Name) is string value
                && value.Contains(exe, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe) key.SetValue(Name, $"\"{exe}\"");
        else key.DeleteValue(Name, false);
    }
}
