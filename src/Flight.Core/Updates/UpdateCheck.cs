using Microsoft.Extensions.Logging;

namespace Flight.Core.Updates;

/// <summary>
/// Runs once at startup: downloads any newer release in the background and
/// stages it to apply on exit. Never throws — a failed update check must not
/// affect dictation.
/// </summary>
public class UpdateCheck
{
    private readonly IAppUpdater _updater;
    private readonly ILogger<UpdateCheck> _logger;

    public UpdateCheck(IAppUpdater updater, ILogger<UpdateCheck> logger)
    {
        _updater = updater;
        _logger = logger;
    }

    /// <returns>The version staged for next launch, or null.</returns>
    public async Task<string?> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_updater.IsInstalled)
        {
            return null;
        }

        try
        {
            var version = await _updater.DownloadLatestAsync(cancellationToken);
            if (version is null)
            {
                return null;
            }

            _updater.ApplyOnExit();
            _logger.LogInformation("Flight {Version} downloaded; it will be applied on exit", version);
            return version;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            return null;
        }
    }
}
