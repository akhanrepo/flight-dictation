namespace Flight.Core.Audio;

public enum CaptureVerdict
{
    /// <summary>Released almost immediately: an accidental tap, not a dictation.</summary>
    TooShort,

    /// <summary>Long enough, but nothing louder than room noise.</summary>
    Silent,

    Speech,
}

/// <summary>
/// Decides, before any model runs, whether a capture is worth transcribing.
/// Whisper fed silence returns markers like <c>[BLANK_AUDIO]</c> or invents a
/// "Thank you." — and costs a full STT pass to do it.
/// </summary>
public static class SpeechGate
{
    public const int SampleRate = 16_000;

    /// <summary>30 ms analysis windows, a common speech-frame size.</summary>
    public const int WindowSamples = SampleRate * 30 / 1000;

    /// <summary>
    /// Speech needs at least this many loud windows (~90 ms in total), so a
    /// single click or bump doesn't count.
    /// </summary>
    public const int MinimumLoudWindows = 3;

    public static CaptureVerdict Evaluate(float[] samples, TimeSpan minimumDuration, float silenceThreshold)
    {
        if (samples.Length < minimumDuration.TotalSeconds * SampleRate)
        {
            return CaptureVerdict.TooShort;
        }

        var loud = 0;
        for (var start = 0; start < samples.Length; start += WindowSamples)
        {
            var end = Math.Min(start + WindowSamples, samples.Length);
            if (Rms(samples, start, end) >= silenceThreshold && ++loud >= MinimumLoudWindows)
            {
                return CaptureVerdict.Speech;
            }
        }

        return CaptureVerdict.Silent;
    }

    public static float Rms(ReadOnlySpan<float> samples) => Rms(samples, 0, samples.Length);

    private static float Rms(ReadOnlySpan<float> samples, int start, int end)
    {
        if (end <= start)
        {
            return 0;
        }

        double sum = 0;
        for (var i = start; i < end; i++)
        {
            sum += samples[i] * samples[i];
        }

        return (float)Math.Sqrt(sum / (end - start));
    }
}
