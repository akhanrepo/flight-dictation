using System.Text.RegularExpressions;

namespace Flight.Core.Vocabulary;

/// <summary>
/// What counts as a vocabulary term: a name, acronym or short piece of
/// jargon — never a sentence. Applied when terms are extracted and again when
/// they are read back, because a stored sentence gets injected into every
/// cleanup prompt and the model happily pastes it into unrelated dictations
/// (it did: "Let's see if it works." showed up in every cleaned transcript).
/// </summary>
public static partial class VocabularyTermRules
{
    public const int MaxWords = 4;
    public const int MaxLength = 40;

    public static bool IsPlausibleTerm(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        var trimmed = term.Trim();
        if (trimmed.Length > MaxLength || !trimmed.Any(char.IsLetter))
        {
            return false;
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > MaxWords)
        {
            return false;
        }

        // Sentence punctuation means a sentence, except dotted acronyms (U.S., e.g.).
        if (trimmed.IndexOfAny(['!', '?', ';', ',', ':']) >= 0)
        {
            return false;
        }

        return words.All(w => !w.Contains('.') || DottedAcronym().IsMatch(w));
    }

    [GeneratedRegex(@"^(?:[A-Za-z]\.)+[A-Za-z]?$")]
    private static partial Regex DottedAcronym();
}
