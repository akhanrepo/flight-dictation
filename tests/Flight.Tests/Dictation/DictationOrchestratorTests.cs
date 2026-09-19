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
    private readonly FlightOptions _options = new();

    private DictationOrchestrator CreateStartedOrchestrator()
    {
        var orchestrator = new DictationOrchestrator(
            _hotkey, _capturer, _stt, _cleaner, _clipboard, _sessions, _ui, _vocabularyLearning, _options,
            NullLogger<DictationOrchestrator>.Instance);
        orchestrator.Start();
        return orchestrator;
    }

    private async Task<DictationOrchestrator> RunToPreviewAsync()
    {
        var orchestrator = CreateStartedOrchestrator();
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

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

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
        _capturer.SamplesToReturn = [0.5f, -0.5f];
        await RunToPreviewAsync();

        Assert.NotNull(_stt.ReceivedSamples);
        Assert.Equal([0.5f, -0.5f], _stt.ReceivedSamples);
    }

    // Criterion: the LLM cleans the raw transcript; preview shows cleaner output verbatim
    [Fact]
    public async Task Processing_Passes_RawTranscript_To_TextCleaner_And_Previews_CleanedText()
    {
        _stt.TranscriptToReturn = "um so the uh quarterly numbers look good";
        _cleaner.CleanedToReturn = "The quarterly numbers look good.";

        var orchestrator = await RunToPreviewAsync();

        Assert.Equal("um so the uh quarterly numbers look good", _cleaner.ReceivedRawTranscript);
        Assert.Equal(DictationState.Preview, orchestrator.State);
        Assert.Equal("The quarterly numbers look good.", _ui.LastPreviewText);
    }

    // Criterion: preview is displayed before the clipboard is touched
    [Fact]
    public async Task Preview_IsShown_Before_Any_Clipboard_Write()
    {
        var orchestrator = await RunToPreviewAsync();

        Assert.Contains("preview", _ui.Events);
        Assert.Empty(_clipboard.Writes);

        await orchestrator.ConfirmAsync();
        Assert.Single(_clipboard.Writes);
    }

    // Criterion: confirming copies the cleaned text to the clipboard
    [Fact]
    public async Task Confirm_Writes_CleanedText_To_Clipboard()
    {
        _cleaner.CleanedToReturn = "Send the audit report by Friday.";
        var orchestrator = await RunToPreviewAsync();

        await orchestrator.ConfirmAsync();

        Assert.Equal("Send the audit report by Friday.", _clipboard.LastText);
        Assert.Equal(DictationState.Idle, orchestrator.State);
    }

    // Criterion: dismissing discards the result; clipboard unmodified
    [Fact]
    public async Task Reject_Discards_Result_And_Leaves_Clipboard_Untouched()
    {
        var orchestrator = await RunToPreviewAsync();

        orchestrator.Reject();

        Assert.Empty(_clipboard.Writes);
        Assert.Empty(_sessions.Sessions);
        Assert.Equal(DictationState.Idle, orchestrator.State);
        Assert.Contains("hide", _ui.Events);
    }

    // Criterion: clipboard is never written unless the user explicitly confirms
    [Fact]
    public async Task Clipboard_IsNeverWritten_On_Capture_Processing_Preview_Error_Or_Reject()
    {
        var orchestrator = CreateStartedOrchestrator();

        _hotkey.Press();
        Assert.Empty(_clipboard.Writes); // capturing

        _hotkey.Release();
        await orchestrator.ProcessingTask!;
        Assert.Empty(_clipboard.Writes); // preview shown

        orchestrator.Reject();
        Assert.Empty(_clipboard.Writes); // rejected

        // Error path
        _stt.ExceptionToThrow = new InvalidOperationException("model exploded");
        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;
        Assert.Equal(DictationState.Error, orchestrator.State);
        Assert.Empty(_clipboard.Writes); // errored

        // Confirm in non-preview states is a no-op
        await orchestrator.ConfirmAsync();
        Assert.Empty(_clipboard.Writes);
    }

    // Criterion: each confirmed dictation persists a DictationSession record
    [Fact]
    public async Task Confirm_Persists_Session_With_RawTranscript_CleanedOutput_Timestamp_LocalMode()
    {
        _stt.TranscriptToReturn = "raw words here";
        _cleaner.CleanedToReturn = "Raw words here.";
        var before = DateTimeOffset.UtcNow;

        var orchestrator = await RunToPreviewAsync();
        await orchestrator.ConfirmAsync();

        var session = Assert.Single(_sessions.Sessions);
        Assert.Equal("raw words here", session.RawTranscript);
        Assert.Equal("Raw words here.", session.CleanedOutput);
        Assert.Equal("local", session.LlmMode);
        Assert.InRange(session.CreatedAt, before, DateTimeOffset.UtcNow);
    }

    // Criterion: STT failure produces an explicit error state
    [Fact]
    public async Task SttFailure_Raises_ErrorState()
    {
        _stt.ExceptionToThrow = new InvalidOperationException("whisper failed");
        var orchestrator = CreateStartedOrchestrator();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        Assert.Equal(DictationState.Error, orchestrator.State);
        Assert.Contains("whisper failed", _ui.LastErrorMessage);
    }

    // Criterion: LLM failure produces an explicit error state
    [Fact]
    public async Task CleanerFailure_Raises_ErrorState()
    {
        _cleaner.ExceptionToThrow = new InvalidOperationException("llm failed");
        var orchestrator = CreateStartedOrchestrator();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        Assert.Equal(DictationState.Error, orchestrator.State);
        Assert.Contains("llm failed", _ui.LastErrorMessage);
    }

    // Criterion: processing cannot hang indefinitely — timeout is an explicit error
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
    }

    // Criterion: the full loop works offline with no account/auth/network involved.
    // The pipeline runs end-to-end against purely local dependencies; nothing in
    // Flight.Core's dictation path takes a network service.
    [Fact]
    public async Task FullLoop_Completes_With_No_Network_Dependencies()
    {
        _stt.TranscriptToReturn = "offline uh test";
        _cleaner.CleanedToReturn = "Offline test.";

        var orchestrator = await RunToPreviewAsync();
        await orchestrator.ConfirmAsync();

        Assert.Equal("Offline test.", _clipboard.LastText);
        Assert.Single(_sessions.Sessions);
        Assert.Equal(DictationState.Idle, orchestrator.State);
    }

    // Error state can be dismissed and the loop reused
    [Fact]
    public async Task After_Error_Dismiss_Returns_To_Idle_And_Loop_Works_Again()
    {
        _stt.ExceptionToThrow = new InvalidOperationException("boom");
        var orchestrator = CreateStartedOrchestrator();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;
        Assert.Equal(DictationState.Error, orchestrator.State);

        orchestrator.DismissError();
        Assert.Equal(DictationState.Idle, orchestrator.State);

        _stt.ExceptionToThrow = null;
        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;
        Assert.Equal(DictationState.Preview, orchestrator.State);
    }
}
