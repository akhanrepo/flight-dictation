namespace Flight.Core.Vocabulary;

public interface ITermExtractor
{
    /// <summary>
    /// Candidate vocabulary terms in a confirmed dictation's cleaned output:
    /// client names, product names, jargon, acronyms. Returns an empty list
    /// when there's nothing worth learning.
    /// </summary>
    Task<IReadOnlyList<string>> ExtractAsync(string cleanedOutput, CancellationToken cancellationToken = default);
}
