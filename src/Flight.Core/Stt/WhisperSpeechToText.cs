using System.Text;
using Flight.Core.Models;
using Whisper.net;

namespace Flight.Core.Stt;

/// <summary>
/// whisper.cpp via Whisper.net, using the quantized English model from the
/// model store. The factory is loaded lazily on first transcription so app
/// startup doesn't block on model load.
/// </summary>
public sealed class WhisperSpeechToText : ISpeechToText, IDisposable
{
    private readonly IModelStore _modelStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private WhisperFactory? _factory;

    public WhisperSpeechToText(IModelStore modelStore)
    {
        _modelStore = modelStore;
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken = default)
    {
        if (samples.Length == 0)
        {
            return string.Empty;
        }

        var factory = await GetFactoryAsync(cancellationToken);
        await using var processor = factory.CreateBuilder()
            .WithLanguage("en")
            .Build();

        var transcript = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
        {
            transcript.Append(segment.Text);
        }

        return transcript.ToString().Trim();
    }

    private async Task<WhisperFactory> GetFactoryAsync(CancellationToken cancellationToken)
    {
        if (_factory is not null)
        {
            return _factory;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            _factory ??= WhisperFactory.FromPath(_modelStore.SttModelPath);
            return _factory;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _initLock.Dispose();
    }
}
