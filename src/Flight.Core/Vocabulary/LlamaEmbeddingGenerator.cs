using Flight.Core.Llm;
using LLama;

namespace Flight.Core.Vocabulary;

/// <summary>
/// Embeddings from the already-loaded local GGUF, via LLamaSharp's embedder.
/// Shares <see cref="LlamaCompletion"/>'s weights so the model is loaded once.
/// Falls back to <see cref="HashingEmbeddingGenerator"/> when the model can't
/// produce an embedding — an empty vocabulary is a degraded experience, not a
/// broken dictation loop.
/// </summary>
public sealed class LlamaEmbeddingGenerator : IEmbeddingGenerator, IDisposable
{
    private readonly LlamaCompletion _completion;
    private readonly IEmbeddingGenerator _fallback;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private LLamaEmbedder? _embedder;

    public LlamaEmbeddingGenerator(LlamaCompletion completion, IEmbeddingGenerator fallback)
    {
        _completion = completion;
        _fallback = fallback;
    }

    /// <summary>
    /// The fallback's width. The real embedder's width is model-defined and
    /// only known after load; retrieval never mixes the two within a store
    /// because <see cref="VectorMath.CosineSimilarity"/> drops mismatched pairs.
    /// </summary>
    public int Dimensions => _fallback.Dimensions;

    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return await _fallback.GenerateAsync(text, cancellationToken);
        }

        try
        {
            var embedder = await GetEmbedderAsync(cancellationToken);
            var embeddings = await embedder.GetEmbeddings(text, cancellationToken);
            var first = embeddings.FirstOrDefault();
            return first is { Length: > 0 }
                ? VectorMath.Normalize(first)
                : await _fallback.GenerateAsync(text, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await _fallback.GenerateAsync(text, cancellationToken);
        }
    }

    private async Task<LLamaEmbedder> GetEmbedderAsync(CancellationToken cancellationToken)
    {
        if (_embedder is not null)
        {
            return _embedder;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_embedder is null)
            {
                var (weights, modelParams) = await _completion.LoadAsync(cancellationToken);
                _embedder = new LLamaEmbedder(weights, modelParams);
            }

            return _embedder;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _embedder?.Dispose();
        _initLock.Dispose();
    }
}
