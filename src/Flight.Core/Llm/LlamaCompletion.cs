using System.Text;
using Flight.Core.Models;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace Flight.Core.Llm;

/// <summary>
/// Local text generation via LLamaSharp (llama.cpp) against the quantized GGUF
/// from the model store. Weights are loaded lazily on first use and shared by
/// every consumer of <see cref="ILlmCompletion"/> — one model load per process.
/// </summary>
public sealed class LlamaCompletion : ILlmCompletion, IDisposable
{
    private readonly IModelStore _modelStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private LLamaWeights? _weights;
    private ModelParams? _modelParams;

    public LlamaCompletion(IModelStore modelStore)
    {
        _modelStore = modelStore;
    }

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var (weights, modelParams) = await LoadAsync(cancellationToken);
        var executor = new StatelessExecutor(weights, modelParams);

        // Phi-4 chat template
        var prompt =
            $"<|system|>{request.SystemPrompt}<|end|>" +
            $"<|user|>{request.UserPrompt}<|end|>" +
            "<|assistant|>";

        var inferenceParams = new InferenceParams
        {
            MaxTokens = request.MaxTokens,
            AntiPrompts = ["<|end|>", "<|user|>"],
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = request.Temperature },
        };

        var output = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            output.Append(token);
        }

        return output.ToString().Replace("<|end|>", string.Empty).Trim();
    }

    /// <summary>Loaded weights, shared with the embedding generator to avoid a second model load.</summary>
    internal async Task<(LLamaWeights Weights, ModelParams Params)> LoadAsync(CancellationToken cancellationToken)
    {
        if (_weights is not null && _modelParams is not null)
        {
            return (_weights, _modelParams);
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_weights is null)
            {
                _modelParams = new ModelParams(_modelStore.LlmModelPath)
                {
                    ContextSize = 4096,
                };
                _weights = await LLamaWeights.LoadFromFileAsync(_modelParams, cancellationToken);
            }

            return (_weights, _modelParams!);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _weights?.Dispose();
        _initLock.Dispose();
    }
}
