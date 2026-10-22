namespace Flight.Core.Vocabulary;

/// <summary>
/// A term learned automatically from a delivered dictation. Matches the PRD
/// data model (term, embedding, frequency, last_used_at, source). There is no
/// manual-entry path by design: every row originates from a real dictation.
/// </summary>
public record VocabularyEntry
{
    public long Id { get; init; }

    public required string Term { get; init; }

    /// <summary>Embedding used for similarity retrieval. Never empty in stored rows.</summary>
    public required float[] Embedding { get; init; }

    /// <summary>How many dictations this term has appeared in.</summary>
    public int Frequency { get; init; } = 1;

    public DateTimeOffset LastUsedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Id of the <c>dictation_sessions</c> row the term was first seen in.</summary>
    public long SourceSessionId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A retrieval hit: the stored entry plus its cosine similarity to the query.</summary>
public record VocabularyMatch(VocabularyEntry Entry, double Similarity);
