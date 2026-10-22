using Flight.Core.Llm;
using Flight.Core.Settings;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Llm;

/// <summary>Feature #10: models load in the background so the first dictation isn't the slow one.</summary>
public class ModelWarmupTests
{
    private readonly FakeSpeechToText _stt = new();
    private readonly FakeLlmCompletion _llm = new("local");
    private readonly FakeSettingsStore _settings = new();

    private ModelWarmup Create() =>
        new(_stt, new LocalLlmEngine(_llm), _settings, NullLogger<ModelWarmup>.Instance);

    [Fact]
    public async Task Warmup_LoadsWhisperAndTheLocalLlm()
    {
        await Create().RunAsync();

        Assert.Equal(1, _stt.CallCount);
        var request = Assert.Single(_llm.Requests);
        Assert.Equal(1, request.MaxTokens);
        Assert.Equal(LlmMode.Local, request.Mode);
    }

    // Criterion: the local LLM only when cleanup runs locally
    [Fact]
    public async Task Warmup_SkipsTheLocalLlmInCloudMode()
    {
        await _settings.UpdateAsync(new FlightSettings { LlmMode = LlmMode.Cloud });

        await Create().RunAsync();

        Assert.Equal(1, _stt.CallCount);
        Assert.Empty(_llm.Requests);
    }

    // Criterion: switching to local later warms the LLM then
    [Fact]
    public async Task SwitchingToLocal_WarmsTheLlmOnce()
    {
        await _settings.UpdateAsync(new FlightSettings { LlmMode = LlmMode.Cloud });
        var warmup = Create();
        await warmup.RunAsync();

        await _settings.UpdateAsync(new FlightSettings { LlmMode = LlmMode.Local });
        await _settings.UpdateAsync(new FlightSettings { LlmMode = LlmMode.Local, TargetLanguage = "fr" });

        Assert.Single(_llm.Requests);
    }

    // Criterion: warm-up runs once
    [Fact]
    public async Task Warmup_RunsOnce()
    {
        var warmup = Create();

        await warmup.RunAsync();
        await warmup.RunAsync();

        Assert.Equal(1, _stt.CallCount);
        Assert.Single(_llm.Requests);
    }

    // Criterion: a failure is logged, not shown or thrown
    [Fact]
    public async Task Warmup_SwallowsFailures()
    {
        _stt.ExceptionToThrow = new InvalidOperationException("model missing");
        _llm.ExceptionToThrow = new InvalidOperationException("model missing");

        await Create().RunAsync();

        Assert.Single(_llm.Requests); // whisper failing didn't stop the LLM warm-up
    }
}
