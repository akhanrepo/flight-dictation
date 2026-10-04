using Flight.Core.Dictation;
using Flight.Core.Models;
using Flight.Core.Startup;
using Flight.Core.Updates;

namespace Flight.Core.Tray;

/// <summary>
/// Turns the app's state (dictation loop, pause, model download, staged
/// update, startup entry) into what the tray shows, and routes tray commands.
/// The Win32 icon only renders <see cref="State"/> and forwards clicks here.
/// </summary>
/// <remarks>
/// <see cref="Changed"/> fires on whichever thread caused the change; the
/// shell marshals to the UI thread before touching the icon.
/// </remarks>
public sealed class TrayPresenter : IDisposable
{
    /// <summary>NOTIFYICONDATA.szTip holds 128 chars including the terminator.</summary>
    public const int MaxTooltipLength = 127;

    private readonly DictationOrchestrator _orchestrator;
    private readonly UpdateCheck _updates;
    private readonly IStartupRegistration _startup;
    private readonly IShellActions _shell;

    private bool _modelsReady;
    private ModelDownloadProgress? _download;

    public TrayPresenter(
        DictationOrchestrator orchestrator,
        UpdateCheck updates,
        IStartupRegistration startup,
        IShellActions shell)
    {
        _orchestrator = orchestrator;
        _updates = updates;
        _startup = startup;
        _shell = shell;

        _orchestrator.StateChanged += OnStateChanged;
        _orchestrator.PausedChanged += OnPausedChanged;
        _updates.Staged += OnUpdateStaged;

        State = Build();
    }

    public TrayState State { get; private set; }

    public event Action<TrayState>? Changed;

    /// <summary>First-run model download progress; the tray shows it until <see cref="ModelsReady"/>.</summary>
    public void ReportModelDownload(ModelDownloadProgress progress)
    {
        _download = progress;
        Refresh();
    }

    public void ModelsReady()
    {
        _modelsReady = true;
        _download = null;
        Refresh();
    }

    /// <summary>
    /// Rebuilds the state. Called before the menu opens too, so the
    /// Start-with-Windows check reflects the real entry at that moment.
    /// </summary>
    public void Refresh()
    {
        State = Build();
        Changed?.Invoke(State);
    }

    /// <summary>Left-click on the icon.</summary>
    public void Activate() => _shell.OpenSettings();

    public void Execute(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.PauseResume:
                if (_orchestrator.IsPaused)
                {
                    _orchestrator.Resume();
                }
                else
                {
                    _orchestrator.Pause();
                }
                break;
            case TrayCommand.StartWithWindows:
                if (!_startup.IsAvailable)
                {
                    break;
                }
                if (_startup.IsEnabled)
                {
                    _startup.Disable();
                }
                else
                {
                    _startup.Enable();
                }
                Refresh();
                break;
            case TrayCommand.Settings:
                _shell.OpenSettings();
                break;
            case TrayCommand.Account:
                _shell.OpenAccount();
                break;
            case TrayCommand.RestartToUpdate:
                if (_updates.StagedVersion is not null)
                {
                    _shell.RestartToUpdate();
                }
                break;
            case TrayCommand.Quit:
                _shell.Quit();
                break;
        }
    }

    public void Dispose()
    {
        _orchestrator.StateChanged -= OnStateChanged;
        _orchestrator.PausedChanged -= OnPausedChanged;
        _updates.Staged -= OnUpdateStaged;
    }

    private void OnStateChanged(DictationState _) => Refresh();

    private void OnPausedChanged(bool _) => Refresh();

    private void OnUpdateStaged(string _) => Refresh();

    private TrayState Build()
    {
        var status = StatusText();
        var tooltip = "Flight — " + status;
        if (tooltip.Length > MaxTooltipLength)
        {
            tooltip = tooltip[..(MaxTooltipLength - 1)] + "…";
        }

        var staged = _updates.StagedVersion;
        IReadOnlyList<TrayMenuItem> menu =
        [
            new(TrayCommand.None, status, Enabled: false),
            TrayMenuItem.Separator,
            new(TrayCommand.PauseResume, _orchestrator.IsPaused ? "Resume dictation" : "Pause dictation"),
            new(TrayCommand.Settings, "Settings…"),
            new(TrayCommand.Account, "Account & subscription…"),
            TrayMenuItem.Separator,
            new(TrayCommand.StartWithWindows, "Start with Windows",
                Enabled: _startup.IsAvailable, Checked: _startup.IsAvailable && _startup.IsEnabled),
            new(TrayCommand.RestartToUpdate, staged is null ? "Restart to update" : $"Restart to update (v{staged})",
                Visible: staged is not null),
            TrayMenuItem.Separator,
            new(TrayCommand.Quit, "Quit Flight"),
        ];

        return new TrayState(tooltip, status, IconFor(), menu);
    }

    private string StatusText()
    {
        if (!_modelsReady)
        {
            return _download?.Percent is { } percent
                ? $"Downloading models ({percent:0}%)"
                : _download is not null ? "Downloading models…" : "Starting…";
        }

        return _orchestrator.State switch
        {
            DictationState.Capturing => "Listening…",
            DictationState.Processing => "Processing…",
            DictationState.Preview => "Preview ready — Enter to copy",
            DictationState.Error => "Something went wrong — see the Flight window",
            _ when _orchestrator.IsPaused => "Paused — Right Alt is off",
            _ => "Ready — hold Right Alt to dictate",
        };
    }

    private TrayIconKind IconFor() => _orchestrator.State switch
    {
        DictationState.Capturing => TrayIconKind.Listening,
        DictationState.Idle or DictationState.Error when _orchestrator.IsPaused => TrayIconKind.Paused,
        _ => TrayIconKind.Normal,
    };
}
