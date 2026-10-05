using Microsoft.Win32;

namespace DynamicIsland;

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
