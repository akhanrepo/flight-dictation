namespace Flight.Core.Updates;

/// <summary>
/// Installer/auto-update seam. The shell implements it with Velopack; tests
/// use a fake. Kept to the three steps <see cref="UpdateCheck"/> needs.
/// </summary>
public interface IAppUpdater
{
    /// <summary>
    /// False for dev builds run straight from <c>bin/</c>; only an installed
    /// copy can update itself.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>Downloads the newest release, if any.</summary>
    /// <returns>The downloaded version, or null when already up to date.</returns>
    Task<string?> DownloadLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the downloaded release once the app exits, without restarting
    /// it — an update never interrupts a dictation in progress.
    /// </summary>
    void ApplyOnExit();

    /// <summary>Exits now, applies the downloaded release, and relaunches.</summary>
    void RestartToApply();
}
