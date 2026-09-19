namespace Flight.Core.Settings;

/// <summary>
/// Persisted user preferences. <see cref="Current"/> is read synchronously at
/// the start of each dictation, so an update mid-session takes effect on the
/// next one without a restart.
/// </summary>
public interface ISettingsStore
{
    FlightSettings Current { get; }

    Task<FlightSettings> UpdateAsync(FlightSettings settings, CancellationToken cancellationToken = default);
}
