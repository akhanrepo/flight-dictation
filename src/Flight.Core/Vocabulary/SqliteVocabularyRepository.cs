using System.Globalization;
using Flight.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Flight.Core.Vocabulary;

/// <summary>
/// SQLite-backed vocabulary store. Embeddings are held as float32 BLOBs and
/// ranked by cosine similarity in-process.
/// </summary>
/// <remarks>
/// The PRD names sqlite-vec for semantic search. sqlite-vec is a native
/// loadable extension requiring a per-RID binary, which would put a native
/// download in the test path — against the project's "no tests that require
/// downloaded models" rule. Ranking therefore happens here, behind
/// <see cref="IVocabularyRepository.FindRelevantAsync"/>, so moving to a
/// <c>vec0</c> virtual table later is a change inside this one class.
/// </remarks>
public class SqliteVocabularyRepository : IVocabularyRepository
{
    private readonly FlightDatabase _database;

    public SqliteVocabularyRepository(FlightDatabase database)
    {
        _database = database;
    }

    public Task<VocabularyEntry> UpsertAsync(VocabularyEntry entry, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();

        // ON CONFLICT on the case-insensitive unique index gives us
        // "new term inserts, repeat term increments" in a single statement.
        command.CommandText = """
            INSERT INTO vocabulary_entries
                (term, embedding, frequency, last_used_at, source_session_id, created_at)
            VALUES ($term, $embedding, $frequency, $lastUsedAt, $source, $createdAt)
            ON CONFLICT (term COLLATE NOCASE) DO UPDATE SET
                frequency = frequency + 1,
                last_used_at = excluded.last_used_at,
                embedding = excluded.embedding
            RETURNING id, term, embedding, frequency, last_used_at, source_session_id, created_at;
            """;
        command.Parameters.AddWithValue("$term", entry.Term);
        command.Parameters.AddWithValue("$embedding", EmbeddingBlob.ToBlob(entry.Embedding));
        command.Parameters.AddWithValue("$frequency", entry.Frequency);
        command.Parameters.AddWithValue("$lastUsedAt", Format(entry.LastUsedAt));
        command.Parameters.AddWithValue("$source", entry.SourceSessionId);
        command.Parameters.AddWithValue("$createdAt", Format(entry.CreatedAt));

        using var reader = command.ExecuteReader();
        reader.Read();
        return Task.FromResult(Read(reader));
    }

    public async Task<IReadOnlyList<VocabularyMatch>> FindRelevantAsync(
        float[] queryEmbedding,
        int topK,
        double minimumSimilarity,
        CancellationToken cancellationToken = default)
    {
        if (topK <= 0 || queryEmbedding.Length == 0)
        {
            return [];
        }

        var all = await GetAllAsync(cancellationToken);
        return all
            .Select(entry => new VocabularyMatch(entry, VectorMath.CosineSimilarity(queryEmbedding, entry.Embedding)))
            .Where(match => match.Similarity >= minimumSimilarity)
            // Frequency breaks ties so the terms the user actually repeats win.
            .OrderByDescending(match => match.Similarity)
            .ThenByDescending(match => match.Entry.Frequency)
            .Take(topK)
            .ToList();
    }

    public Task<IReadOnlyList<VocabularyEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, term, embedding, frequency, last_used_at, source_session_id, created_at
            FROM vocabulary_entries
            ORDER BY id;
            """;

        var entries = new List<VocabularyEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(Read(reader));
        }

        return Task.FromResult<IReadOnlyList<VocabularyEntry>>(entries);
    }

    public Task UpdateEmbeddingAsync(long id, float[] embedding, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE vocabulary_entries SET embedding = $embedding WHERE id = $id;";
        command.Parameters.AddWithValue("$embedding", EmbeddingBlob.ToBlob(embedding));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private static VocabularyEntry Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Term = reader.GetString(1),
        Embedding = EmbeddingBlob.FromBlob((byte[])reader[2]),
        Frequency = reader.GetInt32(3),
        LastUsedAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
        SourceSessionId = reader.GetInt64(5),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
    };

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
