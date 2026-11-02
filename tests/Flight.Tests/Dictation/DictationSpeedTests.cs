using Flight.Core;
using Flight.Core.Dictation;
using Flight.Core.Settings;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Dictation;

/// <summary>Feature #10: what the dictation loop does for speed and measurement.</summary>
public class DictationSpeedTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeSpeechToText _stt = new();
    private readonly FakeTextCleaner _cleaner = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly DictationActivity _activity = new();
    private readonly FakeTimingsLog _timings = new();

    private DictationOrchestrator Create()
    {
        var orchestrator = new DictationOrchestrator(
            _hotkey, new FakeAudioCapturer(), _stt, _cleaner, new FakeClipboardWriter(),
            new InMemorySessionRepository(), new FakeDictationUi(), new FakeVocabularyLearningQueue(),
            _settings, new FlightOptions(), NullLogger<DictationOrchestrator>.Instance, _activity, _timings);
        orchestrator.Start();
        return orchestrator;
    }

    // Background work can tell when a dictation is under way
    [Fact]
    public async Task Activity_IsBusyWhileCapturingAndProcessing()
    {
        var orchestrator = Create();
        _stt.Delay = TimeSpan.FromMilliseconds(100);
        Assert.False(_activity.IsBusy);

        _hotkey.Press();
        Assert.True(_activity.IsBusy);
        _hotkey.Release();
        Assert.True(_activity.IsBusy);

        await orchestrator.ProcessingTask!;
        Assert.False(_activity.IsBusy);
        await _activity.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Activity_IsIdleAfterAnError()
    {
        _stt.ExceptionToThrow = new InvalidOperationException("boom");
        var orchestrator = Create();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        Assert.False(_activity.IsBusy);
    }

    // Criterion: the AI cleanup setting reaches the cleaner
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AiCleanupSetting_IsPassedToTheCleaner(bool enabled)
    {
        await _settings.UpdateAsync(new FlightSettings { AiCleanup = enabled });
        var orchestrator = Create();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        Assert.Equal(enabled, _cleaner.ReceivedRequest!.AiCleanup);
    }

    // Criterion: every dictation records its stage timings
    [Fact]
    public async Task Dictation_RecordsItsTimings()
    {
        _stt.Delay = TimeSpan.FromMilliseconds(30);
        var orchestrator = Create();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        var timings = Assert.Single(_timings.Recorded);
        Assert.Equal(1.0, timings.Audio.TotalSeconds, 2); // FakeAudio.Speech() is one second
        Assert.True(timings.SpeechToText >= TimeSpan.FromMilliseconds(25));
        Assert.True(timings.Total >= timings.SpeechToText + timings.Cleanup - TimeSpan.FromMilliseconds(1));
        Assert.Equal("local", timings.LlmMode);
        Assert.Contains("stt=", timings.ToLogLine());
    }

    // Every dictation gets a line, including the ones that copy nothing, so
    // the log can say why a dictation produced "Didn't catch that".
    [Fact]
    public async Task FailedDictation_RecordsItsOutcomeAndError()
    {
        _cleaner.ExceptionToThrow = new InvalidOperationException("boom");
        var orchestrator = Create();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        var timings = Assert.Single(_timings.Recorded);
        Assert.Equal(DictationOutcome.Failed, timings.Outcome);
        Assert.Equal("boom", timings.Error);
        Assert.Contains("outcome=failed", timings.ToLogLine());
        Assert.Contains("error=\"boom\"", timings.ToLogLine());
    }

    [Fact]
    public async Task SuccessfulDictation_RecordsCopiedWithItsPeakLevel()
    {
        var orchestrator = Create();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        var timings = Assert.Single(_timings.Recorded);
        Assert.Equal(DictationOutcome.Copied, timings.Outcome);
        // FakeAudio.Speech() is a 0.1-amplitude sine: RMS ≈ 0.0707.
        Assert.Equal(0.0707f, timings.PeakLevel!.Value, 3);
        Assert.Contains("outcome=copied", timings.ToLogLine());
    }

    [Theory]
    [InlineData("silent")]
    [InlineData("no-transcript")]
    [InlineData("empty-cleanup")]
    [InlineData("too-short")]
    public async Task NothingCopied_RecordsWhichStageCameBackEmpty(string stage)
    {
        var capturer = new FakeAudioCapturer();
        var expected = stage switch
        {
            "silent" => DictationOutcome.Silent,
            "no-transcript" => DictationOutcome.NoTranscript,
            "empty-cleanup" => DictationOutcome.EmptyCleanup,
            _ => DictationOutcome.TooShort,
        };
        if (stage == "silent")
        {
            capturer.SamplesToReturn = FakeAudio.Silence();
        }
        else if (stage == "too-short")
        {
            capturer.SamplesToReturn = FakeAudio.Speech(0.1);
        }
        else if (stage == "no-transcript")
        {
            _stt.TranscriptToReturn = "[BLANK_AUDIO]";
        }
        else
        {
            _cleaner.CleanedToReturn = "   ";
        }

        var orchestrator = new DictationOrchestrator(
            _hotkey, capturer, _stt, _cleaner, new FakeClipboardWriter(),
            new InMemorySessionRepository(), new FakeDictationUi(), new FakeVocabularyLearningQueue(),
            _settings, new FlightOptions(), NullLogger<DictationOrchestrator>.Instance, _activity, _timings);
        orchestrator.Start();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        Assert.Equal(expected, Assert.Single(_timings.Recorded).Outcome);
    }

    // Criterion: the log keeps the last 200 lines
    [Fact]
    public void TimingsLog_KeepsTheMostRecentLines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flight-timings-{Guid.NewGuid():N}", "timings.log");
        var log = new FileDictationTimingsLog(path);

        for (var i = 0; i < FileDictationTimingsLog.MaxLines + 25; i++)
        {
            log.Append($"line {i}");
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(FileDictationTimingsLog.MaxLines, lines.Length);
        Assert.Equal("line 25", lines[0]);
        Assert.Equal($"line {FileDictationTimingsLog.MaxLines + 24}", lines[^1]);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); // path was built with a directory
    }
}
