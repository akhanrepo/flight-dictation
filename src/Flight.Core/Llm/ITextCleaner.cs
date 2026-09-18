namespace Flight.Core.Llm;

public interface ITextCleaner
{
    /// <summary>
    /// Removes filler words and fixes punctuation/structure without altering
    /// semantic content. Implementations must not add or invent content.
    /// </summary>
    Task<string> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default);
}
