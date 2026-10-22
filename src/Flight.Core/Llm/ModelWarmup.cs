using Flight.Core.Settings;
using Flight.Core.Stt;
using Microsoft.Extensions.Logging;

namespace Flight.Core.Llm;

/// <summary>
/// Loads the models in the background once they're on disk, so the first
/// dictation after launch doesn't pay for loading whisper and the ~2.5 GB
/// local LLM. Whisper always; the local LLM only when cleanup runs locally
/// (a cloud user shouldn't carry it in RAM), and later if they switch.
/// </summary>
public sealed class ModelWarmup : IDisposable
{
    /// <summary>Half a second of silence: enough to run whisper end to end.</summary>
    private static readonly float[] WarmupAudio = new float[8_000];

    private readonly ISpeechToText _stt;
    private readonly LocalLlmEngine _local;
    private readonly ISettingsStore _settings;
    private readonly ILogger<ModelWarmup> _logger;

    private int _started;
    private int _llmWarmed;

    public ModelWarmup(ISpeechToText stt, LocalLlmEngine local, ISettingsStore settings, ILogger<ModelWarmup> logger)
    {
        _stt = stt;
        _local = local;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Runs once; later calls return immediately. Never throws.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _settings.Changed += OnSettingsChanged;

        try
        {
            await _stt.TranscribeAsync(WarmupAudio, _settings.Current.SourceLanguage, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Speech-to-text warm-up failed; the first dictation will load it instead");
        }

        if (_settings.Current.LlmMode == LlmMode.Local)
        {
            await WarmLlmAsync(cancellationToken);
        }
    }

    private void OnSettingsChanged(FlightSettings settings)
    {
        if (settings.LlmMode == LlmMode.Local)
        {
            _ = WarmLlmAsync(CancellationToken.None);
        }
    }

    private async Task WarmLlmAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _llmWarmed, 1) == 1)
        {
            return;
        }

        try
        {
            // One token: loads the weights and pages them in.
            await _local.Inner.CompleteAsync(
                new LlmRequest("Reply with OK.", "OK") { MaxTokens = 1, Temperature = 0f, Mode = LlmMode.Local },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Local LLM warm-up failed; the first dictation will load it instead");
        }
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;
}
