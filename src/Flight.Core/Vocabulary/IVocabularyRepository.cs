namespace Flight.Core.Vocabulary;

public interface IVocabularyRepository
{
    /// <summary>
    /// Inserts <paramref name="entry"/>, or — when its term already exists
    /// (exact, case-insensitive match) — increments that row's frequency and
    /// refreshes <c>last_used_at</c> instead of creating a duplicate.
    /// </summary>
    Task<VocabularyEntry> UpsertAsync(VocabularyEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// The most similar stored terms to <paramref name="queryEmbedding"/>,
    /// highest similarity first. Entries below <paramref name="minimumSimilarity"/>
    /// are excluded. An empty store yields an empty list, not an error.
    /// </summary>
    Task<IReadOnlyList<VocabularyMatch>> FindRelevantAsync(
        float[] queryEmbedding,
        int topK,
        double minimumSimilarity,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VocabularyEntry>> GetAllAsync(CancellationToken cancellationToken = default);
}
