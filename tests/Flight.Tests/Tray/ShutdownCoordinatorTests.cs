using System.Diagnostics;
using Flight.Core;
using Flight.Core.Dictation;
using Flight.Core.Tray;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Tray;

/// <summary>Feature #6: what Quit does on the way out.</summary>
public class ShutdownCoordinatorTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapturer _capturer = new();
    private readonly FakeClipboardWriter _clipboard = new();
    private readonly FakeVocabularyLearningQueue _vocabulary = new();
    private readonly FlightOptions _options = new() { ShutdownVocabularyGrace = TimeSpan.FromMilliseconds(200) };
    private readonly DictationOrchestrator _orchestrator;

    public ShutdownCoordinatorTests()
    {
        _orchestrator = new DictationOrchestrator(
            _hotkey, _capturer, new FakeSpeechToText(), new FakeTextCleaner(), _clipboard,
            new InMemorySessionRepository(), new FakeDictationUi(), _vocabulary,
            new FakeSettingsStore(), _options, NullLogger<DictationOrchestrator>.Instance);
        _orchestrator.Start();
    }

    private ShutdownCoordinator Create() =>
        new(_orchestrator, _vocabulary, _options, NullLogger<ShutdownCoordinator>.Instance);

    [Fact]
    public async Task Quit_StopsListeningForTheHotkey()
    {
        await Create().ShutdownAsync();

        _hotkey.Press();

        Assert.Equal(0, _capturer.StartCount);
    }

    [Fact]
    public async Task Quit_MidCapture_ClosesTheMicrophone()
    {
        _hotkey.Press();

        await Create().ShutdownAsync();

        Assert.False(_capturer.IsCapturing);
    }

    // The clipboard is written only on confirm — quitting with a preview up
    // discards it rather than copying it
    [Fact]
    public async Task Quit_DiscardsAPendingPreviewWithoutTouchingTheClipboard()
    {
        _hotkey.Press();
        _hotkey.Release();
        await _orchestrator.ProcessingTask!;
        Assert.Equal(DictationState.Preview, _orchestrator.State);

        await Create().ShutdownAsync();

        Assert.Equal(DictationState.Idle, _orchestrator.State);
        Assert.Empty(_clipboard.Writes);
    }

    [Fact]
    public async Task Quit_WaitsForVocabularyLearningToFinish()
    {
        var learning = new TaskCompletionSource();
        _vocabulary.IdleTask = learning.Task;

        var shutdown = Create().ShutdownAsync();
        Assert.False(shutdown.IsCompleted);

        learning.SetResult();
        await shutdown;
    }

    // Bounded: a stuck learner never stops Flight from quitting
    [Fact]
    public async Task Quit_GivesUpOnVocabularyLearningAfterTheGrace()
    {
        _vocabulary.IdleTask = new TaskCompletionSource().Task; // never finishes
        var clock = Stopwatch.StartNew();

        await Create().ShutdownAsync();

        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5));
    }
}
