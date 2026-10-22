using Flight.Core.Stt;
using Xunit;

namespace Flight.Tests.Stt;

/// <summary>Feature #9: whisper's non-speech markers never reach the clipboard.</summary>
public class TranscriptFilterTests
{
    [Theory]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("[BLANK_AUDIO].")]
    [InlineData(" [ Silence ] ")]
    [InlineData("(music)")]
    [InlineData("(upbeat music)")]
    [InlineData("*coughs*")]
    [InlineData("[MUSIC] [APPLAUSE]")]
    [InlineData("")]
    [InlineData("   ")]
    public void Filter_MarkerOnlyTranscriptsBecomeEmpty(string transcript)
    {
        Assert.Equal(string.Empty, TranscriptFilter.Clean(transcript));
    }

    [Theory]
    [InlineData("[BLANK_AUDIO] Send the report.", "Send the report.")]
    [InlineData("Send the report. (music)", "Send the report.")]
    [InlineData("Hello  [Silence]  world", "Hello world")]
    public void Filter_RemovesMarkersAroundRealSpeech(string transcript, string expected)
    {
        Assert.Equal(expected, TranscriptFilter.Clean(transcript));
    }

    // Criterion: real text that happens to contain brackets is kept
    [Theory]
    [InlineData("Ship it (after review) on Friday.")]
    [InlineData("The ratio is 3 (three) to one.")]
    [InlineData("Call Priya.")]
    public void Filter_KeepsRealTextWithBrackets(string transcript)
    {
        Assert.Equal(transcript, TranscriptFilter.Clean(transcript));
    }
}
