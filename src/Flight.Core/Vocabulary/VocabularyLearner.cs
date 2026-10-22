using Flight.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Flight.Core.Vocabulary;

/// <summary>
/// The extract-and-store half of feature #2: takes a delivered dictation,
/// pulls candidate terms out of its cleaned output, and upserts them. Invoked
/// off the dictation loop by <see cref="IVocabularyLearningQueue"/>.
/// </summary>
public class VocabularyLearner
{
    private readonly ITermExtractor _extractor;
    private readonly IEmbeddingGenerator _embeddings;
    private readonly IVocabularyRepository _vocabulary;
    private readonly ILogger<VocabularyLearner> _logger;

    public VocabularyLearner(
        ITermExtractor extractor,
        IEmbeddingGenerator embeddings,
        IVocabularyRepository vocabulary,
        ILogger<VocabularyLearner> logger)
    {
        _extractor = extractor;
        _embeddings = embeddings;
        _vocabulary = vocabulary;
        _logger = logger;
    }

    public async Task LearnAsync(DictationSession session, CancellationToken cancellationToken = default)
    {
        var terms = await _extractor.ExtractAsync(session.CleanedOutput, cancellationToken);
        if (terms.Count == 0)
        {
            return;
        }

        foreach (var term in terms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var embedding = await _embeddings.GenerateAsync(term, cancellationToken);
            if (embedding.Length == 0)
            {
                _logger.LogWarning("Skipping vocabulary term {Term}: empty embedding", term);
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            await _vocabulary.UpsertAsync(
                new VocabularyEntry
                {
                    Term = term,
                    Embedding = embedding,
                    Frequency = 1,
                    LastUsedAt = now,
                    CreatedAt = now,
                    SourceSessionId = session.Id,
                },
                cancellationToken);
        }
    }
}
