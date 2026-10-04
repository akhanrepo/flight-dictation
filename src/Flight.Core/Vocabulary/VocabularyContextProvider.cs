using Microsoft.Extensions.Logging;

namespace Flight.Core.Vocabulary;

/// <summary>
/// Embeds the incoming transcript and pulls the nearest stored terms. Sits in
/// the dictation hot path, so it fails soft: a retrieval error yields no
/// context rather than an error state.
/// </summary>
public class VocabularyContextProvider : IVocabularyContextProvider
{
    private readonly IVocabularyRepository _vocabulary;
    private readonly IEmbeddingGenerator _embeddings;
    private readonly FlightOptions _options;
    private readonly ILogger<VocabularyContextProvider> _logger;

    public VocabularyContextProvider(
        IVocabularyRepository vocabulary,
        IEmbeddingGenerator embeddings,
        FlightOptions options,
        ILogger<VocabularyContextProvider> logger)
    {
        _vocabulary = vocabulary;
        _embeddings = embeddings;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetRelevantTermsAsync(
        string rawTranscript,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawTranscript))
        {
            return [];
        }

        try
        {
            var queryEmbedding = await _embeddings.GenerateAsync(rawTranscript, cancellationToken);
            var matches = await _vocabulary.FindRelevantAsync(
                queryEmbedding,
                _options.VocabularyContextTermCount,
                _options.VocabularyContextMinimumSimilarity,
                cancellationToken);

            // Also filtered on the way out, so terms stored before the rules
            // existed (sentences, mostly) are never injected.
            return matches
                .Select(match => match.Entry.Term)
                .Where(VocabularyTermRules.IsPlausibleTerm)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vocabulary retrieval failed; cleaning without context");
            return [];
        }
    }
}
