namespace Flight.Core.Languages;

/// <param name="Code">Whisper/ISO-639-1 language code, also what's stored on the session row.</param>
public record SupportedLanguage(string Code, string EnglishName, string NativeName);

/// <summary>
/// The fixed language set for feature #3. A seventh language is a deliberate
/// later addition, not a config change — this list is the single source of
/// truth for both the settings UI and validation.
/// </summary>
public static class SupportedLanguages
{
    public const string DefaultCode = "en";

    public static IReadOnlyList<SupportedLanguage> All { get; } =
    [
        new("en", "English", "English"),
        new("ur", "Urdu", "اردو"),
        new("ar", "Arabic", "العربية"),
        new("es", "Spanish", "Español"),
        new("fr", "French", "Français"),
        new("zh", "Mandarin", "中文"),
    ];

    public static bool IsSupported(string code) =>
        All.Any(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));

    public static SupportedLanguage Get(string code) =>
        All.FirstOrDefault(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"'{code}' is not one of the supported languages.", nameof(code));

    /// <summary>English name, used when instructing the LLM which way to translate.</summary>
    public static string EnglishNameOf(string code) => Get(code).EnglishName;
}
