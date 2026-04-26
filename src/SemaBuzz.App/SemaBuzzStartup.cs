using Microsoft.Win32;

namespace SemaBuzz.App;

/// <summary>Manages the Windows Run registry entry that launches SemaBuzz at login.</summary>
internal static class SemaBuzzStartup
{
    private const string RunKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SemaBuzz";

    /// <summary>Adds or removes the startup registry entry to match <paramref name="enable"/>.</summary>
    public static void Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                key.SetValue(AppName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch { /* non-fatal */ }
    }

    /// <summary>Returns true if the startup entry currently exists in the registry.</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) is not null;
        }
        catch { return false; }
    }
}
