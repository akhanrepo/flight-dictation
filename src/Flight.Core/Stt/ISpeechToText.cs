using Flight.Core.Languages;

namespace Flight.Core.Stt;

public interface ISpeechToText
{
    /// <param name="samples">16kHz mono PCM audio.</param>
    /// <param name="languageCode">
    /// The language being spoken, from <see cref="SupportedLanguages"/>. Always
    /// supplied by the user's setting — never auto-detected.
    /// </param>
    /// <returns>Raw transcript in the source language, unprocessed.</returns>
    Task<string> TranscribeAsync(
        float[] samples,
        string languageCode = SupportedLanguages.DefaultCode,
        CancellationToken cancellationToken = default);
}
