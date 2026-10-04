using Flight.Core.Updates;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Updates;

public class UpdateCheckTests
{
    private readonly FakeAppUpdater _updater = new();

    private UpdateCheck CreateCheck() => new(_updater, NullLogger<UpdateCheck>.Instance);

    [Fact]
    public async Task UpdateCheck_SkipsDevBuilds()
    {
        _updater.IsInstalled = false;
        _updater.AvailableVersion = "1.1.0";

        Assert.Null(await CreateCheck().RunAsync());
        Assert.Equal(0, _updater.DownloadCount);
    }

    [Fact]
    public async Task UpdateCheck_StagesANewerReleaseForExit()
    {
        _updater.AvailableVersion = "1.1.0";

        Assert.Equal("1.1.0", await CreateCheck().RunAsync());
        Assert.True(_updater.ApplyOnExitRequested);
    }

    // The tray offers "Restart to update" from this
    [Fact]
    public async Task UpdateCheck_ExposesTheStagedVersion()
    {
        _updater.AvailableVersion = "1.1.0";
        var check = CreateCheck();
        string? raised = null;
        check.Staged += v => raised = v;

        await check.RunAsync();

        Assert.Equal("1.1.0", check.StagedVersion);
        Assert.Equal("1.1.0", raised);
    }

    [Fact]
    public async Task UpdateCheck_DoesNothingWhenUpToDate()
    {
        Assert.Null(await CreateCheck().RunAsync());
        Assert.False(_updater.ApplyOnExitRequested);
    }

    // An unreachable feed must never surface into the app
    [Fact]
    public async Task UpdateCheck_SwallowsFeedFailures()
    {
        _updater.ExceptionToThrow = new HttpRequestException("feed down");

        Assert.Null(await CreateCheck().RunAsync());
        Assert.False(_updater.ApplyOnExitRequested);
    }
}
