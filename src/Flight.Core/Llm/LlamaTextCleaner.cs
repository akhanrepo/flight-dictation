using System.Text;
using Flight.Core.Models;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace Flight.Core.Llm;

/// <summary>
/// Local LLM cleanup via LLamaSharp (llama.cpp) with a quantized GGUF model.
/// Fixed instruction: remove fillers, fix punctuation/structure, never change
/// meaning or add content. Model weights are loaded lazily on first use.
/// </summary>
public sealed class LlamaTextCleaner : ITextCleaner, IDisposable
{
    private const string SystemPrompt =
        "You clean up raw speech-to-text transcripts. Remove filler words (um, uh, like, you know), " +
        "false starts, and repetitions. Fix punctuation, capitalization, and sentence structure. " +
        "Preserve the speaker's meaning, wording, and all factual content exactly. " +
        "Never add information, never summarize, never answer questions in the transcript. " +
        "Output only the cleaned text, nothing else.";

    private readonly IModelStore _modelStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private LLamaWeights? _weights;
    private ModelParams? _modelParams;

    public LlamaTextCleaner(IModelStore modelStore)
    {
        _modelStore = modelStore;
    }

    public async Task<string> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawTranscript))
        {
            return string.Empty;
        }

        var (weights, modelParams) = await GetModelAsync(cancellationToken);
        var executor = new StatelessExecutor(weights, modelParams);

        // Phi-4 chat template
        var prompt =
            $"<|system|>{SystemPrompt}<|end|>" +
            $"<|user|>{rawTranscript}<|end|>" +
            "<|assistant|>";

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 1024,
            AntiPrompts = ["<|end|>", "<|user|>"],
            SamplingPipeline = new DefaultSamplingPipeline
            {
                // Cleanup is a faithful-transform task; keep sampling near-greedy
                Temperature = 0.1f,
            },
        };

        var output = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            output.Append(token);
        }

        return output.ToString().Replace("<|end|>", string.Empty).Trim();
    }

    private async Task<(LLamaWeights Weights, ModelParams Params)> GetModelAsync(CancellationToken cancellationToken)
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
