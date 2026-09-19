using Flight.Core.Audio;
using Flight.Core.Clipboard;
using Flight.Core.Dictation;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Storage;
using Flight.Core.Stt;
using Flight.Core.Vocabulary;

namespace Flight.Tests.Fakes;

public sealed class FakeHotkey : IPushToTalkHotkey
{
    public event Action? Pressed;
    public event Action? Released;
    public bool Started { get; private set; }

    public void Start() => Started = true;
    public void Press() => Pressed?.Invoke();
    public void Release() => Released?.Invoke();
    public void Dispose() { }
}

public sealed class FakeAudioCapturer : IAudioCapturer
{
    public bool IsCapturing { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public float[] SamplesToReturn { get; set; } = [0.1f, 0.2f, 0.3f];

    public void Start()
    {
        IsCapturing = true;
        StartCount++;
    }

    public float[] Stop()
    {
        IsCapturing = false;
        StopCount++;
        return SamplesToReturn;
    }
}

public sealed class FakeSpeechToText : ISpeechToText
{
    public string TranscriptToReturn { get; set; } = "um so hello uh world";
    public float[]? ReceivedSamples { get; private set; }
    public Exception? ExceptionToThrow { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken = default)
    {
        ReceivedSamples = samples;
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
        return TranscriptToReturn;
    }
}

public sealed class FakeTextCleaner : ITextCleaner
{
    public string CleanedToReturn { get; set; } = "Hello, world.";
    public string? ReceivedRawTranscript { get; private set; }
    public Exception? ExceptionToThrow { get; set; }

    public Task<string> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        ReceivedRawTranscript = rawTranscript;
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
        return Task.FromResult(CleanedToReturn);
    }
}

public sealed class FakeClipboardWriter : IClipboardWriter
{
    public List<string> Writes { get; } = [];
    public string? LastText => Writes.Count == 0 ? null : Writes[^1];

    public void SetText(string text) => Writes.Add(text);
}

public sealed class FakeDictationUi : IDictationUi
{
    public List<string> Events { get; } = [];
    public string? LastPreviewText { get; private set; }
    public string? LastErrorMessage { get; private set; }

    public void ShowCapturing() => Events.Add("capturing");
    public void ShowProcessing() => Events.Add("processing");

    public void ShowPreview(string cleanedText)
    {
        LastPreviewText = cleanedText;
        Events.Add("preview");
    }

    public void ShowError(string message)
    {
        LastErrorMessage = message;
        Events.Add("error");
    }

    public List<Flight.Core.Models.ModelDownloadProgress> DownloadProgressReports { get; } = [];

    public void ShowModelDownloadProgress(Flight.Core.Models.ModelDownloadProgress progress)
    {
        DownloadProgressReports.Add(progress);
        Events.Add("download");
    }

    public void Hide() => Events.Add("hide");
}

public sealed class InMemorySessionRepository : IDictationSessionRepository
{
    public List<DictationSession> Sessions { get; } = [];

    public Task<DictationSession> AddAsync(DictationSession session, CancellationToken cancellationToken = default)
    {
        var stored = session with { Id = Sessions.Count + 1 };
        Sessions.Add(stored);
        return Task.FromResult(stored);
    }

    public Task<IReadOnlyList<DictationSession>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DictationSession>>(Sessions);
}

public sealed class FakeLlmCompletion : ILlmCompletion
{
    public List<LlmRequest> Requests { get; } = [];
    public LlmRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public string ResponseToReturn { get; set; } = "Hello, world.";
    public Exception? ExceptionToThrow { get; set; }

    public Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
        return Task.FromResult(ResponseToReturn);
    }
}

public sealed class FakeTermExtractor : ITermExtractor
{
    public List<string> ReceivedTexts { get; } = [];
    public IReadOnlyList<string> TermsToReturn { get; set; } = ["Contoso"];
    public Exception? ExceptionToThrow { get; set; }

    public Task<IReadOnlyList<string>> ExtractAsync(string cleanedOutput, CancellationToken cancellationToken = default)
    {
        ReceivedTexts.Add(cleanedOutput);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
        return Task.FromResult(TermsToReturn);
    }
}

public sealed class InMemoryVocabularyRepository : IVocabularyRepository
{
    private readonly List<VocabularyEntry> _entries = [];

    public IReadOnlyList<VocabularyEntry> Entries => _entries;

    public Task<VocabularyEntry> UpsertAsync(VocabularyEntry entry, CancellationToken cancellationToken = default)
    {
        var index = _entries.FindIndex(e => string.Equals(e.Term, entry.Term, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            var updated = _entries[index] with
            {
                Frequency = _entries[index].Frequency + 1,
                LastUsedAt = entry.LastUsedAt,
                Embedding = entry.Embedding,
            };
            _entries[index] = updated;
            return Task.FromResult(updated);
        }

        var stored = entry with { Id = _entries.Count + 1 };
        _entries.Add(stored);
        return Task.FromResult(stored);
    }

    public Task<IReadOnlyList<VocabularyMatch>> FindRelevantAsync(
        float[] queryEmbedding, int topK, double minimumSimilarity, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VocabularyMatch>>(_entries
            .Select(e => new VocabularyMatch(e, VectorMath.CosineSimilarity(queryEmbedding, e.Embedding)))
            .Where(m => m.Similarity >= minimumSimilarity)
            .OrderByDescending(m => m.Similarity)
            .Take(topK)
            .ToList());

    public Task<IReadOnlyList<VocabularyEntry>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VocabularyEntry>>(_entries);
}

public sealed class FakeVocabularyContextProvider : IVocabularyContextProvider
{
    public List<string> ReceivedTranscripts { get; } = [];
    public IReadOnlyList<string> TermsToReturn { get; set; } = [];

    public Task<IReadOnlyList<string>> GetRelevantTermsAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        ReceivedTranscripts.Add(rawTranscript);
        return Task.FromResult(TermsToReturn);
    }
}

/// <summary>
/// Records what was handed off for vocabulary learning, and when relative to
/// the clipboard write, so ordering criteria are directly assertable.
/// </summary>
public sealed class FakeVocabularyLearningQueue : IVocabularyLearningQueue
{
    public List<DictationSession> Enqueued { get; } = [];
    public Action? OnEnqueue { get; set; }

    public void Enqueue(DictationSession session)
    {
        Enqueued.Add(session);
        OnEnqueue?.Invoke();
    }
}
