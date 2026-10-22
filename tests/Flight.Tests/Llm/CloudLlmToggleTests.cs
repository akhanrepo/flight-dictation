using Flight.Core;
using Flight.Core.Dictation;
using Flight.Core.Llm;
using Flight.Core.Settings;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Llm;

/// <summary>
/// Feature #4 acceptance criteria: the local/cloud toggle, routing through the
/// same interface, and what gets recorded on the session row.
/// </summary>
public class CloudLlmToggleTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeSpeechToText _stt = new();
    private readonly FakeClipboardWriter _clipboard = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly FakeDictationUi _ui = new();
    private readonly FakeVocabularyLearningQueue _learning = new();
    private readonly FakeSettingsStore _settings = new();

    private readonly FakeLlmCompletion _local = new("local") { ResponseToReturn = "Local cleanup." };
    private readonly FakeLlmCompletion _cloud = new("cloud") { ResponseToReturn = "Cloud cleanup." };
    private readonly FakeVocabularyContextProvider _vocabulary = new();

    private DictationOrchestrator CreateOrchestrator()
    {
        var router = new LlmModeRouter(new LocalLlmEngine(_local), new CloudLlmEngine(_cloud), _settings);
        var orchestrator = new DictationOrchestrator(
            _hotkey, new FakeAudioCapturer(), _stt, new PromptTextCleaner(router, _vocabulary),
            _clipboard, _sessions, _ui, _learning, _settings, new FlightOptions(),
            NullLogger<DictationOrchestrator>.Instance);
        orchestrator.Start();
        return orchestrator;
    }

    private async Task<DictationOrchestrator> DictateAsync(DictationOrchestrator? existing = null)
    {
        var orchestrator = existing ?? CreateOrchestrator();
        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;
        return orchestrator;
    }

    private Task SetModeAsync(LlmMode mode) =>
        _settings.UpdateAsync(_settings.Current with { LlmMode = mode });

    // Criterion: the toggle switches LLM mode, and it's a persisted setting
    [Fact]
    public async Task Toggle_Switches_The_Persisted_Llm_Mode()
    {
        Assert.Equal(LlmMode.Local, _settings.Current.LlmMode);

        await SetModeAsync(LlmMode.Cloud);

        Assert.Equal(LlmMode.Cloud, _settings.Current.LlmMode);
    }

    // Criterion: local mode is the default — cloud is opt-in
    [Fact]
    public void Default_Mode_Is_Local()
    {
        Assert.Equal(LlmMode.Local, new FlightSettings().LlmMode);
    }

    // Criterion: cloud mode routes cleanup to the cloud engine instead of the
    // local model, through the same contract
    [Fact]
    public async Task CloudMode_Routes_Cleanup_To_The_Cloud_Engine()
    {
        await SetModeAsync(LlmMode.Cloud);

        await DictateAsync();

        Assert.Single(_cloud.Requests);
        Assert.Empty(_local.Requests);
    }

    [Fact]
    public async Task LocalMode_Routes_Cleanup_To_The_Local_Engine()
    {
        await DictateAsync();

        Assert.Single(_local.Requests);
        Assert.Empty(_cloud.Requests);
    }

    // Criterion: the rest of the pipeline — automatic copy to the clipboard —
    // is unchanged in cloud mode
    [Fact]
    public async Task CloudMode_Leaves_The_Clipboard_Path_Unchanged()
    {
        await SetModeAsync(LlmMode.Cloud);

        var orchestrator = await DictateAsync();

        Assert.Equal("Cloud cleanup.", _clipboard.LastText);
        Assert.Equal(DictationState.Idle, orchestrator.State);
    }

    // Criterion: cloud dictations record llm_mode="cloud" with a timestamp
    [Fact]
    public async Task CloudDictation_Records_CloudMode_With_A_Timestamp()
    {
        await SetModeAsync(LlmMode.Cloud);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var orchestrator = await DictateAsync();

        var session = Assert.Single(_sessions.Sessions);
        Assert.Equal("cloud", session.LlmMode);
        Assert.True(session.CreatedAt >= before);
    }

    [Fact]
    public async Task LocalDictation_Still_Records_LocalMode()
    {
        var orchestrator = await DictateAsync();

        Assert.Equal("local", Assert.Single(_sessions.Sessions).LlmMode);
    }

    // Criterion: toggling mid-session takes effect on the next dictation, no
    // restart required
    [Fact]
    public async Task ModeToggle_MidSession_Applies_To_The_Next_Dictation()
    {
        var orchestrator = await DictateAsync();
        Assert.Single(_local.Requests);

        // No restart, no new orchestrator.
        await SetModeAsync(LlmMode.Cloud);

        await DictateAsync(orchestrator);

        Assert.Single(_cloud.Requests);
        Assert.Equal("cloud", _sessions.Sessions[1].LlmMode);
    }

    // A toggle must not switch engines halfway through a dictation already
    // running — the mode is snapshotted with the rest of the settings
    [Fact]
    public async Task ModeToggle_Does_Not_Affect_An_InFlight_Dictation()
    {
        var orchestrator = CreateOrchestrator();
        _stt.Delay = TimeSpan.FromMilliseconds(50);

        _hotkey.Press();
        _hotkey.Release();
        await SetModeAsync(LlmMode.Cloud);
        await orchestrator.ProcessingTask!;

        Assert.Single(_local.Requests);
        Assert.Empty(_cloud.Requests);
        Assert.Equal("local", _sessions.Sessions[0].LlmMode);
    }

    // Criterion: a failed cloud call is an explicit error state — never a
    // silent fallback to local, and never a stale or empty result
    [Fact]
    public async Task CloudFailure_Surfaces_An_Error_And_Never_Falls_Back_To_Local()
    {
        await SetModeAsync(LlmMode.Cloud);
        _cloud.ExceptionToThrow = new CloudLlmException("The cloud provider rejected the saved API key.");

        var orchestrator = await DictateAsync();

        Assert.Equal(DictationState.Error, orchestrator.State);
        Assert.Contains("rejected the saved API key", _ui.LastErrorMessage);
        Assert.Empty(_local.Requests);
        Assert.Empty(_clipboard.Writes);
        Assert.Empty(_sessions.Sessions);
    }

    // Criterion: after a failed cloud call the user can switch back to local
    // and keep dictating
    [Fact]
    public async Task After_A_Cloud_Failure_Switching_To_Local_Keeps_Dictation_Working()
    {
        await SetModeAsync(LlmMode.Cloud);
        _cloud.ExceptionToThrow = new CloudLlmException("Couldn't reach the cloud model.");

        var orchestrator = await DictateAsync();
        Assert.Equal(DictationState.Error, orchestrator.State);

        orchestrator.DismissError();
        await SetModeAsync(LlmMode.Local);

        await DictateAsync(orchestrator);

        Assert.Equal("Local cleanup.", _clipboard.LastText);
        Assert.Equal("local", Assert.Single(_sessions.Sessions).LlmMode);
    }

    // Feature #11: cloud mode is bring-your-own-key — no cap, no paywall, no
    // account requirement.
    [Fact]
    public async Task CloudMode_Is_Unrestricted()
    {
        await SetModeAsync(LlmMode.Cloud);
        var orchestrator = CreateOrchestrator();

        for (var i = 0; i < 25; i++)
        {
            await DictateAsync(orchestrator);
        }

        Assert.Equal(25, _sessions.Sessions.Count);
        Assert.All(_sessions.Sessions, session => Assert.Equal("cloud", session.LlmMode));
        Assert.Equal(25, _cloud.Requests.Count);
    }

    // Feature #2 composes: vocabulary extraction follows the active engine
    // without extra wiring, because it goes through the same router
    [Fact]
    public async Task VocabularyExtraction_Follows_The_Active_Engine()
    {
        await SetModeAsync(LlmMode.Cloud);
        var router = new LlmModeRouter(new LocalLlmEngine(_local), new CloudLlmEngine(_cloud), _settings);

        await new Flight.Core.Vocabulary.LlmTermExtractor(router).ExtractAsync("We met Contoso.");

        Assert.Single(_cloud.Requests);
        Assert.Empty(_local.Requests);
    }
}
