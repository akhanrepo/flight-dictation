using Flight.Core.Vocabulary;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Vocabulary;

/// <summary>Feature #10: entries stored with the old LLM embeddings are re-embedded once.</summary>
public class VocabularyReembedderTests
{
    private readonly InMemoryVocabularyRepository _vocabulary = new();
    private readonly HashingEmbeddingGenerator _embeddings = new();

    private VocabularyReembedder Create() =>
        new(_vocabulary, _embeddings, NullLogger<VocabularyReembedder>.Instance);

    private async Task StoreAsync(string term, float[] embedding) =>
        await _vocabulary.UpsertAsync(new VocabularyEntry { Term = term, Embedding = embedding });

    [Fact]
    public async Task Reembedder_ReplacesVectorsOfTheWrongWidth()
    {
        await StoreAsync("Contoso", new float[3072]); // an LLM-sized vector
        await StoreAsync("Fabrikam", await _embeddings.GenerateAsync("Fabrikam"));

        var count = await Create().RunAsync();

        Assert.Equal(1, count);
        Assert.All(_vocabulary.Entries, e => Assert.Equal(_embeddings.Dimensions, e.Embedding.Length));
        Assert.Equal(await _embeddings.GenerateAsync("Contoso"), _vocabulary.Entries[0].Embedding);
    }

    // Once migrated there's nothing left to do on the next launch
    [Fact]
    public async Task Reembedder_IsANoOpTheSecondTime()
    {
        await StoreAsync("Contoso", new float[3072]);
        var reembedder = Create();

        await reembedder.RunAsync();

        Assert.Equal(0, await reembedder.RunAsync());
    }

    // Re-embedded entries are found by retrieval again
    [Fact]
    public async Task ReembeddedEntries_AreRetrievableAgain()
    {
        await StoreAsync("Contoso", new float[3072]);
        var query = await _embeddings.GenerateAsync("contoso");
        Assert.Empty(await _vocabulary.FindRelevantAsync(query, 5, 0.25));

        await Create().RunAsync();

        Assert.Single(await _vocabulary.FindRelevantAsync(query, 5, 0.25));
    }
}
