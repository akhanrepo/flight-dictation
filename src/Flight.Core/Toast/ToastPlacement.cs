namespace Flight.Core.Toast;

/// <summary>Where the toast goes: the user's remembered spot, kept on screen, or bottom-center.</summary>
public static class ToastPlacement
{
    /// <summary>Gap between the default position and the taskbar.</summary>
    public const double BottomMargin = 24;

    /// <param name="size">Toast size in physical pixels.</param>
    /// <param name="saved">Top-left the user dragged it to last time, if any.</param>
    /// <param name="workAreas">Every monitor's work area right now.</param>
    /// <param name="primary">The primary monitor's work area.</param>
    /// <returns>Top-left in physical pixels, fully inside a work area.</returns>
    public static PixelPoint Place(PixelSize size, PixelPoint? saved, IReadOnlyList<WorkArea> workAreas, WorkArea primary)
    {
        if (saved is not { } point)
        {
            var area = primary.Bounds;
            return new PixelPoint(
                area.X + (area.Width - size.Width) / 2,
                area.Bottom - size.Height - ToastLayout.ToPhysical(BottomMargin, primary.Scale));
        }

        // A monitor may have been unplugged or resized since: snap into the
        // nearest work area that exists now.
        var target = Nearest(point, workAreas.Count > 0 ? workAreas : [primary]).Bounds;
        return new PixelPoint(
            Clamp(point.X, target.X, target.Right - size.Width),
            Clamp(point.Y, target.Y, target.Bottom - size.Height));
    }

    private static WorkArea Nearest(PixelPoint p, IReadOnlyList<WorkArea> areas) =>
        areas.FirstOrDefault(a => a.Bounds.Contains(p)) is { Bounds.Width: > 0 } containing
            ? containing
            : areas.MinBy(a => DistanceSquared(p, a.Bounds));

    private static long DistanceSquared(PixelPoint p, PixelRect r)
    {
        long dx = Math.Max(Math.Max(r.X - p.X, 0), p.X - (r.Right - 1));
        long dy = Math.Max(Math.Max(r.Y - p.Y, 0), p.Y - (r.Bottom - 1));
        return dx * dx + dy * dy;
    }

    // If the toast is larger than the area, pin to the area's top/left edge.
    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);
}
