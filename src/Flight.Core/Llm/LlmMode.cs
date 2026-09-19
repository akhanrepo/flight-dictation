namespace Flight.Core.Llm;

/// <summary>
/// Which engine performs LLM work. Persisted as the <c>llm_mode</c> string on
/// every <c>dictation_sessions</c> row, which is the only record feature #5
/// reads to compute trial windows.
/// </summary>
public enum LlmMode
{
    Local,
    Cloud,
}

public static class LlmModes
{
    public const string Local = "local";
    public const string Cloud = "cloud";

    public static string ToStorageValue(this LlmMode mode) => mode switch
    {
        LlmMode.Cloud => Cloud,
        _ => Local,
    };

    /// <summary>Unknown values read as local: local is the free, unrestricted mode.</summary>
    public static LlmMode Parse(string? value) =>
        string.Equals(value, Cloud, StringComparison.OrdinalIgnoreCase) ? LlmMode.Cloud : LlmMode.Local;
}
