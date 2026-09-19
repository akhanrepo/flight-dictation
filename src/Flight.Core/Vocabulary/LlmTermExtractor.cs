using Flight.Core.Llm;

namespace Flight.Core.Vocabulary;

/// <summary>
/// Term extraction by reusing the LLM that's already loaded for cleanup,
/// rather than standing up a second NLP pipeline. Runs on the background
/// learning queue, so its latency never lands on the dictation loop.
/// </summary>
public sealed class LlmTermExtractor : ITermExtractor
{
    private const string SystemPrompt =
        "You extract domain vocabulary from text. List proper nouns, client and company names, " +
        "product names, acronyms, and technical jargon that a speech-to-text system would be likely " +
        "to mis-transcribe. Copy each term exactly as it appears in the text. " +
        "Output one term per line, nothing else. No numbering, no commentary, no explanations. " +
        "If there are no such terms, output nothing.";

    private const int MaxTermLength = 60;
    private const int MaxTermsPerDictation = 20;

    private readonly ILlmCompletion _llm;

    public LlmTermExtractor(ILlmCompletion llm)
    {
        _llm = llm;
    }

    public async Task<IReadOnlyList<string>> ExtractAsync(string cleanedOutput, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cleanedOutput))
        {
            return [];
        }

        var response = await _llm.CompleteAsync(
            new LlmRequest(SystemPrompt, cleanedOutput) { MaxTokens = 256 },
            cancellationToken);

        return Parse(response);
    }

    /// <summary>
    /// Defensive parse: a small local model will sometimes ignore "one per
    /// line, nothing else" and emit bullets, numbering, or a preamble.
    /// </summary>
    internal static IReadOnlyList<string> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return [];
        }

        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in response.Split('\n'))
        {
            var term = line.Trim().TrimStart('-', '*', '•').Trim();

            // Strip "1." / "2)" style numbering the model may add.
            var dot = term.IndexOfAny(['.', ')']);
            if (dot is > 0 and <= 2 && term[..dot].All(char.IsDigit))
            {
                term = term[(dot + 1)..].Trim();
            }

            term = term.Trim('"', '\'', ',', ';', ':').Trim();

            if (term.Length == 0 || term.Length > MaxTermLength)
            {
                continue;
            }

            // A term has to contain a letter; pure punctuation or digits are noise.
            if (!term.Any(char.IsLetter))
            {
                continue;
            }

            if (seen.Add(term))
            {
                terms.Add(term);
            }

            if (terms.Count == MaxTermsPerDictation)
            {
                break;
            }
        }

        return terms;
    }
}
