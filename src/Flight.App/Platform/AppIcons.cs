using Flight.Core.Tray;

namespace Flight.App.Platform;

/// <summary>Paths to the generated icons (assets/brand), copied beside the exe under Assets\.</summary>
public static class AppIcons
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Assets");

    public static string App => Path.Combine(Directory, "flight.ico");

    public static string For(TrayIconKind kind) => kind switch
    {
        TrayIconKind.Listening => Path.Combine(Directory, "flight-listening.ico"),
        TrayIconKind.Paused => Path.Combine(Directory, "flight-paused.ico"),
        _ => App,
    };
}
