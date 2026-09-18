namespace Flight.Core.Stt;

public interface ISpeechToText
{
    /// <param name="samples">16kHz mono PCM audio.</param>
    /// <returns>Raw transcript, unprocessed.</returns>
    Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken = default);
}
