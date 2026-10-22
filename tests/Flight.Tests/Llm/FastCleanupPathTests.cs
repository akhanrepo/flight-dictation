using Flight.Core.Llm;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Llm;

/// <summary>Feature #10: when cleanup skips the language model.</summary>
public class FastCleanupPathTests
{
    private readonly FakeLlmCompletion _llm = new() { ResponseToReturn = "From the model." };
    private readonly FakeVocabularyContextProvider _vocabulary = new();

    private PromptTextCleaner Cleaner() => new(_llm, _vocabulary);

    // Criterion: under four words, not translating — rules only, no LLM call
    [Theory]
    [InlineData("yes thanks")]
    [InlineData("um sounds good")]
    [InlineData("Okay.")]
    public async Task ShortUtterance_IsTidiedWithoutTheModel(string transcript)
    {
        var cleaned = await Cleaner().CleanAsync(new TranscriptCleanupRequest(transcript));

        Assert.Empty(_llm.Requests);
        Assert.Equal(CleanupGuard.FallbackClean(transcript), cleaned);
    }

    [Fact]
    public async Task FourWords_GoToTheModel()
    {
        await Cleaner().CleanAsync(new TranscriptCleanupRequest("send the report today"));

        Assert.Single(_llm.Requests);
    }

    // Translation always needs the model, however short
    [Fact]
    public async Task ShortUtterance_StillTranslates()
    {
        var cleaned = await Cleaner().CleanAsync(new TranscriptCleanupRequest("hola", "es", "en"));

        Assert.Single(_llm.Requests);
        Assert.Equal("From the model.", cleaned);
    }

    // Criterion: AI cleanup off, not translating — rules only
    [Fact]
    public async Task AiCleanupOff_SkipsTheModel()
    {
        const string transcript = "um so the quarterly numbers look good";

        var cleaned = await Cleaner().CleanAsync(new TranscriptCleanupRequest(transcript, AiCleanup: false));

        Assert.Empty(_llm.Requests);
        Assert.Equal("So the quarterly numbers look good.", cleaned);
    }

    // Criterion: translation always uses the LLM
    [Fact]
    public async Task AiCleanupOff_StillTranslates()
    {
        await Cleaner().CleanAsync(new TranscriptCleanupRequest("bonjour tout le monde", "fr", "en", AiCleanup: false));

        Assert.Single(_llm.Requests);
    }
}
