using Microsoft.Extensions.Logging;

namespace Flight.Core.Vocabulary;

/// <summary>
/// Re-embeds stored terms whose vectors came from a different embedder.
/// Feature #10 moved vocabulary from LLM embeddings to the trigram embedder;
/// without this, entries stored before then would never match again
/// (<see cref="VectorMath.CosineSimilarity"/> drops mismatched widths).
/// Cheap: the trigram embedder is model-free.
/// </summary>
public sealed class VocabularyReembedder
{
    private readonly IVocabularyRepository _vocabulary;
    private readonly IEmbeddingGenerator _embeddings;
    private readonly ILogger<VocabularyReembedder> _logger;

    public VocabularyReembedder(
        IVocabularyRepository vocabulary,
        IEmbeddingGenerator embeddings,
        ILogger<VocabularyReembedder> logger)
    {
        _vocabulary = vocabulary;
        _embeddings = embeddings;
        _logger = logger;
    }

    /// <returns>How many entries were re-embedded. Never throws.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stale = (await _vocabulary.GetAllAsync(cancellationToken))
                .Where(entry => entry.Embedding.Length != _embeddings.Dimensions)
                .ToList();

            foreach (var entry in stale)
            {
                var embedding = await _embeddings.GenerateAsync(entry.Term, cancellationToken);
                await _vocabulary.UpdateEmbeddingAsync(entry.Id, embedding, cancellationToken);
            }

            if (stale.Count > 0)
            {
                _logger.LogInformation("Re-embedded {Count} vocabulary entries", stale.Count);
            }

            return stale.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Re-embedding vocabulary failed; old entries won't match until it succeeds");
            return 0;
        }
    }
}
