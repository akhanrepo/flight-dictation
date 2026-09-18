namespace Flight.Core;

/// <summary>
/// App-wide configuration. Defaults target per-user local storage; tests
/// override paths to temp/in-memory locations.
/// </summary>
public class FlightOptions
{
    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flight");

    public string DataDirectory { get; set; } = DefaultDataDirectory;

    public string DatabasePath => Path.Combine(DataDirectory, "flight.db");

    public string ModelsDirectory => Path.Combine(DataDirectory, "models");

    /// <summary>Virtual-key code of the push-to-talk key. VK_RMENU = Right Alt.</summary>
    public int PushToTalkVirtualKey { get; set; } = 0xA5;

    /// <summary>Hard ceiling on STT + LLM processing before the pipeline reports an error.</summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public ModelSpec SttModel { get; set; } = new(
        "ggml-base.en-q5_1.bin",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en-q5_1.bin");

    public ModelSpec LlmModel { get; set; } = new(
        "Phi-4-mini-instruct-Q4_K_M.gguf",
        "https://huggingface.co/bartowski/microsoft_Phi-4-mini-instruct-GGUF/resolve/main/microsoft_Phi-4-mini-instruct-Q4_K_M.gguf");
}

public record ModelSpec(string FileName, string DownloadUrl);
