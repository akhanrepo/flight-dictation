using System.Text;
using Flight.Core.Languages;
using Flight.Core.Llm;
using Flight.Core.Models;
using Whisper.net;

namespace Flight.Core.Stt;

/// <summary>
/// whisper.cpp via Whisper.net, using the quantized multilingual model from
/// the model store. The model is loaded on first use (normally by
/// <see cref="ModelWarmup"/> at startup), and one processor per language is
/// kept rather than rebuilt for every dictation.
/// </summary>
public sealed class WhisperSpeechToText : ISpeechToText, IDisposable
{
    private readonly IModelStore _modelStore;

    // One transcription at a time: a processor isn't thread-safe, and two
    // concurrent passes would only split the same CPU between them.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string? _processorLanguage;

    public WhisperSpeechToText(IModelStore modelStore)
    {
        _modelStore = modelStore;
    }

    public async Task<string> TranscribeAsync(
        float[] samples,
        string languageCode = SupportedLanguages.DefaultCode,
        CancellationToken cancellationToken = default)
    {
        if (samples.Length == 0)
        {
            return string.Empty;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var processor = await GetProcessorAsync(languageCode);

            var transcript = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
            {
                transcript.Append(segment.Text);
            }

            return transcript.ToString().Trim();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <remarks>Called under <see cref="_lock"/>.</remarks>
    private async Task<WhisperProcessor> GetProcessorAsync(string languageCode)
    {
        if (_processor is not null && _processorLanguage == languageCode)
        {
            return _processor;
        }

        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }

        _factory ??= WhisperFactory.FromPath(_modelStore.SttModelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage(languageCode)
            .WithThreads(InferenceThreads.Recommended)
            // Each dictation stands alone: conditioning on the previous one's
            // text only invites repetition loops.
            .WithNoContext()
            .Build();
        _processorLanguage = languageCode;
        return _processor;
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
        _lock.Dispose();
    }
}
