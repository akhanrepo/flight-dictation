using Flight.Core;
using Flight.Core.Llm;
using Flight.Core.Vocabulary;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Vocabulary;

/// <summary>
/// Feature #2 acceptance criteria: relevant learned terms reach the LLM
/// cleanup prompt, and an empty store is a normal, working state.
/// </summary>
public class VocabularyContextInjectionTests
{
    private readonly FakeLlmCompletion _llm = new();
    private readonly InMemoryVocabularyRepository _vocabulary = new();
    private readonly HashingEmbeddingGenerator _embeddings = new();
    private readonly FlightOptions _options = new();

    private VocabularyContextProvider CreateProvider() =>
        new(_vocabulary, _embeddings, _options, NullLogger<VocabularyContextProvider>.Instance);

    private async Task StoreTermAsync(string term)
    {
        await _vocabulary.UpsertAsync(new VocabularyEntry
        {
            Term = term,
            Embedding = await _embeddings.GenerateAsync(term),
        });
    }

    // Criterion: relevant entries are retrieved and included in the LLM's
    // context before the cleanup step runs
    [Fact]
    public async Task RelevantTerms_Are_Injected_Into_The_Cleanup_Prompt()
    {
        await StoreTermAsync("Contoso");
        var cleaner = new PromptTextCleaner(_llm, CreateProvider());

        await cleaner.CleanAsync("um we met with contosoh yesterday");

        var request = Assert.Single(_llm.Requests);
        Assert.Contains("Contoso", request.SystemPrompt);
        Assert.Equal("um we met with contosoh yesterday", request.UserPrompt);
    }

    // Criterion: retrieval is semantic/similarity-based, not "inject everything" —
    // an unrelated stored term stays out of the prompt
    [Fact]
    public async Task IrrelevantTerms_Are_Not_Injected()
    {
        await StoreTermAsync("Contoso");
        await StoreTermAsync("zzzqqqxwv");
        var cleaner = new PromptTextCleaner(_llm, CreateProvider());

        await cleaner.CleanAsync("we met with contoso yesterday");

        Assert.Contains("Contoso", _llm.LastRequest!.SystemPrompt);
        Assert.DoesNotContain("zzzqqqxwv", _llm.LastRequest.SystemPrompt);
    }

    // Criterion: retrieval is capped, so the prompt doesn't bloat as the store grows
    [Fact]
    public async Task Retrieval_Is_Capped_At_The_Configured_Term_Count()
    {
        _options.VocabularyContextTermCount = 3;
        _options.VocabularyContextMinimumSimilarity = 0;
        for (var i = 0; i < 10; i++)
        {
            await StoreTermAsync($"Contoso{i}");
        }

        var terms = await CreateProvider().GetRelevantTermsAsync("contoso project update");

        Assert.Equal(3, terms.Count);
    }

    // Criterion: an empty store is not an error — the loop functions with no
    // context injected, and the prompt is unchanged from the no-vocabulary one
    [Fact]
    public async Task EmptyStore_Cleans_Normally_With_No_Context_Injected()
    {
        _llm.ResponseToReturn = "Hello, world.";
        var cleaner = new PromptTextCleaner(_llm, CreateProvider());

        var cleaned = await cleaner.CleanAsync("um hello uh world");

        Assert.Equal("Hello, world.", cleaned);
        Assert.Equal(PromptTextCleaner.SystemPrompt, _llm.LastRequest!.SystemPrompt);
        Assert.DoesNotContain(PromptTextCleaner.VocabularyPreamble, _llm.LastRequest.SystemPrompt);
    }

    // Criterion: injection only affects what the LLM is told — the transcript
    // handed to the model is untouched, and cleaned_output is whatever the LLM
    // returned, still bound for feature #1's preview-and-confirm
    [Fact]
    public async Task Injection_Does_Not_Alter_The_Transcript_Or_The_Result()
    {
        await StoreTermAsync("Contoso");
        _llm.ResponseToReturn = "We met with Contoso yesterday.";
        var cleaner = new PromptTextCleaner(_llm, CreateProvider());

        var cleaned = await cleaner.CleanAsync("um we met with contoso yesterday");

        Assert.Equal("um we met with contoso yesterday", _llm.LastRequest!.UserPrompt);
        Assert.Equal("We met with Contoso yesterday.", cleaned);
    }

    // Retrieval sits in the dictation hot path: a store failure degrades to
    // "no context", it does not fail the dictation
    [Fact]
    public async Task RetrievalFailure_Degrades_To_No_Context()
    {
        var provider = new VocabularyContextProvider(
            new ThrowingVocabularyRepository(), _embeddings, _options,
            NullLogger<VocabularyContextProvider>.Instance);
        var cleaner = new PromptTextCleaner(_llm, provider);

        var cleaned = await cleaner.CleanAsync("um hello uh world");

        Assert.Equal("Hello, world.", cleaned);
        Assert.Equal(PromptTextCleaner.SystemPrompt, _llm.LastRequest!.SystemPrompt);
    }

    private sealed class ThrowingVocabularyRepository : IVocabularyRepository
    {
        public Task<VocabularyEntry> UpsertAsync(VocabularyEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("store unavailable");

        public Task<IReadOnlyList<VocabularyMatch>> FindRelevantAsync(
            float[] queryEmbedding, int topK, double minimumSimilarity, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("store unavailable");

        public Task<IReadOnlyList<VocabularyEntry>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("store unavailable");
    }
}
