using Flight.Core.Tray;

namespace Flight.App.Platform;

/// <summary>Paths to the generated icons (assets/brand), copied beside the exe under Assets\.</summary>
public static class AppIcons
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Assets");

    public static string App => Path.Combine(Directory, "flight.ico");

    /// <summary>The 32-px mark, shown at 16 DIPs in the toast header (sharp up to 200%).</summary>
    public static string LogoPng => Path.Combine(Directory, "flight-32.png");

    public static string For(TrayIconKind kind) => kind switch
    {
        TrayIconKind.Listening => Path.Combine(Directory, "flight-listening.ico"),
        TrayIconKind.Paused => Path.Combine(Directory, "flight-paused.ico"),
        _ => App,
    };
}
