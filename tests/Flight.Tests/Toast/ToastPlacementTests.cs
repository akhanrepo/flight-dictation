using Flight.Core.Toast;
using Xunit;

namespace Flight.Tests.Toast;

/// <summary>Feature #7: default position, remembered position, and staying on screen.</summary>
public class ToastPlacementTests
{
    // 1920x1080 primary with a 48 px taskbar, at 100%
    private static readonly WorkArea Primary = new(new PixelRect(0, 0, 1920, 1032), 1.0);

    // A second monitor to the right, at 150%
    private static readonly WorkArea Right = new(new PixelRect(1920, 0, 2560, 1400), 1.5);

    private static readonly PixelSize Size = new(400, 120);

    [Fact]
    public void Default_IsBottomCenterAboveTheTaskbar()
    {
        var at = ToastPlacement.Place(Size, saved: null, [Primary, Right], Primary);

        Assert.Equal((1920 - 400) / 2, at.X);
        Assert.Equal(1032 - 120 - 24, at.Y);
    }

    [Fact]
    public void Default_MarginScalesWithThePrimaryMonitor()
    {
        var hiDpi = new WorkArea(new PixelRect(0, 0, 2880, 1548), 1.5);

        var at = ToastPlacement.Place(Size, saved: null, [hiDpi], hiDpi);

        Assert.Equal(1548 - 120 - 36, at.Y); // 24 DIPs at 150%
    }

    [Fact]
    public void Saved_PositionIsUsedWhenItIsOnScreen()
    {
        var at = ToastPlacement.Place(Size, new PixelPoint(2400, 300), [Primary, Right], Primary);

        Assert.Equal(new PixelPoint(2400, 300), at);
    }

    [Fact]
    public void Saved_PositionHangingOffTheEdgeIsPulledFullyInside()
    {
        var at = ToastPlacement.Place(Size, new PixelPoint(1800, 1000), [Primary], Primary);

        Assert.Equal(new PixelPoint(1920 - 400, 1032 - 120), at);
    }

    // The monitor it was on has been unplugged
    [Fact]
    public void Saved_PositionOnAMissingMonitorMovesToTheNearestOne()
    {
        var at = ToastPlacement.Place(Size, new PixelPoint(3000, 500), [Primary], Primary);

        Assert.Equal(new PixelPoint(1920 - 400, 500), at);
    }

    // Resolution lowered since it was saved
    [Fact]
    public void Saved_PositionPastAShrunkScreenIsPulledBack()
    {
        var smaller = new WorkArea(new PixelRect(0, 0, 1280, 680), 1.0);

        var at = ToastPlacement.Place(Size, new PixelPoint(1500, 900), [smaller], smaller);

        Assert.Equal(new PixelPoint(1280 - 400, 680 - 120), at);
    }

    [Fact]
    public void Saved_PositionAboveOrLeftOfTheScreenIsPulledBack()
    {
        var at = ToastPlacement.Place(Size, new PixelPoint(-500, -40), [Primary], Primary);

        Assert.Equal(new PixelPoint(0, 0), at);
    }
}
