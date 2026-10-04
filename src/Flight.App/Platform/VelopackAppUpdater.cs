using Flight.Core;
using Flight.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace Flight.App.Platform;

/// <summary>
/// Velopack-backed updates from <see cref="FlightOptions.UpdateFeed"/>: a
/// local folder of <c>vpk pack</c> output, or a public GitHub repo.
/// </summary>
public sealed class VelopackAppUpdater : IAppUpdater
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _downloaded;

    public VelopackAppUpdater(FlightOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.UpdateFeed))
        {
            return;
        }

        IUpdateSource source = Uri.TryCreate(options.UpdateFeed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? new GithubSource(options.UpdateFeed, accessToken: null, prerelease: false)
            : new SimpleFileSource(new DirectoryInfo(options.UpdateFeed));
        _manager = new UpdateManager(source);
    }

    /// <summary>False for dev builds, and when no feed is configured.</summary>
    public bool IsInstalled => _manager?.IsInstalled ?? false;

    public async Task<string?> DownloadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null)
        {
            return null;
        }

        var update = await _manager.CheckForUpdatesAsync();
        if (update is null)
        {
            return null;
        }

        await _manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
        _downloaded = update;
        return update.TargetFullRelease.Version.ToString();
    }

    public void RestartToApply()
    {
        if (_manager is not null && _downloaded is not null)
        {
            _manager.ApplyUpdatesAndRestart(_downloaded.TargetFullRelease);
        }
    }

    public void ApplyOnExit()
    {
        if (_manager is not null && _downloaded is not null)
        {
            _manager.WaitExitThenApplyUpdates(_downloaded.TargetFullRelease, silent: true, restart: false);
        }
    }
}
