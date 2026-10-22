using System.Diagnostics;
using Flight.Core.Storage;
using Flight.Core.Vocabulary;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Vocabulary;

/// <summary>
/// Feature #2 criterion: extraction runs as a background job that never blocks
/// the dictation loop's UI.
/// </summary>
public class BackgroundVocabularyLearningQueueTests
{
    private readonly FakeTermExtractor _extractor = new();
    private readonly InMemoryVocabularyRepository _vocabulary = new();

    private BackgroundVocabularyLearningQueue CreateQueue() => new(
        new VocabularyLearner(_extractor, new HashingEmbeddingGenerator(), _vocabulary,
            NullLogger<VocabularyLearner>.Instance),
        NullLogger<BackgroundVocabularyLearningQueue>.Instance);

    private static DictationSession Session(string cleaned = "We met Contoso.") => new()
    {
        Id = 1,
        RawTranscript = "raw",
        CleanedOutput = cleaned,
    };

    // Feature #10 criterion: learning (an LLM call) waits while a dictation
    // is capturing or processing, so it never slows the one the user waits on
    [Fact]
    public async Task Learning_WaitsForTheDictationLoopToBeIdle()
    {
        var activity = new Flight.Core.Dictation.DictationActivity();
        activity.SetBusy(true);
        await using var queue = new BackgroundVocabularyLearningQueue(
            new VocabularyLearner(_extractor, new HashingEmbeddingGenerator(), _vocabulary,
                NullLogger<VocabularyLearner>.Instance),
            NullLogger<BackgroundVocabularyLearningQueue>.Instance,
            activity);

        queue.Enqueue(Session());
        await Task.Delay(100);
        Assert.Empty(_extractor.ReceivedTexts);

        activity.SetBusy(false);
        await queue.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(_extractor.ReceivedTexts);
    }

    // Criterion: the job runs in the background — Enqueue returns immediately
    // even when extraction itself is slow
    [Fact]
    public async Task Enqueue_Returns_Immediately_While_Extraction_Runs()
    {
        var extractionStarted = new TaskCompletionSource();
        var releaseExtraction = new TaskCompletionSource();
        var slowExtractor = new BlockingTermExtractor(extractionStarted, releaseExtraction);
        await using var queue = new BackgroundVocabularyLearningQueue(
            new VocabularyLearner(slowExtractor, new HashingEmbeddingGenerator(), _vocabulary,
                NullLogger<VocabularyLearner>.Instance),
            NullLogger<BackgroundVocabularyLearningQueue>.Instance);

        var stopwatch = Stopwatch.StartNew();
        queue.Enqueue(Session());
        stopwatch.Stop();

        await extractionStarted.Task;
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"Enqueue blocked for {stopwatch.ElapsedMilliseconds}ms while extraction was in flight");

        releaseExtraction.SetResult();
        await queue.WaitForIdleAsync();
        Assert.NotEmpty(_vocabulary.Entries);
    }

    // The work does actually happen, just off the loop
    [Fact]
    public async Task EnqueuedSession_Is_Eventually_Extracted_And_Stored()
    {
        await using var queue = CreateQueue();
        _extractor.TermsToReturn = ["Contoso"];

        queue.Enqueue(Session());
        await queue.WaitForIdleAsync();

        Assert.Equal("Contoso", Assert.Single(_vocabulary.Entries).Term);
    }

    // A failed extraction must not take down the queue: the dictation is
    // already on the clipboard, and later dictations still learn
    [Fact]
    public async Task ExtractionFailure_Does_Not_Stop_Later_Sessions()
    {
        await using var queue = CreateQueue();
        _extractor.ExceptionToThrow = new InvalidOperationException("model unavailable");

        queue.Enqueue(Session());
        await queue.WaitForIdleAsync();
        Assert.Empty(_vocabulary.Entries);

        _extractor.ExceptionToThrow = null;
        _extractor.TermsToReturn = ["Fabrikam"];
        queue.Enqueue(Session());
        await queue.WaitForIdleAsync();

        Assert.Equal("Fabrikam", Assert.Single(_vocabulary.Entries).Term);
    }

    // Repeated confirmations of the same term accumulate frequency
    [Fact]
    public async Task RepeatedSessions_Accumulate_Frequency()
    {
        await using var queue = CreateQueue();
        _extractor.TermsToReturn = ["Contoso"];

        queue.Enqueue(Session());
        await queue.WaitForIdleAsync();
        queue.Enqueue(Session());
        await queue.WaitForIdleAsync();

        Assert.Equal(2, Assert.Single(_vocabulary.Entries).Frequency);
    }

    private sealed class BlockingTermExtractor : ITermExtractor
    {
        private readonly TaskCompletionSource _started;
        private readonly TaskCompletionSource _release;

        public BlockingTermExtractor(TaskCompletionSource started, TaskCompletionSource release)
        {
            _started = started;
            _release = release;
        }

        public async Task<IReadOnlyList<string>> ExtractAsync(string cleanedOutput, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task;
            return ["Contoso"];
        }
    }
}
