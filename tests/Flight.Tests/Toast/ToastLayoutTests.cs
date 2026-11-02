using Flight.Core.Toast;
using Xunit;

namespace Flight.Tests.Toast;

/// <summary>Sizes are DIPs, converted per monitor (feature #7), for the pill (feature #9).</summary>
public class ToastLayoutTests
{
    private static readonly ToastView IconOnly = new() { Title = "Listening", Tone = ToastTone.Listening };
    private static readonly ToastView WithText = new() { Title = "Didn't catch that", ShowsText = true };

    // Same visual size at every common Windows scale factor
    [Theory]
    [InlineData(1.0, 120, 36)]
    [InlineData(1.25, 150, 45)]
    [InlineData(1.5, 180, 54)]
    [InlineData(2.0, 240, 72)]
    public void Layout_ConvertsDipsForTheMonitorScale(double scale, int width, int height)
    {
        Assert.Equal(new PixelSize(width, height), ToastLayout.SizeFor(IconOnly, 0, scale));
    }

    [Fact]
    public void Layout_IconOnlyStatesAreCompactWhateverTheMeasurement()
    {
        Assert.Equal(ToastLayout.CompactWidth, ToastLayout.WidthFor(IconOnly, 500));
    }

    [Theory]
    [InlineData(10, 120)]     // never narrower than the compact pill
    [InlineData(200, 200)]    // fits the message
    [InlineData(200.2, 201)]  // rounds up so nothing is clipped by a fraction
    [InlineData(2000, 360)]   // capped; the title trims with an ellipsis
    public void Layout_MessagesGrowToFitWithinBounds(double measured, double expected)
    {
        Assert.Equal(expected, ToastLayout.WidthFor(WithText, measured));
    }

    // Regression: "Didn't catch that" was clipped to the compact width. The
    // pill's width now comes from the title's own width plus the fixed parts.
    [Fact]
    public void Layout_TextStateWidthIsBuiltFromTheTitleWidth()
    {
        // 28 padding + 16 indicator + 10 gap + 110 title
        Assert.Equal(164, ToastLayout.ContentWidthFor(WithText, 109.3));
        Assert.True(ToastLayout.WidthFor(WithText, ToastLayout.ContentWidthFor(WithText, 109.3)) > ToastLayout.CompactWidth);

        var downloading = WithText with { Progress = 40 };
        Assert.Equal(164 + 10 + 64, ToastLayout.ContentWidthFor(downloading, 109.3));
    }
}
