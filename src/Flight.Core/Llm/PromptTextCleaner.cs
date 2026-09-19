using Flight.Core.Vocabulary;

namespace Flight.Core.Llm;

/// <summary>
/// Transcript cleanup over whichever <see cref="ILlmCompletion"/> is active.
/// Before inference it pulls the most relevant learned vocabulary and injects
/// it, so client names and jargon survive an imperfect transcription.
/// </summary>
public class PromptTextCleaner : ITextCleaner
{
    internal const string SystemPrompt =
        "You clean up raw speech-to-text transcripts. Remove filler words (um, uh, like, you know), " +
        "false starts, and repetitions. Fix punctuation, capitalization, and sentence structure. " +
        "Preserve the speaker's meaning, wording, and all factual content exactly. " +
        "Never add information, never summarize, never answer questions in the transcript. " +
        "Output only the cleaned text, nothing else.";

    internal const string VocabularyPreamble =
        "The speaker frequently uses these terms. If the transcript contains a near-miss of one of " +
        "them, correct it to the exact spelling below. Do not insert a term that is not being said:";

    private readonly ILlmCompletion _llm;
    private readonly IVocabularyContextProvider _vocabulary;

    public PromptTextCleaner(ILlmCompletion llm, IVocabularyContextProvider vocabulary)
    {
        _llm = llm;
        _vocabulary = vocabulary;
    }

    public async Task<string> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawTranscript))
        {
            return string.Empty;
        }

        var terms = await _vocabulary.GetRelevantTermsAsync(rawTranscript, cancellationToken);
        var systemPrompt = BuildSystemPrompt(terms);

        return await _llm.CompleteAsync(new LlmRequest(systemPrompt, rawTranscript), cancellationToken);
    }

    /// <summary>
    /// With no learned terms the prompt is byte-identical to the pre-vocabulary
    /// one — an empty store is a normal state, not a degraded one.
    /// </summary>
    internal static string BuildSystemPrompt(IReadOnlyList<string> terms) =>
        terms.Count == 0
            ? SystemPrompt
            : $"{SystemPrompt}\n\n{VocabularyPreamble}\n{string.Join("\n", terms)}";
}
