using Flight.Core.Audio;
using Flight.Core.Clipboard;
using Flight.Core.Dictation;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Security;
using Flight.Core.Languages;
using Flight.Core.Settings;
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

    /// <summary>One second of audible audio by default, so the speech gate lets it through.</summary>
    public float[] SamplesToReturn { get; set; } = FakeAudio.Speech();

    public event Action<float>? LevelChanged;

    public void RaiseLevel(float level) => LevelChanged?.Invoke(level);

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

/// <summary>Synthetic 16 kHz captures for the speech gate.</summary>
public static class FakeAudio
{
    /// <summary>A 220 Hz tone at 0.1 amplitude — comfortably above the silence floor.</summary>
    public static float[] Speech(double seconds = 1.0) =>
        Enumerable.Range(0, (int)(seconds * 16_000))
            .Select(i => (float)(0.1 * Math.Sin(2 * Math.PI * 220 * i / 16_000)))
            .ToArray();

    public static float[] Silence(double seconds = 1.0, float noise = 0.001f) =>
        Enumerable.Range(0, (int)(seconds * 16_000))
            .Select(i => i % 2 == 0 ? noise : -noise)
            .ToArray();
}

public sealed class FakeSpeechToText : ISpeechToText
{
    public string TranscriptToReturn { get; set; } = "um so hello uh world";
    public float[]? ReceivedSamples { get; private set; }
    public string? ReceivedLanguage { get; private set; }
    public Exception? ExceptionToThrow { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public int CallCount { get; private set; }

    public async Task<string> TranscribeAsync(
        float[] samples,
        string languageCode = SupportedLanguages.DefaultCode,
        CancellationToken cancellationToken = default)
    {
        ReceivedSamples = samples;
        ReceivedLanguage = languageCode;
        CallCount++;
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
    public TranscriptCleanupRequest? ReceivedRequest { get; private set; }
    public string? ReceivedRawTranscript => ReceivedRequest?.RawTranscript;
    public Exception? ExceptionToThrow { get; set; }

    public Task<string> CleanAsync(TranscriptCleanupRequest request, CancellationToken cancellationToken = default)
    {
        ReceivedRequest = request;
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

    /// <summary>Thrown by every write, as when another app holds the clipboard open.</summary>
    public Exception? ExceptionToThrow { get; set; }

    public void SetText(string text)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        Writes.Add(text);
    }
}

public sealed class FakeDictationUi : IDictationUi
{
    public List<string> Events { get; } = [];
    public List<float> Levels { get; } = [];
    public string? LastErrorMessage { get; private set; }

    public void ShowCapturing() => Events.Add("capturing");
    public void ShowLevel(float level) => Levels.Add(level);
    public void ShowProcessing() => Events.Add("processing");
    public void ShowCopied() => Events.Add("copied");
    public void ShowNothingHeard() => Events.Add("nothing-heard");

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
    public Exception? ExceptionToThrow { get; set; }

    public Task<DictationSession> AddAsync(DictationSession session, CancellationToken cancellationToken = default)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        var stored = session with { Id = Sessions.Count + 1 };
        Sessions.Add(stored);
        return Task.FromResult(stored);
    }

    public Task<IReadOnlyList<DictationSession>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DictationSession>>(Sessions);
}

public sealed class FakeLlmCompletion : ILlmCompletion
{
    public FakeLlmCompletion(string name = "fake") => Name = name;

    public string Name { get; }
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

    public Task UpdateEmbeddingAsync(long id, float[] embedding, CancellationToken cancellationToken = default)
    {
        var index = _entries.FindIndex(e => e.Id == id);
        _entries[index] = _entries[index] with { Embedding = embedding };
        return Task.CompletedTask;
    }
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

    /// <summary>Completed by default; replace to simulate learning still in progress.</summary>
    public Task IdleTask { get; set; } = Task.CompletedTask;

    public Task WaitForIdleAsync() => IdleTask;
}

public sealed class FakeSettingsStore : ISettingsStore
{
    public FakeSettingsStore(FlightSettings? initial = null) => Current = initial ?? new FlightSettings();

    public FlightSettings Current { get; private set; }

    public event Action<FlightSettings>? Changed;

    public Task<FlightSettings> UpdateAsync(FlightSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        Current = settings;
        Changed?.Invoke(settings);
        return Task.FromResult(settings);
    }
}

public sealed class FakeAppUpdater : Flight.Core.Updates.IAppUpdater
{
    public bool IsInstalled { get; set; } = true;
    public string? AvailableVersion { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public int DownloadCount { get; private set; }
    public bool ApplyOnExitRequested { get; private set; }

    public Task<string?> DownloadLatestAsync(CancellationToken cancellationToken = default)
    {
        DownloadCount++;
        return ExceptionToThrow is not null ? throw ExceptionToThrow : Task.FromResult(AvailableVersion);
    }

    public void ApplyOnExit() => ApplyOnExitRequested = true;

    public bool RestartRequested { get; private set; }

    public void RestartToApply() => RestartRequested = true;
}

public sealed class FakeShellActions : Flight.Core.Tray.IShellActions
{
    public List<string> Calls { get; } = [];

    public void OpenSettings() => Calls.Add("settings");

    public void Quit() => Calls.Add("quit");

    public void RestartToUpdate() => Calls.Add("restart-to-update");
}

public sealed class FakeStartupRegistration : Flight.Core.Startup.IStartupRegistration
{
    public bool IsAvailable { get; set; } = true;
    public bool IsEnabled { get; set; }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;
}

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = [];

    public InMemorySecretStore(string? cloudApiKey = null)
    {
        if (cloudApiKey is not null)
        {
            _secrets[SecretNames.CloudLlmApiKey] = cloudApiKey;
        }
    }

    public string? Get(string name) => _secrets.TryGetValue(name, out var value) ? value : null;

    public void Set(string name, string value) => _secrets[name] = value;

    public void Remove(string name) => _secrets.Remove(name);
}

/// <summary>Serves a canned HTTP response, or throws, without a network.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        _handler = handler;

    public List<HttpRequestMessage> Requests { get; } = [];
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return await _handler(request, cancellationToken);
    }
}

/// <summary>Feeds synthetic key events to whatever the hook was started with.</summary>
public sealed class FakeKeyboardHook : IKeyboardHook
{
    private Func<KeyEvent, bool>? _onKey;
    private readonly HashSet<int> _down = [];

    public int MenuSuppressions { get; private set; }

    /// <summary>Keys reported down by <see cref="IsKeyDown"/> regardless of events, or not.</summary>
    public HashSet<int> ForgottenKeys { get; } = [];

    public void Start(Func<KeyEvent, bool> onKey) => _onKey = onKey;

    /// <returns>True when the hotkey logic swallowed the key.</returns>
    public bool Down(int vk, bool injected = false)
    {
        _down.Add(vk);
        return _onKey!(new KeyEvent(vk, IsDown: true, injected)); // tests Start() before typing
    }

    public bool Up(int vk, bool injected = false)
    {
        _down.Remove(vk);
        return _onKey!(new KeyEvent(vk, IsDown: false, injected)); // tests Start() before typing
    }

    public bool IsKeyDown(int virtualKey) => _down.Contains(virtualKey) && !ForgottenKeys.Contains(virtualKey);

    public void SuppressMenuActivation() => MenuSuppressions++;

    public void Dispose() { }
}

public sealed class FakeTimingsLog : Flight.Core.Dictation.IDictationTimingsLog
{
    public List<Flight.Core.Dictation.DictationTimings> Recorded { get; } = [];

    public void Record(Flight.Core.Dictation.DictationTimings timings) => Recorded.Add(timings);
}
