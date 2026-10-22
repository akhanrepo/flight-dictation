namespace Flight.Core.Toast;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelSize(int Width, int Height);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(PixelPoint p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;
}

/// <summary>A monitor's usable area (excluding the taskbar) in physical pixels, and its scale.</summary>
public readonly record struct WorkArea(PixelRect Bounds, double Scale);

/// <summary>
/// Indicator pill dimensions. Everything is designed in DIPs and converted per
/// monitor, so the pill is the same visual size at 100% and 200% scaling —
/// sizing in raw pixels is what once squeezed the old toast until its text
/// disappeared.
/// </summary>
public static class ToastLayout
{
    /// <summary>Icon-only states: listening, processing, copied.</summary>
    public const double CompactWidth = 120;

    /// <summary>States with a message grow to fit it, up to this; the title trims past it.</summary>
    public const double MaxWidth = 360;

    public const double Height = 36;

    /// <param name="measuredContentWidth">The rendered message's desired width, for text states.</param>
    public static double WidthFor(ToastView view, double measuredContentWidth) =>
        view.ShowsText
            ? Math.Clamp(Math.Ceiling(measuredContentWidth), CompactWidth, MaxWidth)
            : CompactWidth;

    public static int ToPhysical(double dip, double scale) => (int)Math.Ceiling(dip * scale);

    public static PixelSize SizeFor(ToastView view, double measuredContentWidth, double scale) => new(
        ToPhysical(WidthFor(view, measuredContentWidth), scale),
        ToPhysical(Height, scale));
}
