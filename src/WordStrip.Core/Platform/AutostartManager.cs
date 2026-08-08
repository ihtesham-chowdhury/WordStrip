using Microsoft.Win32;

namespace WordStrip.Core.Platform;

/// <summary>
/// Toggles per-user autostart via the classic HKCU "Run" registry key — functionally the same result as a
/// Startup-folder shortcut (Windows launches the target on sign-in), but a couple lines of registry code
/// instead of building/writing a .lnk shortcut through COM interop. Trivial for the user to inspect or
/// remove by hand (regedit or msconfig/Task Manager's Startup tab) if they ever want to.
/// </summary>
public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WordStrip";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
            key.SetValue(ValueName, $"\"{executablePath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
