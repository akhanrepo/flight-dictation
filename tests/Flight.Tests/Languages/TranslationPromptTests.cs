using Flight.Core.Llm;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Languages;

/// <summary>
/// Feature #3: what the LLM cleanup step is actually told for each language
/// pair. Vocabulary injection (feature #2) must keep working alongside it.
/// </summary>
public class TranslationPromptTests
{
    private readonly FakeLlmCompletion _llm = new();
    private readonly FakeVocabularyContextProvider _vocabulary = new();

    private PromptTextCleaner CreateCleaner() => new(_llm, _vocabulary);

    // Criterion: same source and target behaves like feature #1 — the prompt is
    // the unmodified cleanup prompt, with no translation instruction
    [Fact]
    public async Task SameLanguage_Uses_The_Feature1_Cleanup_Prompt_Verbatim()
    {
        await CreateCleaner().CleanAsync(new TranscriptCleanupRequest("um hello uh world", "en", "en"));

        Assert.Equal(PromptTextCleaner.SystemPrompt, _llm.LastRequest!.SystemPrompt);
    }

    // Criterion: differing languages instruct a translation into the target
    [Fact]
    public async Task DifferentLanguages_Instruct_Translation_By_Name()
    {
        await CreateCleaner().CleanAsync(new TranscriptCleanupRequest("kuch bhi", "ur", "en"));

        var prompt = _llm.LastRequest!.SystemPrompt;
        Assert.Contains("Urdu", prompt);
        Assert.Contains("English", prompt);
        Assert.Contains("translate", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // Criterion: reverse direction produces the reverse instruction
    [Fact]
    public async Task ReverseDirection_Instructs_The_Reverse_Translation()
    {
        await CreateCleaner().CleanAsync(new TranscriptCleanupRequest("hello world", "en", "zh"));

        var prompt = _llm.LastRequest!.SystemPrompt;
        Assert.Contains("from English into Mandarin", prompt);
    }

    // Cleanup work is not dropped when translating: fillers still go
    [Fact]
    public async Task TranslationPrompt_Still_Asks_For_Cleanup()
    {
        await CreateCleaner().CleanAsync(new TranscriptCleanupRequest("eh hola", "es", "en"));

        var prompt = _llm.LastRequest!.SystemPrompt;
        Assert.Contains("filler words", prompt);
        Assert.Contains("Never add information", prompt);
    }

    // Feature #2 and #3 compose: vocabulary still reaches a translating prompt
    [Fact]
    public async Task VocabularyContext_Is_Injected_When_Translating_Too()
    {
        _vocabulary.TermsToReturn = ["Contoso"];

        await CreateCleaner().CleanAsync(new TranscriptCleanupRequest("kuch bhi", "ur", "en"));

        Assert.Contains("Contoso", _llm.LastRequest!.SystemPrompt);
        Assert.Contains(PromptTextCleaner.VocabularyPreamble, _llm.LastRequest.SystemPrompt);
    }

    // The transcript itself is never rewritten before inference
    [Fact]
    public async Task Transcript_Is_Passed_To_The_Model_Unmodified()
    {
        await CreateCleaner().CleanAsync(new TranscriptCleanupRequest("eh hola que tal", "es", "en"));

        Assert.Equal("eh hola que tal", _llm.LastRequest!.UserPrompt);
    }

    [Fact]
    public async Task EmptyTranscript_Short_Circuits_Without_Calling_The_Model()
    {
        var cleaned = await CreateCleaner().CleanAsync(new TranscriptCleanupRequest("   ", "fr", "en"));

        Assert.Equal(string.Empty, cleaned);
        Assert.Empty(_llm.Requests);
    }
}
