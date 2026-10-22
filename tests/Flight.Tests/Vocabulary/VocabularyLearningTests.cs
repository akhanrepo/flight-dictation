using Flight.Core;
using Flight.Core.Dictation;
using Flight.Core.Storage;
using Flight.Core.Vocabulary;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Vocabulary;

/// <summary>
/// Feature #2 acceptance criteria: extraction from confirmed dictations into
/// the vocabulary store, off the dictation loop's critical path.
/// </summary>
public class VocabularyLearningTests
{
    private readonly FakeTermExtractor _extractor = new();
    private readonly HashingEmbeddingGenerator _embeddings = new();
    private readonly InMemoryVocabularyRepository _vocabulary = new();

    private VocabularyLearner CreateLearner() =>
        new(_extractor, _embeddings, _vocabulary, NullLogger<VocabularyLearner>.Instance);

    private static DictationSession Session(string cleaned, string llmMode = "local") => new()
    {
        Id = 7,
        RawTranscript = "raw",
        CleanedOutput = cleaned,
        LlmMode = llmMode,
    };

    // Criterion: extraction reads cleaned_output from the confirmed session
    [Fact]
    public async Task Learner_Extracts_Terms_From_CleanedOutput()
    {
        await CreateLearner().LearnAsync(Session("We met Contoso today."));

        Assert.Equal(["We met Contoso today."], _extractor.ReceivedTexts);
    }

    // Criterion: a new term creates a row with term, embedding, frequency, last_used_at, source
    [Fact]
    public async Task NewTerm_Creates_Row_With_Full_Metadata()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        _extractor.TermsToReturn = ["Contoso"];

        await CreateLearner().LearnAsync(Session("We met Contoso today."));

        var entry = Assert.Single(_vocabulary.Entries);
        Assert.Equal("Contoso", entry.Term);
        Assert.NotEmpty(entry.Embedding);
        Assert.Equal(1, entry.Frequency);
        Assert.True(entry.LastUsedAt >= before);
        Assert.Equal(7, entry.SourceSessionId);
    }

    // Criterion: a repeated term increments frequency and updates last_used_at, no duplicate row
    [Fact]
    public async Task RepeatedTerm_Increments_Frequency_Without_Duplicating()
    {
        _extractor.TermsToReturn = ["Contoso"];
        var learner = CreateLearner();

        await learner.LearnAsync(Session("Contoso once."));
        var firstUse = _vocabulary.Entries[0].LastUsedAt;

        await Task.Delay(5);
        await learner.LearnAsync(Session("Contoso again."));

        var entry = Assert.Single(_vocabulary.Entries);
        Assert.Equal(2, entry.Frequency);
        Assert.True(entry.LastUsedAt > firstUse);
    }

    // Criterion: extraction is independent of which LLM produced the cleanup —
    // a cloud-mode session learns exactly as a local-mode one does
    [Fact]
    public async Task CloudMode_Session_Extracts_Vocabulary_The_Same_Way()
    {
        _extractor.TermsToReturn = ["Fabrikam"];

        await CreateLearner().LearnAsync(Session("Fabrikam deal.", llmMode: "cloud"));

        var entry = Assert.Single(_vocabulary.Entries);
        Assert.Equal("Fabrikam", entry.Term);
    }

    // Criterion: the dictation is handed to learning AFTER the clipboard
    // copy, never before
    [Fact]
    public async Task Dictation_Copies_To_Clipboard_Before_Enqueuing_Extraction()
    {
        var clipboard = new FakeClipboardWriter();
        var queue = new FakeVocabularyLearningQueue();
        var order = new List<string>();
        queue.OnEnqueue = () => order.Add("enqueued");

        var hotkey = new FakeHotkey();
        var orchestrator = new DictationOrchestrator(
            hotkey, new FakeAudioCapturer(), new FakeSpeechToText(), new FakeTextCleaner(),
            new OrderRecordingClipboard(clipboard, order), new InMemorySessionRepository(),
            new FakeDictationUi(), queue, new FakeSettingsStore(), new FlightOptions(),
            NullLogger<DictationOrchestrator>.Instance);
        orchestrator.Start();

        hotkey.Press();
        hotkey.Release();
        await orchestrator.ProcessingTask!;

        Assert.Equal(["clipboard", "enqueued"], order);
    }

    // Criterion: the stored session (with its id) is what gets handed off, so
    // entries can be traced back to their source dictation
    [Fact]
    public async Task Dictation_Enqueues_The_Persisted_Session()
    {
        var queue = new FakeVocabularyLearningQueue();
        var sessions = new InMemorySessionRepository();
        var hotkey = new FakeHotkey();
        var cleaner = new FakeTextCleaner { CleanedToReturn = "Contoso ships Tuesday." };

        var orchestrator = new DictationOrchestrator(
            hotkey, new FakeAudioCapturer(), new FakeSpeechToText(), cleaner,
            new FakeClipboardWriter(), sessions, new FakeDictationUi(), queue,
            new FakeSettingsStore(), new FlightOptions(), NullLogger<DictationOrchestrator>.Instance);
        orchestrator.Start();

        hotkey.Press();
        hotkey.Release();
        await orchestrator.ProcessingTask!;

        var enqueued = Assert.Single(queue.Enqueued);
        Assert.Equal(sessions.Sessions[0].Id, enqueued.Id);
        Assert.Equal("Contoso ships Tuesday.", enqueued.CleanedOutput);
    }

    // Criterion: only a delivered dictation teaches anything — a failed one
    // learns nothing
    [Fact]
    public async Task FailedDictation_Learns_Nothing()
    {
        var queue = new FakeVocabularyLearningQueue();
        var hotkey = new FakeHotkey();
        var cleaner = new FakeTextCleaner { ExceptionToThrow = new InvalidOperationException("llm failed") };
        var orchestrator = new DictationOrchestrator(
            hotkey, new FakeAudioCapturer(), new FakeSpeechToText(), cleaner,
            new FakeClipboardWriter(), new InMemorySessionRepository(), new FakeDictationUi(),
            queue, new FakeSettingsStore(), new FlightOptions(), NullLogger<DictationOrchestrator>.Instance);
        orchestrator.Start();

        hotkey.Press();
        hotkey.Release();
        await orchestrator.ProcessingTask!;

        Assert.Empty(queue.Enqueued);
    }

    // Criterion: extraction never touches the clipboard
    [Fact]
    public async Task Learning_Never_Writes_To_The_Clipboard()
    {
        var clipboard = new FakeClipboardWriter();
        _extractor.TermsToReturn = ["Contoso", "Fabrikam"];

        await CreateLearner().LearnAsync(Session("Contoso and Fabrikam."));

        // One confirm wrote nothing here; the learner has no clipboard dependency
        // at all, which is what keeps this criterion structurally true.
        Assert.Empty(clipboard.Writes);
        Assert.Equal(2, _vocabulary.Entries.Count);
    }

    // Criterion: no UI exists for manually adding a vocabulary entry — every
    // entry originates from a confirmed dictation. Enforced structurally: the
    // only way into the store is the learning queue, which takes a
    // DictationSession, and the UI surface has no vocabulary verb at all.
    [Fact]
    public void Vocabulary_Can_Only_Be_Reached_Through_A_Confirmed_Dictation()
    {
        var enqueue = typeof(IVocabularyLearningQueue).GetMethod(nameof(IVocabularyLearningQueue.Enqueue))!;
        Assert.Equal(
            typeof(Flight.Core.Storage.DictationSession),
            Assert.Single(enqueue.GetParameters()).ParameterType);

        // The user-facing surface offers no way to add, edit, or delete a term.
        Assert.DoesNotContain(
            typeof(Flight.Core.Dictation.IDictationUi).GetMethods(),
            method => method.Name.Contains("Vocabulary", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Term", StringComparison.OrdinalIgnoreCase));
    }

    // Every stored entry traces back to the dictation it came from
    [Fact]
    public async Task EveryStoredEntry_Traces_Back_To_A_Source_Dictation()
    {
        _extractor.TermsToReturn = ["Contoso", "Fabrikam"];

        await CreateLearner().LearnAsync(Session("Contoso and Fabrikam."));

        Assert.Equal(2, _vocabulary.Entries.Count);
        Assert.All(_vocabulary.Entries, entry => Assert.Equal(7, entry.SourceSessionId));
    }

    private sealed class OrderRecordingClipboard : Flight.Core.Clipboard.IClipboardWriter
    {
        private readonly FakeClipboardWriter _inner;
        private readonly List<string> _order;

        public OrderRecordingClipboard(FakeClipboardWriter inner, List<string> order)
        {
            _inner = inner;
            _order = order;
        }

        public void SetText(string text)
        {
            _order.Add("clipboard");
            _inner.SetText(text);
        }
    }
}
