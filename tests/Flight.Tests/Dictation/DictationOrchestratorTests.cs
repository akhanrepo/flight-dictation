using Flight.Core;
using Flight.Core.Dictation;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Dictation;

public class DictationOrchestratorTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapturer _capturer = new();
    private readonly FakeSpeechToText _stt = new();
    private readonly FakeTextCleaner _cleaner = new();
    private readonly FakeClipboardWriter _clipboard = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly FakeDictationUi _ui = new();
    private readonly FakeVocabularyLearningQueue _vocabularyLearning = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly FlightOptions _options = new();

    private DictationOrchestrator CreateStartedOrchestrator()
    {
        var orchestrator = new DictationOrchestrator(
            _hotkey, _capturer, _stt, _cleaner, _clipboard, _sessions, _ui, _vocabularyLearning, _settings, _options,
            NullLogger<DictationOrchestrator>.Instance);
        orchestrator.Start();
        return orchestrator;
    }

    private async Task<DictationOrchestrator> DictateAsync(DictationOrchestrator? orchestrator = null)
    {
        orchestrator ??= CreateStartedOrchestrator();
        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;
        return orchestrator;
    }

    // Criterion: holding the hotkey starts capture with a visible cue
    [Fact]
    public void HotkeyPressed_StartsCapture_And_RaisesCapturingState()
    {
        var orchestrator = CreateStartedOrchestrator();

        _hotkey.Press();

        Assert.Equal(DictationState.Capturing, orchestrator.State);
        Assert.True(_capturer.IsCapturing);
        Assert.Contains("capturing", _ui.Events);
    }

    // Criterion: releasing the hotkey stops capture and begins processing
    [Fact]
    public async Task HotkeyReleased_StopsCapture_And_StartsProcessing()
    {
        var orchestrator = CreateStartedOrchestrator();

        _hotkey.Press();
        _hotkey.Release();

        Assert.False(_capturer.IsCapturing);
        Assert.Contains("processing", _ui.Events);
        await orchestrator.ProcessingTask!;
    }

    // Criterion: no capture occurs before hotkey-down or after hotkey-up
    [Fact]
    public async Task NoCapture_Occurs_Before_Press_Or_After_Release()
    {
        var orchestrator = CreateStartedOrchestrator();
        Assert.Equal(0, _capturer.StartCount);

        // Release without press: nothing happens
        _hotkey.Release();
        Assert.Equal(DictationState.Idle, orchestrator.State);
        Assert.Equal(0, _capturer.StartCount);

        await DictateAsync(orchestrator);

        // Exactly one start/stop pair; capture is not running after release
        Assert.Equal(1, _capturer.StartCount);
        Assert.Equal(1, _capturer.StopCount);
        Assert.False(_capturer.IsCapturing);

        // A second release does not re-enter capture or stop again
        _hotkey.Release();
        Assert.Equal(1, _capturer.StopCount);
    }

    // Criterion: STT produces a raw transcript from the captured audio
    [Fact]
    public async Task Processing_Passes_CapturedAudio_To_SpeechToText()
    {
        var audio = FakeAudio.Speech(0.5);
        _capturer.SamplesToReturn = audio;
        await DictateAsync();

        Assert.Same(audio, _stt.ReceivedSamples);
    }

    // Feature #9 criterion: the cleaned text goes to the clipboard with no
    // user action
    [Fact]
    public async Task Success_Copies_CleanedText_To_Clipboard_Automatically()
    {
        _stt.TranscriptToReturn = "um so the uh quarterly numbers look good";
        _cleaner.CleanedToReturn = "The quarterly numbers look good.";

        var orchestrator = await DictateAsync();

        Assert.Equal("um so the uh quarterly numbers look good", _cleaner.ReceivedRawTranscript);
        Assert.Equal(["The quarterly numbers look good."], _clipboard.Writes);
        Assert.Equal(DictationState.Idle, orchestrator.State);
        Assert.Equal(["capturing", "processing", "copied"], _ui.Events);
    }

    // Regression: a clipboard write that throws (another app holding the
    // clipboard) escaped the pipeline and left the loop in Processing, so
    // every later press was ignored and nothing new was ever copied.
    [Fact]
    public async Task ClipboardFailure_ShowsAnError_And_TheNextDictationStillWorks()
    {
        _clipboard.ExceptionToThrow = new System.Runtime.InteropServices.COMException("OpenClipboard failed");
        var orchestrator = await DictateAsync();

        Assert.Equal(DictationState.Error, orchestrator.State);
        Assert.Contains(_ui.Events, e => e.StartsWith("error", StringComparison.Ordinal));
        Assert.Empty(_sessions.Sessions);

        _clipboard.ExceptionToThrow = null;
        _cleaner.CleanedToReturn = "Second try.";
        await DictateAsync(orchestrator);

        Assert.Equal(["Second try."], _clipboard.Writes);
        Assert.Equal(DictationState.Idle, orchestrator.State);
    }

    // Regression for the same report: consecutive dictations each reach the clipboard.
    [Fact]
    public async Task ConsecutiveDictations_EachUpdateTheClipboard()
    {
        var orchestrator = CreateStartedOrchestrator();

        foreach (var text in new[] { "First.", "Second.", "Third." })
        {
            _cleaner.CleanedToReturn = text;
            await DictateAsync(orchestrator);
        }

        Assert.Equal(["First.", "Second.", "Third."], _clipboard.Writes);
    }

    // Feature #9 criterion: clipboard, then persist, then vocabulary learning
    [Fact]
    public async Task Success_Copies_Then_Persists_Then_Enqueues()
    {
        var order = new List<string>();
        var clipboard = new RecordingClipboard(order);
        var sessions = new RecordingSessions(order);
        var queue = new RecordingQueue(order);
        var orchestrator = new DictationOrchestrator(
            _hotkey, _capturer, _stt, _cleaner, clipboard, sessions, _ui, queue, _settings, _options,
            NullLogger<DictationOrchestrator>.Instance);
        orchestrator.Start();

        await DictateAsync(orchestrator);

        Assert.Equal(["clipboard", "persist", "enqueue"], order);
    }

    // Each dictation persists a DictationSession record
    [Fact]
    public async Task Success_Persists_Session_With_RawTranscript_CleanedOutput_Timestamp_LocalMode()
    {
        _stt.TranscriptToReturn = "raw words here";
        _cleaner.CleanedToReturn = "Raw words here.";
        var before = DateTimeOffset.UtcNow;

        await DictateAsync();

        var session = Assert.Single(_sessions.Sessions);
        Assert.Equal("raw words here", session.RawTranscript);
        Assert.Equal("Raw words here.", session.CleanedOutput);
        Assert.Equal("local", session.LlmMode);
        Assert.InRange(session.CreatedAt, before, DateTimeOffset.UtcNow);
    }

    // A failed history write must not cost the user the text they dictated
    [Fact]
    public async Task PersistFailure_StillCopies_And_Shows_Copied()
    {
        _sessions.ExceptionToThrow = new InvalidOperationException("disk full");

        var orchestrator = await DictateAsync();

        Assert.Single(_clipboard.Writes);
        Assert.Contains("copied", _ui.Events);
        Assert.Empty(_vocabularyLearning.Enqueued);
        Assert.Equal(DictationState.Idle, orchestrator.State);
    }

    // Feature #9 criterion: a short tap runs nothing and hides quietly
    [Fact]
    public async Task ShortTap_RunsNothing_And_Hides_Quietly()
    {
        _capturer.SamplesToReturn = FakeAudio.Speech(0.1);

        var orchestrator = await DictateAsync();

        Assert.Null(_stt.ReceivedSamples);
        Assert.Empty(_clipboard.Writes);
        Assert.Empty(_sessions.Sessions);
        Assert.Equal(["capturing", "hide"], _ui.Events);
        Assert.Equal(DictationState.Idle, orchestrator.State);
    }

    // Feature #9 criteria: silence skips STT and cleanup entirely, and never
    // touches the clipboard
    [Fact]
    public async Task Silence_SkipsTheModels_And_Shows_NothingHeard()
    {
        _capturer.SamplesToReturn = FakeAudio.Silence();

        var orchestrator = await DictateAsync();

        Assert.Null(_stt.ReceivedSamples);
        Assert.Null(_cleaner.ReceivedRequest);
        Assert.Empty(_clipboard.Writes);
        Assert.Empty(_sessions.Sessions);
        Assert.Equal(["capturing", "nothing-heard"], _ui.Events);
        Assert.Equal(DictationState.Idle, orchestrator.State);
    }

    // Feature #9 criterion: whisper's non-speech markers never reach the
    // clipboard (the "[BLANK_AUDIO]." bug)
    [Theory]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("[BLANK_AUDIO].")]
    [InlineData(" (music) ")]
    [InlineData("")]
    public async Task MarkerOnlyTranscript_LeavesClipboardAndHistoryUntouched(string transcript)
    {
        _stt.TranscriptToReturn = transcript;

        await DictateAsync();

        Assert.Null(_cleaner.ReceivedRequest);
        Assert.Empty(_clipboard.Writes);
        Assert.Empty(_sessions.Sessions);
        Assert.Contains("nothing-heard", _ui.Events);
    }

    [Fact]
    public async Task EmptyCleanupResult_LeavesClipboardUntouched()
    {
        _cleaner.CleanedToReturn = "   ";

        await DictateAsync();

        Assert.Empty(_clipboard.Writes);
        Assert.Contains("nothing-heard", _ui.Events);
    }

    // Feature #9 criterion: the indicator gets the mic level while listening only
    [Fact]
    public async Task Level_IsForwarded_WhileCapturing_Only()
    {
        var orchestrator = CreateStartedOrchestrator();
        _capturer.RaiseLevel(0.9f); // idle: ignored

        _hotkey.Press();
        _capturer.RaiseLevel(0.2f);
        _capturer.RaiseLevel(0.4f);
        _hotkey.Release();
        _capturer.RaiseLevel(0.7f); // processing: ignored
        await orchestrator.ProcessingTask!;

        Assert.Equal([0.2f, 0.4f], _ui.Levels);
    }

    // STT failure produces an explicit error state, clipboard untouched
    [Fact]
    public async Task SttFailure_Raises_ErrorState()
    {
        _stt.ExceptionToThrow = new InvalidOperationException("whisper failed");

        var orchestrator = await DictateAsync();

        Assert.Equal(DictationState.Error, orchestrator.State);
        Assert.Contains("whisper failed", _ui.LastErrorMessage);
        Assert.Empty(_clipboard.Writes);
    }

    // LLM failure produces an explicit error state, clipboard untouched
    [Fact]
    public async Task CleanerFailure_Raises_ErrorState()
    {
        _cleaner.ExceptionToThrow = new InvalidOperationException("llm failed");

        var orchestrator = await DictateAsync();

        Assert.Equal(DictationState.Error, orchestrator.State);
        Assert.Contains("llm failed", _ui.LastErrorMessage);
        Assert.Empty(_clipboard.Writes);
        Assert.Empty(_sessions.Sessions);
    }

    // Processing cannot hang indefinitely — timeout is an explicit error
    [Fact]
    public async Task Processing_Timeout_Raises_ErrorState_Not_Hang()
    {
        _options.ProcessingTimeout = TimeSpan.FromMilliseconds(50);
        _stt.Delay = TimeSpan.FromSeconds(30);
        var orchestrator = CreateStartedOrchestrator();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DictationState.Error, orchestrator.State);
        Assert.Equal("Processing timed out.", _ui.LastErrorMessage);
        Assert.Empty(_clipboard.Writes);
    }

    // Quitting mid-processing never copies the late result
    [Fact]
    public async Task Dispose_DuringProcessing_NeverWritesTheClipboard()
    {
        _stt.Delay = TimeSpan.FromSeconds(30);
        var orchestrator = CreateStartedOrchestrator();
        _hotkey.Press();
        _hotkey.Release();

        orchestrator.Dispose();
        await orchestrator.ProcessingTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(_clipboard.Writes);
        Assert.DoesNotContain("error", _ui.Events);
    }

    // The full loop works offline with no account/auth/network involved.
    [Fact]
    public async Task FullLoop_Completes_With_No_Network_Dependencies()
    {
        _stt.TranscriptToReturn = "offline uh test";
        _cleaner.CleanedToReturn = "Offline test.";

        var orchestrator = await DictateAsync();

        Assert.Equal("Offline test.", _clipboard.LastText);
        Assert.Single(_sessions.Sessions);
        Assert.Equal(DictationState.Idle, orchestrator.State);
    }

    // Error state can be dismissed and the loop reused
    [Fact]
    public async Task After_Error_Dismiss_Returns_To_Idle_And_Loop_Works_Again()
    {
        _stt.ExceptionToThrow = new InvalidOperationException("boom");
        var orchestrator = await DictateAsync();
        Assert.Equal(DictationState.Error, orchestrator.State);

        orchestrator.DismissError();
        Assert.Equal(DictationState.Idle, orchestrator.State);

        _stt.ExceptionToThrow = null;
        await DictateAsync(orchestrator);
        Assert.Single(_clipboard.Writes);
    }

    // A new dictation can start straight from an error, without dismissing it
    [Fact]
    public async Task HotkeyPress_FromError_StartsANewDictation()
    {
        _stt.ExceptionToThrow = new InvalidOperationException("boom");
        var orchestrator = await DictateAsync();

        _hotkey.Press();

        Assert.Equal(DictationState.Capturing, orchestrator.State);
    }

    // Feature #6 criterion: while paused the hotkey does nothing — no capture, no toast
    [Fact]
    public void Paused_HotkeyDoesNotCaptureOrShowTheToast()
    {
        var orchestrator = CreateStartedOrchestrator();
        orchestrator.Pause();

        _hotkey.Press();
        _hotkey.Release();

        Assert.Equal(DictationState.Idle, orchestrator.State);
        Assert.Equal(0, _capturer.StartCount);
        Assert.Empty(_ui.Events);
    }

    // Feature #6 criterion: pausing mid-dictation lets it finish normally
    [Fact]
    public async Task Pause_DuringCapture_LetsTheDictationFinish()
    {
        var orchestrator = CreateStartedOrchestrator();
        _hotkey.Press();

        orchestrator.Pause();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        Assert.Single(_clipboard.Writes);
    }

    // Feature #6 criterion: pause isn't remembered — a new orchestrator (a
    // new launch) starts active
    [Fact]
    public void Pause_IsNotPersistedAcrossLaunches()
    {
        CreateStartedOrchestrator().Pause();

        var nextLaunch = CreateStartedOrchestrator();

        Assert.False(nextLaunch.IsPaused);
    }

    [Fact]
    public void Resume_RestoresTheHotkey()
    {
        var orchestrator = CreateStartedOrchestrator();
        orchestrator.Pause();
        orchestrator.Resume();

        _hotkey.Press();

        Assert.Equal(DictationState.Capturing, orchestrator.State);
    }

    // The tray follows the dictation loop through this event
    [Fact]
    public async Task StateChanged_ReportsEachTransition()
    {
        var orchestrator = CreateStartedOrchestrator();
        var seen = new List<DictationState>();
        orchestrator.StateChanged += seen.Add;

        await DictateAsync(orchestrator);

        Assert.Equal([DictationState.Capturing, DictationState.Processing, DictationState.Idle], seen);
    }

    private sealed class RecordingClipboard(List<string> order) : Flight.Core.Clipboard.IClipboardWriter
    {
        public void SetText(string text) => order.Add("clipboard");
    }

    private sealed class RecordingSessions(List<string> order) : Flight.Core.Storage.IDictationSessionRepository
    {
        public Task<Flight.Core.Storage.DictationSession> AddAsync(
            Flight.Core.Storage.DictationSession session, CancellationToken cancellationToken = default)
        {
            order.Add("persist");
            return Task.FromResult(session with { Id = 1 });
        }

        public Task<IReadOnlyList<Flight.Core.Storage.DictationSession>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Flight.Core.Storage.DictationSession>>([]);
    }

    private sealed class RecordingQueue(List<string> order) : Flight.Core.Vocabulary.IVocabularyLearningQueue
    {
        public void Enqueue(Flight.Core.Storage.DictationSession session) => order.Add("enqueue");

        public Task WaitForIdleAsync() => Task.CompletedTask;
    }
}
