using System.Diagnostics;
using Flight.Core.Audio;
using Flight.Core.Clipboard;
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
/// Idle → Capturing (hotkey down) → Processing (hotkey up) → Idle, with the
/// result copied to the clipboard on the way out. Any failure lands in Error,
/// surfaced through the UI — never silent.
/// The clipboard is written in exactly one place: after a successful,
/// non-empty result in <see cref="ProcessAsync"/>.
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
    private readonly DictationActivity? _activity;
    private readonly IDictationTimingsLog? _timings;

    // Cancelled on Dispose, so quitting mid-processing never writes the clipboard.
    private readonly CancellationTokenSource _shutdown = new();

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
            _activity?.SetBusy(value is DictationState.Capturing or DictationState.Processing);
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
        ILogger<DictationOrchestrator> logger,
        DictationActivity? activity = null,
        IDictationTimingsLog? timings = null)
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
        _activity = activity;
        _timings = timings;
    }

    public void Start()
    {
        _capturer.LevelChanged += OnLevelChanged;
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

    private void OnLevelChanged(float level)
    {
        if (State == DictationState.Capturing)
        {
            _ui.ShowLevel(level);
        }
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

        ProcessingTask = Task.CompletedTask;
        switch (SpeechGate.Evaluate(samples, _options.MinimumCaptureDuration, _options.SilenceThreshold))
        {
            case CaptureVerdict.TooShort:
                // An accidental tap: no models run and nothing is said about it.
                ReturnToIdle();
                return;
            case CaptureVerdict.Silent:
                // Silence would cost a full STT pass to come back as [BLANK_AUDIO].
                NothingHeard();
                return;
        }

        State = DictationState.Processing;
        _ui.ShowProcessing();
        ProcessingTask = ProcessAsync(samples, settings);
    }

    private async Task ProcessAsync(float[] samples, FlightSettings settings)
    {
        var total = Stopwatch.StartNew();
        TimeSpan sttTime;
        string raw;
        string cleaned;
        try
        {
            using var timeout = new CancellationTokenSource(_options.ProcessingTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _shutdown.Token);
            var token = linked.Token;

            // STT runs in the user's selected source language, not an
            // auto-detected one. Whisper's non-speech markers are dropped.
            raw = TranscriptFilter.Clean(await _stt
                .TranscribeAsync(samples, settings.SourceLanguage, token)
                .WaitAsync(token));
            sttTime = total.Elapsed;

            if (raw.Length == 0)
            {
                NothingHeard();
                return;
            }

            // When source and target match this is cleanup only; when they
            // differ the same step also translates.
            cleaned = (await _cleaner
                .CleanAsync(
                    new TranscriptCleanupRequest(
                        raw,
                        settings.SourceLanguage,
                        settings.TargetLanguage,
                        settings.LlmMode,
                        settings.AiCleanup),
                    token)
                .WaitAsync(token)).Trim();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Quitting: drop the result without touching the clipboard or the UI.
            State = DictationState.Idle;
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation processing failed");
            State = DictationState.Error;
            _ui.ShowError(ex is OperationCanceledException
                ? "Processing timed out."
                : $"Processing failed: {ex.Message}");
            return;
        }

        if (cleaned.Length == 0)
        {
            NothingHeard();
            return;
        }

        // Release-to-ready time; the clipboard write and history row after it are milliseconds.
        var processed = total.Elapsed;
        await DeliverAsync(raw, cleaned, settings);

        _timings?.Record(new DictationTimings(
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(samples.Length / (double)SpeechGate.SampleRate),
            sttTime,
            processed - sttTime,
            processed,
            settings.LlmMode.ToStorageValue(),
            settings.IsTranslating));
    }

    /// <summary>
    /// Copy, then persist, then hand to vocabulary learning. That order is
    /// deliberate: the text the user is waiting to paste is never behind a
    /// database write or extraction work.
    /// </summary>
    private async Task DeliverAsync(string raw, string cleaned, FlightSettings settings)
    {
        // The cleaned, target-language text — not the raw source-language transcript.
        _clipboard.SetText(cleaned);
        State = DictationState.Idle;
        _ui.ShowCopied();

        try
        {
            var stored = await _sessions.AddAsync(new DictationSession
            {
                RawTranscript = raw,
                CleanedOutput = cleaned,
                SourceLanguage = settings.SourceLanguage,
                TargetLanguage = settings.TargetLanguage,
                // Records which engine actually produced this text.
                LlmMode = settings.LlmMode.ToStorageValue(),
            });

            // Fire-and-forget by contract: Enqueue returns immediately.
            _vocabularyLearning.Enqueue(stored);
        }
        catch (Exception ex)
        {
            // The user already has their text; losing one history row isn't
            // worth an error over it.
            _logger.LogError(ex, "Couldn't save the dictation session");
        }
    }

    /// <summary>Dismiss an error state back to idle.</summary>
    public void DismissError()
    {
        if (State != DictationState.Error)
        {
            return;
        }

        ReturnToIdle();
    }

    private void NothingHeard()
    {
        State = DictationState.Idle;
        _ui.ShowNothingHeard();
    }

    private void ReturnToIdle()
    {
        State = DictationState.Idle;
        _ui.Hide();
    }

    public void Dispose()
    {
        _hotkey.Pressed -= OnHotkeyPressed;
        _hotkey.Released -= OnHotkeyReleased;
        _capturer.LevelChanged -= OnLevelChanged;
        _shutdown.Cancel();

        // Quitting mid-capture must not leave the microphone open.
        if (State == DictationState.Capturing)
        {
            _capturer.Stop();
            State = DictationState.Idle;
        }

        GC.SuppressFinalize(this);
    }
}
