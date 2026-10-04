using Flight.Core.Audio;
using Flight.Core.Clipboard;
using Flight.Core.Entitlement;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Settings;
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
    private readonly ISettingsStore _settings;
    private readonly FlightOptions _options;
    private readonly ILogger<DictationOrchestrator> _logger;

    private string? _pendingRawTranscript;
    private string? _pendingCleanedText;
    private FlightSettings? _pendingSettings;

    private DictationState _state = DictationState.Idle;

    public DictationState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            StateChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Raised on every state transition, on whichever thread caused it (the
    /// hotkey hook or the processing task). Subscribers marshal as needed.
    /// </summary>
    public event Action<DictationState>? StateChanged;

    /// <summary>
    /// While paused the hotkey is ignored: nothing is captured and no toast
    /// appears. In memory only, so every launch starts active.
    /// </summary>
    public bool IsPaused { get; private set; }

    /// <summary>Raised when <see cref="IsPaused"/> changes.</summary>
    public event Action<bool>? PausedChanged;

    /// <summary>Ignore the hotkey from now on. A dictation already under way finishes normally.</summary>
    public void Pause() => SetPaused(true);

    public void Resume() => SetPaused(false);

    private void SetPaused(bool paused)
    {
        if (IsPaused == paused)
        {
            return;
        }

        IsPaused = paused;
        PausedChanged?.Invoke(paused);
    }

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
        ISettingsStore settings,
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
        _settings = settings;
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
        if (IsPaused || (State != DictationState.Idle && State != DictationState.Error))
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

        // Settings are snapshotted per dictation, so a language or mode change
        // mid-session takes effect on the next one without a restart, and an
        // in-flight dictation can never see a half-applied change.
        var settings = _settings.Current;

        State = DictationState.Processing;
        _ui.ShowProcessing();
        ProcessingTask = ProcessAsync(samples, settings);
    }

    private async Task ProcessAsync(float[] samples, FlightSettings settings)
    {
        try
        {
            using var timeout = new CancellationTokenSource(_options.ProcessingTimeout);

            // STT runs in the user's selected source language, not an
            // auto-detected one.
            var raw = await _stt
                .TranscribeAsync(samples, settings.SourceLanguage, timeout.Token)
                .WaitAsync(timeout.Token);

            // When source and target match this is cleanup only; when they
            // differ the same step also translates.
            var cleaned = await _cleaner
                .CleanAsync(
                    new TranscriptCleanupRequest(
                        raw,
                        settings.SourceLanguage,
                        settings.TargetLanguage,
                        settings.LlmMode),
                    timeout.Token)
                .WaitAsync(timeout.Token);

            _pendingRawTranscript = raw;
            _pendingCleanedText = cleaned;
            _pendingSettings = settings;
            State = DictationState.Preview;
            _ui.ShowPreview(cleaned);
        }
        catch (EntitlementRequiredException ex)
        {
            // Cloud was blocked before any API call was made. Local mode is
            // untouched and still available, which the prompt says.
            _logger.LogInformation("Cloud dictation blocked: {Status}", ex.Decision.Status);
            _pendingRawTranscript = null;
            _pendingCleanedText = null;
            _pendingSettings = null;
            State = DictationState.Error;
            _ui.ShowUpgradeRequired(ex.Decision);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation processing failed");
            _pendingRawTranscript = null;
            _pendingCleanedText = null;
            _pendingSettings = null;
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
        if (State != DictationState.Preview
            || _pendingCleanedText is null
            || _pendingRawTranscript is null
            || _pendingSettings is null)
        {
            return;
        }

        // The preview showed — and this copies — the cleaned, target-language
        // text, not the raw source-language transcript.
        _clipboard.SetText(_pendingCleanedText);
        var stored = await _sessions.AddAsync(new DictationSession
        {
            RawTranscript = _pendingRawTranscript,
            CleanedOutput = _pendingCleanedText,
            SourceLanguage = _pendingSettings.SourceLanguage,
            TargetLanguage = _pendingSettings.TargetLanguage,
            // Records which engine actually produced this text. Feature #5
            // reads nothing but these rows to compute the cloud trial window.
            LlmMode = _pendingSettings.LlmMode.ToStorageValue(),
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
        _pendingSettings = null;
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
