namespace Flight.Core.Llm;

/// <summary>One instruction-following completion request.</summary>
/// <param name="SystemPrompt">Role/behaviour instruction.</param>
/// <param name="UserPrompt">The content to act on.</param>
public record LlmRequest(string SystemPrompt, string UserPrompt)
{
    public int MaxTokens { get; init; } = 1024;

    /// <summary>Near-greedy by default: every use here is a faithful transform, not creative writing.</summary>
    public float Temperature { get; init; } = 0.1f;
}

/// <summary>
/// The single text-generation seam in the app. Local (llama.cpp) and — from
/// feature #4 — cloud engines implement this, so cleanup, translation, and
/// vocabulary extraction all work against whichever engine is active without
/// knowing which one it is.
/// </summary>
public interface ILlmCompletion
{
    Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
