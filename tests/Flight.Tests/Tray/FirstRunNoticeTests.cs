using Flight.Core;
using Flight.Core.Tray;
using Xunit;

namespace Flight.Tests.Tray;

public class FirstRunNoticeTests
{
    private readonly FlightOptions _options = new()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "flight-tests-" + Guid.NewGuid().ToString("N")),
    };

    [Fact]
    public void FirstRunNotice_IsClaimedOnlyOnce()
    {
        var notice = new FirstRunNotice(_options);

        Assert.True(notice.TryClaim());
        Assert.False(notice.TryClaim());
    }

    // Once per machine, not per launch: a new process (new instance) sees the marker
    [Fact]
    public void FirstRunNotice_StaysClaimedAcrossLaunches()
    {
        new FirstRunNotice(_options).TryClaim();

        Assert.False(new FirstRunNotice(_options).TryClaim());
    }

    // Feature #8 criterion: the notice names the configured key
    [Fact]
    public void FirstRunNotice_NamesTheHotkey()
    {
        Assert.True(Flight.Core.Input.Hotkey.TryParse("Ctrl+Win", out var hotkey));

        Assert.Contains("Hold Ctrl+Win to dictate", FirstRunNotice.MessageFor(hotkey));
        Assert.Contains("Hold Right Alt to dictate", FirstRunNotice.MessageFor(Flight.Core.Input.Hotkey.Default));
    }
}
