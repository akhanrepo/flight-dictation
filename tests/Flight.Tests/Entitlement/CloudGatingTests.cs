using Flight.Core;
using Flight.Core.Dictation;
using Flight.Core.Entitlement;
using Flight.Core.Llm;
using Flight.Core.Settings;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Entitlement;

/// <summary>
/// Feature #5 acceptance criteria at the pipeline level: local is never
/// gated, a blocked cloud call never reaches the API, and expiry blocks cloud
/// only.
/// </summary>
public class CloudGatingTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeClipboardWriter _clipboard = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly FakeDictationUi _ui = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly FakeEntitlementService _entitlement = new();

    private readonly FakeLlmCompletion _local = new("local") { ResponseToReturn = "Local cleanup." };
    private readonly FakeLlmCompletion _cloud = new("cloud") { ResponseToReturn = "Cloud cleanup." };

    private DictationOrchestrator CreateOrchestrator()
    {
        var gatedCloud = new EntitlementGatedLlmCompletion(_cloud, _entitlement);
        var router = new LlmModeRouter(
            new LocalLlmEngine(_local), new CloudLlmEngine(gatedCloud), _settings);

        var orchestrator = new DictationOrchestrator(
            _hotkey, new FakeAudioCapturer(), new FakeSpeechToText(),
            new PromptTextCleaner(router, new FakeVocabularyContextProvider()),
            _clipboard, _sessions, _ui, new FakeVocabularyLearningQueue(), _settings,
            new FlightOptions(), NullLogger<DictationOrchestrator>.Instance);
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

    private void BlockCloud(bool offersUpgrade = true) =>
        _entitlement.DecisionToReturn = new EntitlementDecision(
            false,
            offersUpgrade ? EntitlementStatus.TrialExpired : EntitlementStatus.Unverifiable,
            "Your 30-day cloud trial has ended. Local mode stays free and unlimited.",
            OffersUpgrade: offersUpgrade);

    // Criterion: local-only dictation never prompts for an account, ever — no
    // entitlement check runs against it
    [Fact]
    public async Task LocalDictation_Never_Runs_An_Entitlement_Check()
    {
        BlockCloud();

        var orchestrator = await DictateAsync();
        await orchestrator.ConfirmAsync();

        Assert.Equal(0, _entitlement.EvaluationCount);
        Assert.Equal("Local cleanup.", _clipboard.LastText);
    }

    [Fact]
    public async Task LocalDictation_Is_Unaffected_Even_After_Many_Uses()
    {
        BlockCloud();
        var orchestrator = CreateOrchestrator();

        for (var i = 0; i < 10; i++)
        {
            await DictateAsync(orchestrator);
            await orchestrator.ConfirmAsync();
        }

        Assert.Equal(10, _sessions.Sessions.Count);
        Assert.Equal(0, _entitlement.EvaluationCount);
    }

    // Criterion: a blocked cloud dictation is stopped before the API call is
    // made — the user is not silently charged
    [Fact]
    public async Task BlockedCloudDictation_Never_Reaches_The_Api()
    {
        await SetModeAsync(LlmMode.Cloud);
        BlockCloud();

        await DictateAsync();

        Assert.Equal(1, _entitlement.EvaluationCount);
        Assert.Empty(_cloud.Requests);
    }

    // Criterion: blocked with a clear upgrade prompt — not silently denied
    [Fact]
    public async Task BlockedCloudDictation_Shows_An_Upgrade_Prompt()
    {
        await SetModeAsync(LlmMode.Cloud);
        BlockCloud();

        var orchestrator = await DictateAsync();

        Assert.Equal(DictationState.Error, orchestrator.State);
        Assert.Contains("upgrade", _ui.Events);
        Assert.True(_ui.LastUpgradePrompt!.OffersUpgrade);
        Assert.NotEmpty(_ui.LastUpgradePrompt.Message);
        Assert.Empty(_clipboard.Writes);
        Assert.Empty(_sessions.Sessions);
    }

    // "Can't verify" is a different prompt: paying again wouldn't resolve it
    [Fact]
    public async Task UnverifiableEntitlement_Prompts_Without_Offering_Checkout()
    {
        await SetModeAsync(LlmMode.Cloud);
        BlockCloud(offersUpgrade: false);

        await DictateAsync();

        Assert.Equal(EntitlementStatus.Unverifiable, _ui.LastUpgradePrompt!.Status);
        Assert.False(_ui.LastUpgradePrompt.OffersUpgrade);
    }

    // Criterion: local mode remains fully available when the cloud trial
    // expires — expiry blocks cloud calls only, never the app as a whole
    [Fact]
    public async Task ExpiredTrial_Blocks_Cloud_But_Local_Keeps_Working()
    {
        await SetModeAsync(LlmMode.Cloud);
        BlockCloud();

        var orchestrator = await DictateAsync();
        Assert.Equal(DictationState.Error, orchestrator.State);

        orchestrator.DismissError();
        await SetModeAsync(LlmMode.Local);

        await DictateAsync(orchestrator);
        await orchestrator.ConfirmAsync();

        Assert.Equal("Local cleanup.", _clipboard.LastText);
        Assert.Equal("local", Assert.Single(_sessions.Sessions).LlmMode);
    }

    // Criterion: an entitled cloud dictation passes straight through
    [Fact]
    public async Task EntitledCloudDictation_Proceeds_Normally()
    {
        await SetModeAsync(LlmMode.Cloud);

        var orchestrator = await DictateAsync();
        await orchestrator.ConfirmAsync();

        Assert.Equal(1, _entitlement.EvaluationCount);
        Assert.Single(_cloud.Requests);
        Assert.Equal("Cloud cleanup.", _clipboard.LastText);
        Assert.Equal("cloud", Assert.Single(_sessions.Sessions).LlmMode);
    }

    // Criterion: after checkout, cloud dictation works again with no restart
    [Fact]
    public async Task After_Entitlement_Is_Restored_Cloud_Works_Again()
    {
        await SetModeAsync(LlmMode.Cloud);
        BlockCloud();

        var orchestrator = await DictateAsync();
        Assert.Equal(DictationState.Error, orchestrator.State);
        orchestrator.DismissError();

        // Checkout completed; the next evaluation sees the paid tier.
        _entitlement.DecisionToReturn = new EntitlementDecision(true, EntitlementStatus.Paid);

        await DictateAsync(orchestrator);
        await orchestrator.ConfirmAsync();

        Assert.Equal("Cloud cleanup.", _clipboard.LastText);
    }

    // The gate must not run after the call: a block means no request at all
    [Fact]
    public async Task Gate_Runs_Before_The_Delegate_Not_After()
    {
        var gated = new EntitlementGatedLlmCompletion(_cloud, _entitlement);
        BlockCloud();

        await Assert.ThrowsAsync<EntitlementRequiredException>(() =>
            gated.CompleteAsync(new LlmRequest("s", "u")));

        Assert.Empty(_cloud.Requests);
    }
}
