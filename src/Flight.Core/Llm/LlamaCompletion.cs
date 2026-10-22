using System.Text;
using Flight.Core.Models;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace Flight.Core.Llm;

/// <summary>
/// Local text generation via LLamaSharp (llama.cpp) against the quantized GGUF
/// from the model store. Weights are loaded once (by <see cref="ModelWarmup"/>,
/// or on first use) and shared by every consumer of <see cref="ILlmCompletion"/>.
/// </summary>
public sealed class LlamaCompletion : ILlmCompletion, IDisposable
{
    /// <summary>Contexts are sized in steps of this many tokens.</summary>
    public const int ContextStep = 512;

    public const int MaxContextSize = 4096;

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

        // Phi-4 chat template
        var prompt =
            $"<|system|>{request.SystemPrompt}<|end|>" +
            $"<|user|>{request.UserPrompt}<|end|>" +
            "<|assistant|>";

        // The executor allocates a fresh KV cache per call, sized by the
        // context: a fixed 4096 tokens is ~0.5 GB for Phi-4-mini, mostly
        // unused by a two-sentence dictation. Size it to this request instead.
        var promptTokens = weights.Tokenize(prompt, true, true, Encoding.UTF8).Length;
        var contextParams = new ModelParams(modelParams.ModelPath)
        {
            ContextSize = (uint)ContextSizeFor(promptTokens, request.MaxTokens),
            Threads = modelParams.Threads,
            BatchThreads = modelParams.BatchThreads,
        };
        var executor = new StatelessExecutor(weights, contextParams);

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

    /// <summary>
    /// Room for the prompt and the output budget plus a little slack, rounded
    /// up to <see cref="ContextStep"/> and capped at <see cref="MaxContextSize"/>.
    /// </summary>
    internal static int ContextSizeFor(int promptTokens, int maxTokens)
    {
        var needed = promptTokens + maxTokens + 16;
        var rounded = (needed + ContextStep - 1) / ContextStep * ContextStep;
        return Math.Clamp(rounded, ContextStep, MaxContextSize);
    }

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
                    ContextSize = MaxContextSize,
                    Threads = InferenceThreads.Recommended,
                    BatchThreads = InferenceThreads.Recommended,
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
