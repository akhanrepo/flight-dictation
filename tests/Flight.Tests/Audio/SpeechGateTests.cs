using Flight.Core.Audio;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Audio;

/// <summary>Feature #9: which captures are worth transcribing.</summary>
public class SpeechGateTests
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(300);
    private const float Threshold = 0.005f;

    [Fact]
    public void Gate_AQuickTapIsTooShort()
    {
        Assert.Equal(CaptureVerdict.TooShort, SpeechGate.Evaluate(FakeAudio.Speech(0.2), MinimumDuration, Threshold));
        Assert.Equal(CaptureVerdict.TooShort, SpeechGate.Evaluate([], MinimumDuration, Threshold));
    }

    [Fact]
    public void Gate_RoomNoiseIsSilent()
    {
        Assert.Equal(CaptureVerdict.Silent, SpeechGate.Evaluate(FakeAudio.Silence(2), MinimumDuration, Threshold));
    }

    [Fact]
    public void Gate_AudibleAudioIsSpeech()
    {
        Assert.Equal(CaptureVerdict.Speech, SpeechGate.Evaluate(FakeAudio.Speech(0.5), MinimumDuration, Threshold));
    }

    // A single click or bump in otherwise silent audio doesn't count
    [Fact]
    public void Gate_ASingleSpikeIsStillSilent()
    {
        var samples = FakeAudio.Silence(1);
        for (var i = 1000; i < 1000 + SpeechGate.WindowSamples; i++)
        {
            samples[i] = 0.5f;
        }

        Assert.Equal(CaptureVerdict.Silent, SpeechGate.Evaluate(samples, MinimumDuration, Threshold));
    }

    // Quiet speech after a long pause still gets through
    [Fact]
    public void Gate_QuietSpeechAfterSilenceIsSpeech()
    {
        var samples = FakeAudio.Silence(2).Concat(FakeAudio.Speech(0.2).Select(s => s * 0.1f)).ToArray();

        Assert.Equal(CaptureVerdict.Speech, SpeechGate.Evaluate(samples, MinimumDuration, Threshold));
    }

    [Fact]
    public void Gate_RmsOfASteadySignal()
    {
        Assert.Equal(0.5f, SpeechGate.Rms([0.5f, -0.5f, 0.5f, -0.5f]), 3);
        Assert.Equal(0f, SpeechGate.Rms([]));
    }
}
