using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland;

static class SourceApp
{
    const uint GW_OWNER = 4;
    const int SW_RESTORE = 9;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const int TitleCapacity = 512, PathCapacity = 1024;
    const string AppsFolder = "shell:AppsFolder";

    static readonly object ExecutableLock = new();
    static string _namedApp = "", _name = "";
    static string _resolvedApp = "", _executable = "";

    delegate bool WindowProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")] static extern bool EnumWindows(WindowProc proc, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint relation);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetApplicationUserModelId(IntPtr process, ref int length, StringBuilder id);

    public static void BringToFront(string appId, string title)
    {
        if (appId.Length == 0) return;

        string executable = ResolveExecutable(appId);
        IntPtr found = IntPtr.Zero;
        var text = new StringBuilder(TitleCapacity);
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;
            if (Native.IsToolWindow(hwnd)) return true;
            if (GetWindowText(hwnd, text, text.Capacity) == 0) return true;
            GetWindowThreadProcessId(hwnd, out uint process);
            if (!IsProcessOf(process, appId, executable)) return true;

            bool titled = title.Length > 0 && text.ToString().Contains(title, StringComparison.OrdinalIgnoreCase);
            if (titled || found == IntPtr.Zero) found = hwnd;
            return !titled;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            Launch(appId);
            return;
        }
        if (IsIconic(found)) ShowWindow(found, SW_RESTORE);
        SetForegroundWindow(found);
    }

    public static string Name(string appId)
    {
        if (appId != _namedApp) (_namedApp, _name) = (appId, ResolveName(appId));
        return _name;
    }

    public static bool OwnsProcess(string appId, uint process)
    {
        if (appId.Length == 0) return false;
        string executable;
        lock (ExecutableLock)
        {
            if (appId != _resolvedApp) (_resolvedApp, _executable) = (appId, ResolveExecutable(appId));
            executable = _executable;
        }
        return IsProcessOf(process, appId, executable);
    }

    public static List<IntPtr> Windows(string appId)
    {
        var found = new List<IntPtr>();
        if (appId.Length == 0) return found;
        EnumWindows((hwnd, _) =>
        {
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;
            GetWindowThreadProcessId(hwnd, out uint process);
            if (OwnsProcess(appId, process)) found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static dynamic? StartMenuEntry(string appId)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        return shell.NameSpace(AppsFolder)?.ParseName(appId);
    }

    static string ResolveName(string appId)
    {
        if (appId.Length == 0) return "";
        try
        {
            if (StartMenuEntry(appId)?.Name is string name && name.Length > 0) return name;
        }
        catch { }
        return appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(appId) : "";
    }

    static string ResolveExecutable(string appId)
    {
        try
        {
            return StartMenuEntry(appId)?.ExtendedProperty("System.Link.TargetParsingPath") as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    static bool IsProcessOf(uint process, string appId, string executable)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process);
        if (handle == IntPtr.Zero) return false;
        try
        {
            var text = new StringBuilder(PathCapacity);
            int size = text.Capacity;
            if (QueryFullProcessImageName(handle, 0, text, ref size))
            {
                string path = text.ToString();
                if (SameName(path, executable) || SameName(Path.GetFileName(path), appId)) return true;
            }
            size = text.Capacity;
            return GetApplicationUserModelId(handle, ref size, text.Clear()) == 0 && SameName(text.ToString(), appId);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static void Launch(string appId)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppsFolder + @"\" + appId) { UseShellExecute = true })?.Dispose();
            return;
        }
        catch (Win32Exception) { }

        try
        {
            foreach (Process running in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(appId)))
            {
                if (running.MainModule?.FileName is not { } path) continue;
                Process.Start(path).Dispose();
                return;
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
