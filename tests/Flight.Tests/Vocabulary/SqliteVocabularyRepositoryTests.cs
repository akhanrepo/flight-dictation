using Flight.Core.Storage;
using Flight.Core.Vocabulary;
using Xunit;

namespace Flight.Tests.Vocabulary;

/// <summary>
/// Feature #2 storage criteria, against real SQLite: upsert semantics and
/// similarity retrieval, including the empty-store case.
/// </summary>
public class SqliteVocabularyRepositoryTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"flight-vocab-{Guid.NewGuid():N}.db");

    private readonly SqliteVocabularyRepository _repository;
    private readonly HashingEmbeddingGenerator _embeddings = new();

    public SqliteVocabularyRepositoryTests()
    {
        _repository = new SqliteVocabularyRepository(new FlightDatabase($"Data Source={_databasePath}"));
    }

    private async Task<VocabularyEntry> EntryAsync(string term, long sourceSessionId = 1) => new()
    {
        Term = term,
        Embedding = await _embeddings.GenerateAsync(term),
        SourceSessionId = sourceSessionId,
    };

    // Criterion: a new term creates a row, round-tripping every field
    [Fact]
    public async Task NewTerm_Is_Persisted_With_Its_Embedding()
    {
        var stored = await _repository.UpsertAsync(await EntryAsync("Contoso", sourceSessionId: 42));

        Assert.True(stored.Id > 0);
        var all = await _repository.GetAllAsync();
        var entry = Assert.Single(all);
        Assert.Equal("Contoso", entry.Term);
        Assert.Equal(1, entry.Frequency);
        Assert.Equal(42, entry.SourceSessionId);
        Assert.Equal(_embeddings.Dimensions, entry.Embedding.Length);
    }

    // Criterion: repeated terms increment frequency and update last_used_at
    // rather than duplicating
    [Fact]
    public async Task RepeatedTerm_Increments_Frequency_Instead_Of_Duplicating()
    {
        await _repository.UpsertAsync(await EntryAsync("Contoso"));
        var later = DateTimeOffset.UtcNow.AddMinutes(5);
        await _repository.UpsertAsync((await EntryAsync("Contoso")) with { LastUsedAt = later });

        var all = await _repository.GetAllAsync();
        var entry = Assert.Single(all);
        Assert.Equal(2, entry.Frequency);
        Assert.Equal(later.ToUnixTimeSeconds(), entry.LastUsedAt.ToUnixTimeSeconds());
    }

    // Dedup is exact-string, case-insensitive (feature #2's stated scope)
    [Fact]
    public async Task TermDedup_Is_Case_Insensitive()
    {
        await _repository.UpsertAsync(await EntryAsync("Contoso"));
        await _repository.UpsertAsync(await EntryAsync("contoso"));

        var all = await _repository.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(2, all[0].Frequency);
    }

    // Criterion: retrieval returns the most relevant rows, ranked
    [Fact]
    public async Task FindRelevant_Ranks_By_Similarity()
    {
        await _repository.UpsertAsync(await EntryAsync("Contoso"));
        await _repository.UpsertAsync(await EntryAsync("Fabrikam"));

        var query = await _embeddings.GenerateAsync("Contoso");
        var matches = await _repository.FindRelevantAsync(query, topK: 2, minimumSimilarity: 0);

        Assert.Equal("Contoso", matches[0].Entry.Term);
        Assert.True(matches[0].Similarity > matches[1].Similarity);
    }

    // Criterion: the similarity floor keeps unrelated terms out of the prompt
    [Fact]
    public async Task FindRelevant_Excludes_Matches_Below_The_Similarity_Floor()
    {
        await _repository.UpsertAsync(await EntryAsync("Contoso"));

        var query = await _embeddings.GenerateAsync("zzzqqqxwv");
        var matches = await _repository.FindRelevantAsync(query, topK: 5, minimumSimilarity: 0.9);

        Assert.Empty(matches);
    }

    // Criterion: an empty store returns nothing and is not an error state
    [Fact]
    public async Task EmptyStore_Returns_No_Matches_Without_Throwing()
    {
        var query = await _embeddings.GenerateAsync("anything at all");

        var matches = await _repository.FindRelevantAsync(query, topK: 5, minimumSimilarity: 0);

        Assert.Empty(matches);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
        GC.SuppressFinalize(this);
    }
}
