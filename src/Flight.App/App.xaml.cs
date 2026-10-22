using Flight.App.Audio;
using Flight.App.Clipboard;
using Flight.App.Platform;
using Flight.App.Security;
using Flight.App.Views;
using Flight.App.Win32;
using Flight.Core;
using Flight.Core.Audio;
using Flight.Core.Clipboard;
using Flight.Core.Dictation;
using Flight.Core.Hosting;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Models;
using Flight.Core.Security;
using Flight.Core.Settings;
using Flight.Core.Startup;
using Flight.Core.Stt;
using Flight.Core.Toast;
using Flight.Core.Tools;
using Flight.Core.Tray;
using Flight.Core.Updates;
using Flight.Core.Vocabulary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Flight.App;

public partial class App : Application, IShellActions
{
    private DictationToastWindow? _window;
    private DictationOrchestrator? _orchestrator;
    private NotifyIcon? _tray;
    private TrayPresenter? _trayPresenter;
    private DispatcherQueue? _dispatcher;
    private bool _quitting;

    public IHost Host { get; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => ErrorLog.Write("Unhandled UI exception", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => ErrorLog.Write("Unobserved task exception", e.Exception);
        Host = AppHost.Build(services =>
        {
            // The raw keyboard feed; Core's PushToTalkHotkey (registered by
            // AppHost) turns it into push-to-talk.
            services.AddSingleton<IKeyboardHook, LowLevelKeyboardHook>();
            services.AddSingleton<IAudioCapturer, WasapiAudioCapturer>();
            services.AddSingleton<ISpeechToText, WhisperSpeechToText>();
            services.AddSingleton<LlamaCompletion>();
            services.AddSingleton(sp => new LocalLlmEngine(sp.GetRequiredService<LlamaCompletion>()));
            services.AddSingleton<ISecretStore, DpapiSecretStore>();
            services.AddSingleton<IAppUpdater, VelopackAppUpdater>();
            services.AddSingleton<IStartupRegistration, RegistryStartupRegistration>();
            services.AddSingleton<IShellActions>(this);
            // Real embeddings come from the same loaded GGUF, with the
            // model-free generator as the fallback path.
            services.AddSingleton<IEmbeddingGenerator>(sp => new LlamaEmbeddingGenerator(
                sp.GetRequiredService<LlamaCompletion>(),
                new HashingEmbeddingGenerator()));
            services.AddSingleton<IClipboardWriter, WindowsClipboardWriter>();
            services.AddSingleton(sp => new HttpModelStore(sp.GetRequiredService<FlightOptions>(), new HttpClient()));
            services.AddSingleton<IModelStore>(sp => sp.GetRequiredService<HttpModelStore>());
        });
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await LaunchAsync();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Startup failed", ex);
            throw;
        }
    }

    private async Task LaunchAsync()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        var ui = Host.Services.GetRequiredService<ToastPresenter>();
        // The indicator's view model (ToastPresenter) is AppHost's default
        // IDictationUi; the window renders it.
        _window = new DictationToastWindow(ui, Host.Services.GetRequiredService<IToastPositionStore>());

        // View changes arrive from the hotkey hook and background tasks; the
        // window renders on the UI thread.
        ui.Changed += view =>
        {
            if (_dispatcher.HasThreadAccess)
            {
                _window.Render(view);
            }
            else
            {
                _dispatcher.TryEnqueue(() => _window.Render(view));
            }
        };
        // ~20 a second from the audio thread while listening.
        ui.LevelChanged += level => _dispatcher.TryEnqueue(() => _window.RenderLevel(level));

        _orchestrator = Host.Services.GetRequiredService<DictationOrchestrator>();
        _window.Orchestrator = _orchestrator;

        // The tray comes first, so Flight is visibly running (and quittable)
        // even during the first-run model download.
        StartTray();
        if (Program.Instance is { } instance)
        {
            instance.ActivationRequested += () => _dispatcher.TryEnqueue(ShowSettings);
        }

        try
        {
            // First run: models aren't bundled; download with visible progress.
            var modelStore = Host.Services.GetRequiredService<IModelStore>();
            if (!modelStore.ModelsArePresent)
            {
                var progress = new Progress<ModelDownloadProgress>(p =>
                {
                    ui.ShowModelDownloadProgress(p);
                    _trayPresenter?.ReportModelDownload(p);
                });
                await modelStore.EnsureModelsAsync(progress);
                ui.Hide();
            }
            _trayPresenter?.ModelsReady();
        }
        catch (Exception ex)
        {
            ui.ShowError($"Model download failed: {ex.Message}. Check your connection and restart Flight.");
            return;
        }

        if (Environment.GetCommandLineArgs().Contains("--benchmark"))
        {
            await RunBenchmarkAsync(ui);
            return;
        }

        _orchestrator.Start();

        // Background; applies on exit and never throws.
        _ = Host.Services.GetRequiredService<UpdateCheck>().RunAsync();
    }

    private void StartTray()
    {
        _trayPresenter = Host.Services.GetRequiredService<TrayPresenter>();
        _tray = new NotifyIcon(AppIcons.For);
        _tray.Activated += _trayPresenter.Activate;
        _tray.MenuOpening += _trayPresenter.Refresh;
        _tray.CommandInvoked += _trayPresenter.Execute;

        // Presenter changes arrive from the hotkey hook and background tasks;
        // the icon is only touched on the UI thread.
        _trayPresenter.Changed += state =>
        {
            if (_dispatcher!.HasThreadAccess) // set before StartTray runs
            {
                _tray?.Render(state);
            }
            else
            {
                _dispatcher.TryEnqueue(() => _tray?.Render(state));
            }
        };
        _tray.Render(_trayPresenter.State);

        if (Host.Services.GetRequiredService<FirstRunNotice>().TryClaim())
        {
            _tray.ShowNotification(
                FirstRunNotice.Title,
                FirstRunNotice.MessageFor(Host.Services.GetRequiredService<ISettingsStore>().Current.Hotkey));
        }
    }

    void IShellActions.OpenSettings() => ShowSettings();

    async void IShellActions.Quit()
    {
        if (await ShutDownAsync())
        {
            Exit();
        }
    }

    async void IShellActions.RestartToUpdate()
    {
        if (await ShutDownAsync())
        {
            // Exits the process, applies the update, and relaunches.
            Host.Services.GetRequiredService<IAppUpdater>().RestartToApply();
        }
    }

    /// <returns>False when a shutdown is already under way.</returns>
    private async Task<bool> ShutDownAsync()
    {
        if (_quitting)
        {
            return false;
        }
        _quitting = true;

        await Host.Services.GetRequiredService<ShutdownCoordinator>().ShutdownAsync();
        _trayPresenter?.Dispose();
        _tray?.Dispose(); // removes the icon, so no ghost is left in the tray
        _tray = null;
        return true;
    }

    private SettingsWindow? _settingsWindow;

    private void ShowSettings()
    {
        // One settings window; re-activate rather than stacking duplicates.
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
                Host.Services.GetRequiredService<ISettingsStore>(),
                Host.Services.GetRequiredService<ISecretStore>(),
                Host.Services.GetRequiredService<PushToTalkHotkey>());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
    }

    /// <summary>
    /// PRD validation aid: measures real local-LLM cleanup latency on this
    /// machine. Results are copied to the clipboard and written to
    /// benchmark-results.txt under the data directory.
    /// </summary>
    private async Task RunBenchmarkAsync(ToastPresenter ui)
    {
        ui.ShowProcessing();
        var cleaner = Host.Services.GetRequiredService<ITextCleaner>();
        var options = Host.Services.GetRequiredService<FlightOptions>();

        string report;
        try
        {
            report = await Task.Run(() => CleanerBenchmark.RunAsync(cleaner));
            File.WriteAllText(Path.Combine(options.DataDirectory, "benchmark-results.txt"), report);
        }
        catch (Exception ex)
        {
            ui.ShowError($"Benchmark failed: {ex.Message}");
            return;
        }

        Host.Services.GetRequiredService<IClipboardWriter>().SetText(report);
        ui.ShowCopied();
    }
}
