using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland;

static class Native
{
    public enum MediaKey { VolumeDown = 9, VolumeUp = 10, NextTrack = 11, PreviousTrack = 12 }

    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;
    const long WS_MAXIMIZE = 0x01000000;
    const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const long WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint MONITORINFOF_PRIMARY = 1;
    const int VK_CONTROL = 0x11;
    const int HSHELL_APPCOMMAND = 12;
    const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;
    const byte AC_LINE_ONLINE = 1, BATTERY_FLAG_NO_BATTERY = 128, BATTERY_FLAG_UNKNOWN = 255, BATTERY_PERCENT_MAX = 100;
    const int ClassNameCapacity = 64;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    static readonly int ShellMessage = (int)RegisterWindowMessage("SHELLHOOK");

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
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

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

    [DllImport("user32.dll")]
    static extern bool RegisterShellHookWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string name);

    [DllImport("ntdll.dll")]
    static extern int NtQueryWnfStateData(ref ulong name, IntPtr type, IntPtr scope, out uint stamp, out int data, ref uint size);

    public static bool IsCtrlDown => GetAsyncKeyState(VK_CONTROL) < 0;

    public static bool RegisterShellHook(IntPtr hwnd) => RegisterShellHookWindow(hwnd);

    public static MediaKey? MediaKeyOf(int message, IntPtr wParam, IntPtr lParam) =>
        message == ShellMessage && wParam.ToInt64() == HSHELL_APPCOMMAND ? (MediaKey)((lParam.ToInt64() >> 16) & 0xFFF) : null;

    public static void HideFromTaskSwitcher(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    public static bool IsToolWindow(IntPtr hwnd) => (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0;

    public static void KeepOnTop(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static bool IsForegroundFullscreen(IntPtr self)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == self || foreground == GetDesktopWindow() || foreground == GetShellWindow()) return false;

        var className = new StringBuilder(ClassNameCapacity);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow") return false;

        long style = GetWindowLongPtr(foreground, GWL_STYLE).ToInt64();
        bool framed = (style & WS_CAPTION) == WS_CAPTION || (style & WS_THICKFRAME) != 0;
        if ((style & WS_MAXIMIZE) != 0 && framed) return false;
        if (!GetWindowRect(foreground, out RECT window)) return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0) return false;

        RECT monitor = info.rcMonitor;
        return window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }

    public static bool? IsDoNotDisturbOn()
    {
        ulong name = WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED;
        uint size = sizeof(int);
        try
        {
            if (NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, out int profile, ref size) != 0) return null;
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
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status)) return false;
        if (status.BatteryFlag == BATTERY_FLAG_UNKNOWN || (status.BatteryFlag & BATTERY_FLAG_NO_BATTERY) != 0
            || status.BatteryLifePercent > BATTERY_PERCENT_MAX) return false;
        percent = status.BatteryLifePercent;
        plugged = status.ACLineStatus == AC_LINE_ONLINE;
        return true;
    }
}
