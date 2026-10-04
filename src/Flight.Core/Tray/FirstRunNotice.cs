namespace Flight.Core.Tray;

/// <summary>
/// The one-time "Flight is running in the notification area" notice. Claimed
/// through a marker file in the data directory, which survives reinstalls, so
/// it really is once per machine.
/// </summary>
public sealed class FirstRunNotice
{
    private readonly string _markerPath;

    public FirstRunNotice(FlightOptions options) =>
        _markerPath = Path.Combine(options.DataDirectory, "first-run-notice-shown");

    public const string Title = "Flight is running";

    public const string Message = "Find it in the notification area. Hold Right Alt to dictate.";

    /// <returns>True exactly once; the caller shows the notice then.</returns>
    public bool TryClaim()
    {
        if (File.Exists(_markerPath))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!); // path is DataDirectory/<file>, never a root
        File.WriteAllText(_markerPath, DateTimeOffset.UtcNow.ToString("O"));
        return true;
    }
}
