using Flight.Core.Input;
using Flight.Core.Languages;
using Flight.Core.Llm;

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

    /// <summary>
    /// Which engine performs LLM cleanup. The PRD's "LLM toggle preference".
    /// Local is the default and is free and unrestricted forever.
    /// </summary>
    public LlmMode LlmMode { get; init; } = LlmMode.Local;

    /// <summary>The push-to-talk key or combination (feature #8). Right Alt by default.</summary>
    public Hotkey Hotkey { get; init; } = Hotkey.Default;

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

        if (Hotkey.Validate() is { } problem)
        {
            throw new ArgumentException($"Unusable push-to-talk key '{Hotkey}': {problem}");
        }
    }
}
