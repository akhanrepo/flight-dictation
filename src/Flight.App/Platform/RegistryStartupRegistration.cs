using Flight.Core.Startup;
using Microsoft.Win32;

namespace Flight.App.Platform;

/// <summary>
/// "Start with Windows" as an HKCU Run value: per-user, no admin rights, and
/// visible (and switchable) in Task Manager's Startup tab.
/// </summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Flight";

    // Velopack layout: <root>\current\Flight.App.exe, with a stable launcher
    // at <root>\Flight.exe that survives updates (current\ is replaced).
    private static readonly string InstallRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    private static readonly string Launcher = Path.Combine(InstallRoot, "Flight.exe");

    /// <summary>Installed copies only: a dev build in bin/ has no launcher or Update.exe.</summary>
    public bool IsAvailable =>
        File.Exists(Launcher) && File.Exists(Path.Combine(InstallRoot, "Update.exe"));

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void Enable()
    {
        if (!IsAvailable)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{Launcher}\"");
    }

    public void Disable() => RemoveEntry();

    /// <summary>Also called from Velopack's uninstall hook, so uninstalling leaves no startup entry behind.</summary>
    public static void RemoveEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
