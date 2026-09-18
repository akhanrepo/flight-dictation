using Flight.Core.Audio;
using Flight.Core.Clipboard;
using Flight.Core.Dictation;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Storage;
using Flight.Core.Stt;

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
