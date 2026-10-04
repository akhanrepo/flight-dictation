using System.Text;
using System.Text.RegularExpressions;

namespace Flight.Core.Llm;

/// <summary>
/// Catches cleanup output that says things the speaker didn't. Cleanup only
/// removes and repunctuates, so almost every word it returns should already be
/// in the transcript; a run of new words means the model invented or
/// continued text. Dictation output gets pasted into client email, so
/// invented text is worse than an imperfectly tidied transcript.
/// </summary>
/// <remarks>Not used for translation, where every word is legitimately new.</remarks>
public static partial class CleanupGuard
{
    /// <summary>
    /// Invented text arrives as a run of new words ("Let's see if it works");
    /// genuine fixes are isolated ("there" for "their", "going to" for "gonna").
    /// </summary>
    public const int MaxNewWordRun = 3;

    /// <summary>Above this share of new words the output is treated as invented, runs or not.</summary>
    public const double MaxNewWordShare = 0.3;

    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "um", "umm", "uh", "uhh", "er", "erm", "ah", "hmm", "mm",
    };

    /// <param name="raw">The transcript the model was given.</param>
    /// <param name="cleaned">What the model returned.</param>
    /// <param name="allowedWords">Learned vocabulary: correcting to these spellings is expected.</param>
    public static bool IsFaithful(string raw, string cleaned, IEnumerable<string> allowedWords)
    {
        var known = new HashSet<string>(Words(raw), StringComparer.Ordinal);
        foreach (var term in allowedWords)
        {
            known.UnionWith(Words(term));
        }

        var output = Words(cleaned).ToList();
        if (output.Count == 0)
        {
            // Nothing came back for a transcript that had words.
            return known.Count == 0;
        }

        var novel = 0;
        var run = 0;
        foreach (var word in output)
        {
            if (known.Contains(word))
            {
                run = 0;
                continue;
            }

            novel++;
            if (++run > MaxNewWordRun)
            {
                return false;
            }
        }

        return novel <= MaxNewWordRun || (double)novel / output.Count <= MaxNewWordShare;
    }

    /// <summary>
    /// Rule-based tidy-up used when the model's output can't be trusted:
    /// drops fillers and immediate repeats, capitalizes, ends the sentence.
    /// Plainer than the model's, but every word is the speaker's.
    /// </summary>
    public static string FallbackClean(string raw)
    {
        var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Fillers.Contains(w.Trim(',', '.')))
            .ToList();

        var kept = new List<string>();
        foreach (var word in words)
        {
            if (kept.Count == 0 || !string.Equals(kept[^1], word, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(word);
            }
        }

        if (kept.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder(string.Join(' ', kept));
        text[0] = char.ToUpperInvariant(text[0]);
        if (!".!?".Contains(text[^1]))
        {
            text.Append('.');
        }
        return text.ToString();
    }

    /// <summary>Lower-cased words with apostrophes dropped, so "Let's" and "lets" match.</summary>
    private static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text.Replace("'", string.Empty).Replace("’", string.Empty))
            .Select(m => m.Value.ToLowerInvariant());

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
