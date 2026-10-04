using Flight.Core;
using Flight.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace Flight.App.Platform;

/// <summary>Velopack-backed updates from the public releases repo.</summary>
public sealed class VelopackAppUpdater : IAppUpdater
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _downloaded;

    public VelopackAppUpdater(FlightOptions options) =>
        _manager = new UpdateManager(new GithubSource(options.UpdateFeedUrl.ToString(), accessToken: null, prerelease: false));

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<string?> DownloadLatestAsync(CancellationToken cancellationToken = default)
    {
        var update = await _manager.CheckForUpdatesAsync();
        if (update is null)
        {
            return null;
        }

        await _manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
        _downloaded = update;
        return update.TargetFullRelease.Version.ToString();
    }

    public void ApplyOnExit()
    {
        if (_downloaded is not null)
        {
            _manager.WaitExitThenApplyUpdates(_downloaded.TargetFullRelease, silent: true, restart: false);
        }
    }
}
