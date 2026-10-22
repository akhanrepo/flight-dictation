using System.Text.RegularExpressions;

namespace Flight.Core.Stt;

/// <summary>
/// Strips whisper's non-speech annotations — <c>[BLANK_AUDIO]</c>,
/// <c>(music)</c>, <c>[Silence]</c>, <c>*coughs*</c> — so they never reach the
/// clipboard. A transcript that is nothing but annotations becomes empty.
/// </summary>
public static partial class TranscriptFilter
{
    // Only short bracketed spans with no sentence punctuation inside: real
    // dictation like "(see attached)" is rare but kept when it reads as text.
    [GeneratedRegex(@"\[[^\[\]]{1,40}\]|\((?:[A-Za-z _-]{1,30})\)|\*[A-Za-z _-]{1,30}\*")]
    private static partial Regex Annotation();

    private static readonly HashSet<string> NonSpeechWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "blank_audio", "blank audio", "silence", "music", "applause", "laughter", "laughs", "noise",
        "inaudible", "coughs", "cough", "sighs", "breathing", "background noise", "no speech",
        "music playing", "upbeat music", "sound", "beep", "clicking", "typing", "wind",
    };

    public static string Clean(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var cleaned = Annotation().Replace(transcript, match =>
        {
            var inner = match.Value.Trim('[', ']', '(', ')', '*').Trim();
            // Square brackets are always whisper's; round and starred ones only
            // when they name a sound.
            return match.Value[0] == '[' || NonSpeechWords.Contains(inner) ? " " : match.Value;
        });

        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        // What's left after removing markers can be stray punctuation.
        return cleaned.Any(char.IsLetterOrDigit) ? cleaned : string.Empty;
    }
}
