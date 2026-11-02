using System.Globalization;

namespace Flight.Core.Dictation;

/// <summary>How a dictation ended. Everything but <see cref="Copied"/> left the clipboard alone.</summary>
public enum DictationOutcome
{
    Copied,

    /// <summary>Released before <see cref="FlightOptions.MinimumCaptureDuration"/>: an accidental tap.</summary>
    TooShort,

    /// <summary>The speech gate found nothing above the silence threshold ("Didn't catch that").</summary>
    Silent,

    /// <summary>Whisper returned nothing, or only non-speech markers ("Didn't catch that").</summary>
    NoTranscript,

    /// <summary>Cleanup or translation returned nothing ("Didn't catch that").</summary>
    EmptyCleanup,

    Failed,

    ClipboardFailed,
}

/// <summary>Where one dictation's time went, and how it ended, for <c>logs/timings.log</c>.</summary>
public sealed record DictationTimings(
    DateTimeOffset At,
    TimeSpan Audio,
    TimeSpan SpeechToText,
    TimeSpan Cleanup,
    TimeSpan Total,
    string LlmMode,
    bool Translated)
{
    public DictationOutcome Outcome { get; init; } = DictationOutcome.Copied;

    /// <summary>Loudest 30 ms window's RMS (0–1); compare with <see cref="FlightOptions.SilenceThreshold"/>.</summary>
    public float? PeakLevel { get; init; }

    public string? Error { get; init; }

    public string ToLogLine()
    {
        var peak = PeakLevel is { } level ? FormattableString.Invariant($"peak={level:F4} ") : string.Empty;
        var error = Error is null ? string.Empty : $" error=\"{Error.ReplaceLineEndings(" ")}\"";
        return string.Create(CultureInfo.InvariantCulture,
            $"{At:O} outcome={Outcome.ToString().ToLowerInvariant()} audio={Audio.TotalSeconds:F1}s {peak}" +
            $"stt={SpeechToText.TotalMilliseconds:F0}ms cleanup={Cleanup.TotalMilliseconds:F0}ms " +
            $"total={Total.TotalMilliseconds:F0}ms mode={LlmMode}{(Translated ? " translated" : string.Empty)}{error}");
    }
}

public interface IDictationTimingsLog
{
    /// <summary>Must not throw or block: called on the dictation path.</summary>
    void Record(DictationTimings timings);
}

/// <summary>
/// Appends one line per dictation to <c>logs/timings.log</c> and keeps only
/// the most recent <see cref="MaxLines"/>. The point is to answer "where does
/// my time go?" on the user's own hardware.
/// </summary>
public sealed class FileDictationTimingsLog : IDictationTimingsLog
{
    public const int MaxLines = 200;

    private readonly string _path;
    private readonly object _lock = new();

    public FileDictationTimingsLog(FlightOptions options)
        : this(Path.Combine(options.DataDirectory, "logs", "timings.log"))
    {
    }

    public FileDictationTimingsLog(string path) => _path = path;

    public void Record(DictationTimings timings)
    {
        // Off the dictation path: the clipboard is already written.
        _ = Task.Run(() => Append(timings.ToLogLine()));
    }

    internal void Append(string line)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!); // _path always has a directory
                var lines = File.Exists(_path) ? File.ReadAllLines(_path).ToList() : [];
                lines.Add(line);
                File.WriteAllLines(_path, lines.Count > MaxLines ? lines[^MaxLines..] : lines);
            }
        }
        catch (IOException)
        {
            // Diagnostics only; never worth an error.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
