namespace Flight.Core.Llm;

/// <summary>One instruction-following completion request.</summary>
/// <param name="SystemPrompt">Role/behaviour instruction.</param>
/// <param name="UserPrompt">The content to act on.</param>
public record LlmRequest(string SystemPrompt, string UserPrompt)
{
    public int MaxTokens { get; init; } = 1024;

    /// <summary>Near-greedy by default: every use here is a faithful transform, not creative writing.</summary>
    public float Temperature { get; init; } = 0.1f;

    /// <summary>
    /// Which engine to route to. Carried on the request rather than read live,
    /// so a mid-dictation toggle can't switch engines halfway through the one
    /// already running. Null means "whatever is currently selected".
    /// </summary>
    public LlmMode? Mode { get; init; }
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
