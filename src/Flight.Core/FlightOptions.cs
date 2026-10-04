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

    /// <summary>Virtual-key code of the push-to-talk key. VK_RMENU = Right Alt.</summary>
    public int PushToTalkVirtualKey { get; set; } = 0xA5;

    /// <summary>Hard ceiling on STT + LLM processing before the pipeline reports an error.</summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// On Quit, how long confirmed dictations get to finish vocabulary
    /// learning before Flight exits anyway. Bounded so Quit never hangs.
    /// </summary>
    public TimeSpan ShutdownVocabularyGrace { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Cloud trial length, measured from the first cloud dictation.</summary>
    public TimeSpan CloudTrialDuration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How stale a cached entitlement may get before the app tries to refresh
    /// it. The PRD leaves this open; a day is short enough that a cancellation
    /// is picked up promptly and long enough that launches aren't
    /// network-bound.
    /// </summary>
    public TimeSpan EntitlementRefreshInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How long a cached entitlement stays usable when Supabase can't be
    /// reached. Covers a normal trip offline without letting a cancelled
    /// subscription run indefinitely. Past it, cloud fails closed; local is
    /// never affected.
    /// </summary>
    public TimeSpan EntitlementOfflineGrace { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Supabase project URL. Shipped in Flight.App's appsettings.json.</summary>
    public Uri? SupabaseUrl { get; set; }

    /// <summary>Supabase anon/publishable key. Public by design, not a secret.</summary>
    public string? SupabaseAnonKey { get; set; }

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
