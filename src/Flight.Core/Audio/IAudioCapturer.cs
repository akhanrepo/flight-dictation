namespace Flight.Core.Audio;

/// <summary>
/// Push-to-talk audio capture. No audio is buffered outside the
/// Start/Stop window. Samples are 16kHz mono PCM floats (whisper's input format).
/// </summary>
public interface IAudioCapturer
{
    /// <summary>
    /// RMS level (0–1) of each captured buffer, for the listening indicator.
    /// Raised on the capture thread.
    /// </summary>
    event Action<float>? LevelChanged;

    void Start();
    float[] Stop();
}
