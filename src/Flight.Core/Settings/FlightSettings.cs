using Flight.Core.Languages;

namespace Flight.Core.Settings;

/// <summary>
/// The PRD's <c>Settings</c> entity: per-device preferences. Immutable; the
/// store hands out a fresh instance on every change so an in-flight dictation
/// can never see a half-applied update.
/// </summary>
public record FlightSettings
{
    /// <summary>Language the user speaks. Never auto-detected — feature #3 decision.</summary>
    public string SourceLanguage { get; init; } = SupportedLanguages.DefaultCode;

    /// <summary>Language the output is produced in. English by default.</summary>
    public string TargetLanguage { get; init; } = SupportedLanguages.DefaultCode;

    /// <summary>True when the pipeline should translate rather than only clean up.</summary>
    public bool IsTranslating =>
        !string.Equals(SourceLanguage, TargetLanguage, StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (!SupportedLanguages.IsSupported(SourceLanguage))
        {
            throw new ArgumentException($"Unsupported source language '{SourceLanguage}'.");
        }

        if (!SupportedLanguages.IsSupported(TargetLanguage))
        {
            throw new ArgumentException($"Unsupported target language '{TargetLanguage}'.");
        }
    }
}
