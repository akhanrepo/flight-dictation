using Flight.Core.Languages;
using Flight.Core.Vocabulary;

namespace Flight.Core.Llm;

/// <summary>
/// Transcript cleanup — and, when the language pair calls for it, translation —
/// over whichever <see cref="ILlmCompletion"/> is active. Before inference it
/// pulls the most relevant learned vocabulary and injects it, so client names
/// and jargon survive an imperfect transcription.
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

    public async Task<string> CleanAsync(
        TranscriptCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RawTranscript))
        {
            return string.Empty;
        }

        var terms = await _vocabulary.GetRelevantTermsAsync(request.RawTranscript, cancellationToken);
        var systemPrompt = BuildSystemPrompt(request, terms);

        return await _llm.CompleteAsync(
            new LlmRequest(systemPrompt, request.RawTranscript),
            cancellationToken);
    }

    /// <summary>
    /// With no learned terms and no translation, the prompt is byte-identical
    /// to feature #1's — a same-language pair and an empty store are both
    /// normal states, not degraded ones.
    /// </summary>
    internal static string BuildSystemPrompt(TranscriptCleanupRequest request, IReadOnlyList<string> terms)
    {
        var prompt = request.RequiresTranslation
            ? BuildTranslationPrompt(request)
            : SystemPrompt;

        return terms.Count == 0
            ? prompt
            : $"{prompt}\n\n{VocabularyPreamble}\n{string.Join("\n", terms)}";
    }

    private static string BuildTranslationPrompt(TranscriptCleanupRequest request)
    {
        var source = SupportedLanguages.EnglishNameOf(request.SourceLanguage);
        var target = SupportedLanguages.EnglishNameOf(request.TargetLanguage);

        // Cleanup and translation in one pass: a second round trip would
        // double local inference latency on CPU-only hardware.
        return
            $"You translate raw speech-to-text transcripts from {source} into {target}. " +
            $"The input is a {source} transcript containing filler words, false starts, and repetitions. " +
            $"Remove those, then render the speaker's meaning in fluent, correctly punctuated {target}. " +
            "Preserve the speaker's meaning and all factual content exactly. " +
            "Never add information, never summarize, never answer questions in the transcript. " +
            $"Output only the {target} text, nothing else.";
    }
}
