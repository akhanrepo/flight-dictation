namespace Flight.Core.Vocabulary;

/// <summary>
/// Supplies the vocabulary terms most relevant to a transcript, for injection
/// into the LLM cleanup prompt. Never touches the clipboard or the transcript
/// itself — it only shapes what the LLM is told.
/// </summary>
public interface IVocabularyContextProvider
{
    /// <returns>Relevant terms, most relevant first. Empty when the store is empty.</returns>
    Task<IReadOnlyList<string>> GetRelevantTermsAsync(string rawTranscript, CancellationToken cancellationToken = default);
}
