using Flight.Core.Toast;
using Xunit;

namespace Flight.Tests.Toast;

/// <summary>Feature #7: sizes are DIPs, converted per monitor, and fit the content.</summary>
public class ToastLayoutTests
{
    // Same visual size at every common Windows scale factor
    [Theory]
    [InlineData(1.0, 400, 120)]
    [InlineData(1.25, 500, 150)]
    [InlineData(1.5, 600, 180)]
    [InlineData(2.0, 800, 240)]
    public void Layout_ConvertsDipsForTheMonitorScale(double scale, int width, int height)
    {
        var size = ToastLayout.SizeFor(ToastKind.Card, contentHeight: 120, scale);

        Assert.Equal(new PixelSize(width, height), size);
    }

    [Fact]
    public void Layout_PillsAreNarrowerThanCards()
    {
        Assert.Equal(280, ToastLayout.WidthFor(ToastKind.Pill));
        Assert.Equal(400, ToastLayout.WidthFor(ToastKind.Card));
    }

    [Theory]
    [InlineData(10, 52)]   // never smaller than a pill
    [InlineData(140, 140)] // fits the content
    [InlineData(140.2, 141)] // rounds up so nothing is clipped by a fraction
    [InlineData(2000, 320)] // capped; the body scrolls inside
    public void Layout_HeightFitsTheContentWithinBounds(double content, double expected)
    {
        Assert.Equal(expected, ToastLayout.HeightFor(content));
    }
}
