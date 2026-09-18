namespace Flight.Core.Audio;

/// <summary>
/// Push-to-talk audio capture. No audio is buffered outside the
/// Start/Stop window. Samples are 16kHz mono PCM floats (whisper's input format).
/// </summary>
public interface IAudioCapturer
{
    void Start();
    float[] Stop();
}
