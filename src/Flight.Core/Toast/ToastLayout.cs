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
/// Toast dimensions. Everything is designed in DIPs and converted per monitor,
/// so the toast is the same visual size at 100% and 200% scaling — sizing in
/// raw pixels is what squeezed the old toast until its text disappeared.
/// </summary>
public static class ToastLayout
{
    public const double CardWidth = 400;
    public const double PillWidth = 280;
    public const double MinHeight = 52;

    /// <summary>Past this the body scrolls inside the card instead of growing.</summary>
    public const double MaxHeight = 320;

    public static double WidthFor(ToastKind kind) => kind == ToastKind.Pill ? PillWidth : CardWidth;

    /// <summary>Fits the content's measured height into [MinHeight, MaxHeight].</summary>
    public static double HeightFor(double contentHeight) =>
        Math.Clamp(Math.Ceiling(contentHeight), MinHeight, MaxHeight);

    public static int ToPhysical(double dip, double scale) => (int)Math.Ceiling(dip * scale);

    public static PixelSize SizeFor(ToastKind kind, double contentHeight, double scale) => new(
        ToPhysical(WidthFor(kind), scale),
        ToPhysical(HeightFor(contentHeight), scale));
}
