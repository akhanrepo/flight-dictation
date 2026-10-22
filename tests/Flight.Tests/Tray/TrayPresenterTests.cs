using Flight.Core;
using Flight.Core.Dictation;
using Flight.Core.Models;
using Flight.Core.Tray;
using Flight.Core.Updates;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Tray;

/// <summary>Feature #6: what the tray shows and what its commands do.</summary>
public class TrayPresenterTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapturer _capturer = new();
    private readonly FakeAppUpdater _updater = new();
    private readonly FakeStartupRegistration _startup = new();
    private readonly FakeShellActions _shell = new();
    private readonly DictationOrchestrator _orchestrator;
    private readonly UpdateCheck _updates;

    public TrayPresenterTests()
    {
        _orchestrator = new DictationOrchestrator(
            _hotkey, _capturer, new FakeSpeechToText(), new FakeTextCleaner(), new FakeClipboardWriter(),
            new InMemorySessionRepository(), new FakeDictationUi(), new FakeVocabularyLearningQueue(),
            new FakeSettingsStore(), new FlightOptions(), NullLogger<DictationOrchestrator>.Instance);
        _orchestrator.Start();
        _updates = new UpdateCheck(_updater, NullLogger<UpdateCheck>.Instance);
    }

    private TrayPresenter CreateReady()
    {
        var presenter = new TrayPresenter(_orchestrator, _updates, _startup, _shell);
        presenter.ModelsReady();
        return presenter;
    }

    private static TrayMenuItem Item(TrayState state, TrayCommand command) =>
        state.Menu.Single(i => i.Command == command);

    [Fact]
    public void Tray_ShowsReadyWithTheHotkeyHint()
    {
        var state = CreateReady().State;

        Assert.Equal("Ready — hold Right Alt to dictate", state.StatusLine);
        Assert.Equal("Flight — Ready — hold Right Alt to dictate", state.Tooltip);
        Assert.Equal(TrayIconKind.Normal, state.Icon);
    }

    [Fact]
    public async Task Tray_FollowsTheDictationLoop()
    {
        var presenter = CreateReady();
        var seen = new List<TrayState>();
        presenter.Changed += seen.Add;

        _hotkey.Press();
        _hotkey.Release();
        await _orchestrator.ProcessingTask!;

        // The fakes finish instantly, so check the sequence the tray went through.
        Assert.Equal(
            ["Listening…", "Processing…", "Ready — hold Right Alt to dictate"],
            seen.Select(s => s.StatusLine));
        Assert.Equal(
            [TrayIconKind.Listening, TrayIconKind.Normal, TrayIconKind.Normal],
            seen.Select(s => s.Icon));
    }

    [Fact]
    public void Tray_ShowsPausedWithTheDimmedIcon()
    {
        var presenter = CreateReady();

        presenter.Execute(TrayCommand.PauseResume);

        Assert.True(_orchestrator.IsPaused);
        Assert.StartsWith("Paused", presenter.State.StatusLine);
        Assert.Equal(TrayIconKind.Paused, presenter.State.Icon);
        Assert.Equal("Resume dictation", Item(presenter.State, TrayCommand.PauseResume).Text);
    }

    [Fact]
    public void Tray_ResumeRestoresReady()
    {
        var presenter = CreateReady();
        presenter.Execute(TrayCommand.PauseResume);

        presenter.Execute(TrayCommand.PauseResume);

        Assert.False(_orchestrator.IsPaused);
        Assert.Equal(TrayIconKind.Normal, presenter.State.Icon);
        Assert.Equal("Pause dictation", Item(presenter.State, TrayCommand.PauseResume).Text);
    }

    // The icon is there from launch, so first-run download progress shows in it
    [Fact]
    public void Tray_ShowsModelDownloadProgressUntilReady()
    {
        var presenter = new TrayPresenter(_orchestrator, _updates, _startup, _shell);
        Assert.Equal("Starting…", presenter.State.StatusLine);

        presenter.ReportModelDownload(new ModelDownloadProgress("model.gguf", 42, 100));
        Assert.Equal("Downloading models (42%)", presenter.State.StatusLine);

        presenter.ModelsReady();
        Assert.StartsWith("Ready", presenter.State.StatusLine);
    }

    [Fact]
    public void Tray_TooltipStaysWithinTheWin32Limit()
    {
        var presenter = new TrayPresenter(_orchestrator, _updates, _startup, _shell);
        presenter.ReportModelDownload(new ModelDownloadProgress(new string('x', 500), 1, null));

        Assert.True(presenter.State.Tooltip.Length <= TrayPresenter.MaxTooltipLength);
    }

    [Fact]
    public void Tray_MenuHasTheSpecifiedItemsInOrder()
    {
        var commands = CreateReady().State.Menu
            .Where(i => !i.IsSeparator && i.Visible)
            .Select(i => i.Command)
            .ToArray();

        Assert.Equal(
            [TrayCommand.None, TrayCommand.PauseResume, TrayCommand.Settings,
             TrayCommand.StartWithWindows, TrayCommand.Quit],
            commands);
        Assert.False(CreateReady().State.Menu[0].Enabled); // the status line isn't clickable
    }

    [Fact]
    public void Tray_LeftClickOpensSettings()
    {
        CreateReady().Activate();

        Assert.Equal(["settings"], _shell.Calls);
    }

    [Theory]
    [InlineData(TrayCommand.Settings, "settings")]
    [InlineData(TrayCommand.Quit, "quit")]
    public void Tray_ShellCommandsGoToTheShell(TrayCommand command, string expected)
    {
        CreateReady().Execute(command);

        Assert.Equal([expected], _shell.Calls);
    }

    [Fact]
    public async Task Tray_OffersRestartOnlyOnceAnUpdateIsStaged()
    {
        var presenter = CreateReady();
        Assert.False(Item(presenter.State, TrayCommand.RestartToUpdate).Visible);

        _updater.AvailableVersion = "0.2.0";
        await _updates.RunAsync();

        var restart = Item(presenter.State, TrayCommand.RestartToUpdate);
        Assert.True(restart.Visible);
        Assert.Equal("Restart to update (v0.2.0)", restart.Text);

        presenter.Execute(TrayCommand.RestartToUpdate);
        Assert.Equal(["restart-to-update"], _shell.Calls);
    }

    [Fact]
    public void Tray_StartWithWindowsIsOffByDefaultAndToggles()
    {
        var presenter = CreateReady();
        Assert.False(Item(presenter.State, TrayCommand.StartWithWindows).Checked);

        presenter.Execute(TrayCommand.StartWithWindows);
        Assert.True(_startup.IsEnabled);
        Assert.True(Item(presenter.State, TrayCommand.StartWithWindows).Checked);

        presenter.Execute(TrayCommand.StartWithWindows);
        Assert.False(_startup.IsEnabled);
    }

    // The check mark reflects the real entry, not a remembered preference
    [Fact]
    public void Tray_StartWithWindowsReadsTheLiveEntryOnRefresh()
    {
        var presenter = CreateReady();

        _startup.IsEnabled = true; // e.g. changed in Task Manager's Startup tab
        presenter.Refresh();

        Assert.True(Item(presenter.State, TrayCommand.StartWithWindows).Checked);
    }

    [Fact]
    public void Tray_StartWithWindowsIsDisabledOnDevBuilds()
    {
        _startup.IsAvailable = false;
        var presenter = CreateReady();

        var item = Item(presenter.State, TrayCommand.StartWithWindows);
        Assert.False(item.Enabled);

        presenter.Execute(TrayCommand.StartWithWindows);
        Assert.False(_startup.IsEnabled);
    }

    [Fact]
    public void Tray_RaisesChangedWhenTheStateMoves()
    {
        var presenter = CreateReady();
        var updates = new List<TrayState>();
        presenter.Changed += updates.Add;

        _hotkey.Press();

        Assert.Equal(TrayIconKind.Listening, Assert.Single(updates).Icon);
    }
}
