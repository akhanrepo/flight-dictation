using Flight.Core.Llm;
using Xunit;

namespace Flight.Tests.Llm;

/// <summary>Feature #10: the local LLM's context and thread counts.</summary>
public class InferenceSizingTests
{
    // Criterion: context = prompt + output budget, rounded up to 512, capped at 4096
    [Theory]
    [InlineData(200, 64, 512)]    // a short dictation: the smallest context
    [InlineData(400, 120, 1024)]  // 536 needed -> next step
    [InlineData(496, 0, 512)]     // exactly fills a step with the slack
    [InlineData(497, 0, 1024)]
    [InlineData(3000, 2048, 4096)] // capped
    public void ContextSize_FitsTheRequest(int promptTokens, int maxTokens, int expected)
    {
        Assert.Equal(expected, LlamaCompletion.ContextSizeFor(promptTokens, maxTokens));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]   // small machines use every core
    [InlineData(6, 6)]
    [InlineData(8, 4)]   // hyper-threaded: about one per physical core
    [InlineData(16, 8)]
    [InlineData(64, 8)]  // capped
    public void Threads_AboutOnePerPhysicalCore(int logical, int expected)
    {
        Assert.Equal(expected, InferenceThreads.For(logical));
    }
}
