namespace Flight.Core.Storage;

/// <summary>
/// One completed, user-confirmed dictation. Matches the PRD data model;
/// feature #2 (vocabulary building) reads directly from these rows.
/// </summary>
public record DictationSession
{
    public long Id { get; init; }
    public required string RawTranscript { get; init; }
    public required string CleanedOutput { get; init; }
    public string SourceLanguage { get; init; } = "en";
    public string TargetLanguage { get; init; } = "en";
    public string LlmMode { get; init; } = "local";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
