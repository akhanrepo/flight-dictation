using Flight.Core.Languages;

namespace Flight.Core.Llm;

/// <param name="RawTranscript">STT output, in the source language.</param>
/// <param name="SourceLanguage">Language the transcript is in.</param>
/// <param name="TargetLanguage">
/// Language the output must be in. Equal to the source for cleanup-only.
/// </param>
/// <param name="LlmMode">
/// Engine to run this cleanup on, captured when the dictation started.
/// </param>
public record TranscriptCleanupRequest(
    string RawTranscript,
    string SourceLanguage = SupportedLanguages.DefaultCode,
    string TargetLanguage = SupportedLanguages.DefaultCode,
    LlmMode LlmMode = LlmMode.Local)
{
    public bool RequiresTranslation =>
        !string.Equals(SourceLanguage, TargetLanguage, StringComparison.OrdinalIgnoreCase);
}

public interface ITextCleaner
{
    /// <summary>
    /// Removes filler words and fixes punctuation/structure without altering
    /// semantic content, and — when source and target differ — renders the
    /// result in the target language. Implementations must not add or invent
    /// content.
    /// </summary>
    Task<string> CleanAsync(TranscriptCleanupRequest request, CancellationToken cancellationToken = default);
}
