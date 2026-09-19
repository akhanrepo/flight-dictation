using Flight.Core.Audio;
using Flight.Core.Clipboard;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Storage;
using Flight.Core.Stt;
using Flight.Core.Vocabulary;
using Microsoft.Extensions.Logging;

namespace Flight.Core.Dictation;

/// <summary>
/// State machine for the core dictation loop:
/// Idle → Capturing (hotkey down) → Processing (hotkey up) → Preview → Idle (confirm/reject).
/// Any failure lands in Error, surfaced through the UI — never silent.
/// The clipboard is written in exactly one code path: <see cref="ConfirmAsync"/>.
/// </summary>
public class DictationOrchestrator : IDisposable
{
    private readonly IPushToTalkHotkey _hotkey;
    private readonly IAudioCapturer _capturer;
    private readonly ISpeechToText _stt;
    private readonly ITextCleaner _cleaner;
    private readonly IClipboardWriter _clipboard;
    private readonly IDictationSessionRepository _sessions;
    private readonly IDictationUi _ui;
    private readonly IVocabularyLearningQueue _vocabularyLearning;
    private readonly FlightOptions _options;
    private readonly ILogger<DictationOrchestrator> _logger;

    private string? _pendingRawTranscript;
    private string? _pendingCleanedText;

    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>The in-flight STT/LLM pipeline task, if any. Awaitable by tests and shutdown.</summary>
    public Task? ProcessingTask { get; private set; }

    public DictationOrchestrator(
        IPushToTalkHotkey hotkey,
        IAudioCapturer capturer,
        ISpeechToText stt,
        ITextCleaner cleaner,
        IClipboardWriter clipboard,
        IDictationSessionRepository sessions,
        IDictationUi ui,
        IVocabularyLearningQueue vocabularyLearning,
        FlightOptions options,
        ILogger<DictationOrchestrator> logger)
    {
        _hotkey = hotkey;
        _capturer = capturer;
        _stt = stt;
        _cleaner = cleaner;
        _clipboard = clipboard;
        _sessions = sessions;
        _ui = ui;
        _vocabularyLearning = vocabularyLearning;
        _options = options;
        _logger = logger;
    }

    public void Start()
    {
        _hotkey.Pressed += OnHotkeyPressed;
        _hotkey.Released += OnHotkeyReleased;
        _hotkey.Start();
    }

    private void OnHotkeyPressed()
    {
        if (State != DictationState.Idle && State != DictationState.Error)
        {
            return;
        }

        State = DictationState.Capturing;
        _capturer.Start();
        _ui.ShowCapturing();
    }

    private void OnHotkeyReleased()
    {
        if (State != DictationState.Capturing)
        {
            return;
        }

        var samples = _capturer.Stop();
        State = DictationState.Processing;
        _ui.ShowProcessing();
        ProcessingTask = ProcessAsync(samples);
    }

    private async Task ProcessAsync(float[] samples)
    {
        try
        {
            using var timeout = new CancellationTokenSource(_options.ProcessingTimeout);
            var raw = await _stt.TranscribeAsync(samples, timeout.Token).WaitAsync(timeout.Token);
            var cleaned = await _cleaner.CleanAsync(raw, timeout.Token).WaitAsync(timeout.Token);

            _pendingRawTranscript = raw;
            _pendingCleanedText = cleaned;
            State = DictationState.Preview;
            _ui.ShowPreview(cleaned);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation processing failed");
            _pendingRawTranscript = null;
            _pendingCleanedText = null;
            State = DictationState.Error;
            _ui.ShowError(ex is OperationCanceledException
                ? "Processing timed out."
                : $"Processing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// User confirmed the preview: copy to clipboard, persist the session, then
    /// hand it to vocabulary learning. That order is deliberate — the clipboard
    /// write is never behind extraction work.
    /// </summary>
    public async Task ConfirmAsync()
    {
        if (State != DictationState.Preview || _pendingCleanedText is null || _pendingRawTranscript is null)
        {
            return;
        }

        _clipboard.SetText(_pendingCleanedText);
        var stored = await _sessions.AddAsync(new DictationSession
        {
            RawTranscript = _pendingRawTranscript,
            CleanedOutput = _pendingCleanedText,
            LlmMode = "local",
        });

        // Fire-and-forget by contract: Enqueue returns immediately.
        _vocabularyLearning.Enqueue(stored);

        Reset();
    }

    /// <summary>User dismissed the preview: discard everything, clipboard untouched.</summary>
    public void Reject()
    {
        if (State != DictationState.Preview)
        {
            return;
        }

        Reset();
    }

    /// <summary>Dismiss an error state back to idle.</summary>
    public void DismissError()
    {
        if (State != DictationState.Error)
        {
            return;
        }

        Reset();
    }

    private void Reset()
    {
        _pendingRawTranscript = null;
        _pendingCleanedText = null;
        State = DictationState.Idle;
        _ui.Hide();
    }

    public void Dispose()
    {
        _hotkey.Pressed -= OnHotkeyPressed;
        _hotkey.Released -= OnHotkeyReleased;
        GC.SuppressFinalize(this);
    }
}
