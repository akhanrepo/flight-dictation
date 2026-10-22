namespace Flight.Core;

/// <summary>
/// App-wide configuration. Defaults target per-user local storage; tests
/// override paths to temp/in-memory locations.
/// </summary>
public class FlightOptions
{
    /// <summary>Configuration section bound onto these options by <c>AppHost</c>.</summary>
    public const string SectionName = "Flight";

    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flight");

    public string DataDirectory { get; set; } = DefaultDataDirectory;

    public string DatabasePath => Path.Combine(DataDirectory, "flight.db");

    public string ModelsDirectory => Path.Combine(DataDirectory, "models");

    /// <summary>
    /// Captures shorter than this are treated as an accidental tap of the
    /// hotkey: nothing runs and nothing is shown.
    /// </summary>
    public TimeSpan MinimumCaptureDuration { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// RMS level (0–1) a 30 ms window must reach to count as sound. Low on
    /// purpose: a quiet laptop mic must not lose a real dictation; whisper's
    /// own non-speech markers catch what slips through.
    /// </summary>
    public float SilenceThreshold { get; set; } = 0.005f;

    /// <summary>Hard ceiling on STT + LLM processing before the pipeline reports an error.</summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// On Quit, how long confirmed dictations get to finish vocabulary
    /// learning before Flight exits anyway. Bounded so Quit never hangs.
    /// </summary>
    public TimeSpan ShutdownVocabularyGrace { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Where installed copies look for new releases: a local folder of
    /// <c>vpk pack</c> output (personal testing) or a public GitHub repo URL.
    /// Null disables auto-update.
    /// </summary>
    public string? UpdateFeed { get; set; }

    /// <summary>
    /// OpenAI-compatible chat-completions endpoint. The PRD leaves the cloud
    /// provider open; every budget-tier candidate (OpenAI, DeepSeek, Groq,
    /// Gemini's compat endpoint) speaks this shape, so switching provider is
    /// this setting plus <see cref="CloudLlmModel"/>, not a code change.
    /// </summary>
    public Uri CloudLlmEndpoint { get; set; } = new("https://api.openai.com/v1/chat/completions");

    /// <summary>
    /// Budget-tier by requirement, not by accident — the PRD rules out
    /// frontier models for the production cloud toggle.
    /// </summary>
    public string CloudLlmModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Ceiling on a single cloud call. Shorter than
    /// <see cref="ProcessingTimeout"/>: a network round trip that slow is a
    /// failure worth telling the user about, not something to wait out.
    /// </summary>
    public TimeSpan CloudLlmTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many learned terms are injected into the cleanup prompt. Kept small
    /// deliberately: every term is prompt tokens, and prompt tokens are local
    /// inference latency on CPU-only hardware.
    /// </summary>
    public int VocabularyContextTermCount { get; set; } = 8;

    /// <summary>
    /// Cosine-similarity floor for injecting a term. Conservative on purpose —
    /// a wrong term in the prompt invites the model to insert words the
    /// speaker never said.
    /// </summary>
    public double VocabularyContextMinimumSimilarity { get; set; } = 0.25;

    /// <summary>
    /// Multilingual whisper base (not the <c>.en</c> variant): feature #3's
    /// five non-English source languages can't be transcribed by the
    /// English-only model.
    /// </summary>
    public ModelSpec SttModel { get; set; } = new(
        "ggml-base-q5_1.bin",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin");

    public ModelSpec LlmModel { get; set; } = new(
        "Phi-4-mini-instruct-Q4_K_M.gguf",
        "https://huggingface.co/bartowski/microsoft_Phi-4-mini-instruct-GGUF/resolve/main/microsoft_Phi-4-mini-instruct-Q4_K_M.gguf");
}

public record ModelSpec(string FileName, string DownloadUrl);
