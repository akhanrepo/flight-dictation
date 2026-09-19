using Flight.App.Audio;
using Flight.App.Clipboard;
using Flight.App.Platform;
using Flight.App.Security;
using Flight.App.Views;
using Flight.App.Win32;
using Flight.Core;
using Flight.Core.Accounts;
using Flight.Core.Audio;
using Flight.Core.Billing;
using Flight.Core.Entitlement;
using Flight.Core.Clipboard;
using Flight.Core.Dictation;
using Flight.Core.Hosting;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Models;
using Flight.Core.Security;
using Flight.Core.Settings;
using Flight.Core.Stt;
using Flight.Core.Tools;
using Flight.Core.Vocabulary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Flight.App;

public partial class App : Application
{
    private DictationToastWindow? _window;
    private DictationOrchestrator? _orchestrator;

    public IHost Host { get; }

    public App()
    {
        InitializeComponent();
        Host = AppHost.Build(services =>
        {
            services.AddSingleton<IPushToTalkHotkey, LowLevelKeyboardHook>();
            services.AddSingleton<IAudioCapturer, WasapiAudioCapturer>();
            services.AddSingleton<ISpeechToText, WhisperSpeechToText>();
            services.AddSingleton<LlamaCompletion>();
            services.AddSingleton(sp => new LocalLlmEngine(sp.GetRequiredService<LlamaCompletion>()));
            services.AddSingleton<ISecretStore, DpapiSecretStore>();
            services.AddSingleton<IUrlLauncher, BrowserUrlLauncher>();
            // Real embeddings come from the same loaded GGUF, with the
            // model-free generator as the fallback path.
            services.AddSingleton<IEmbeddingGenerator>(sp => new LlamaEmbeddingGenerator(
                sp.GetRequiredService<LlamaCompletion>(),
                new HashingEmbeddingGenerator()));
            services.AddSingleton<IClipboardWriter, WindowsClipboardWriter>();
            services.AddSingleton(sp => new HttpModelStore(sp.GetRequiredService<FlightOptions>(), new HttpClient()));
            services.AddSingleton<IModelStore>(sp => sp.GetRequiredService<HttpModelStore>());
            services.AddSingleton<DeferredDictationUi>();
            services.AddSingleton<IDictationUi>(sp => sp.GetRequiredService<DeferredDictationUi>());
        });
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new DictationToastWindow();
        var ui = new DictationUiPresenter(_window, DispatcherQueue.GetForCurrentThread());
        Host.Services.GetRequiredService<DeferredDictationUi>().Inner = ui;

        _orchestrator = Host.Services.GetRequiredService<DictationOrchestrator>();
        _window.Orchestrator = _orchestrator;
        _window.OpenSettings = ShowSettings;
        _window.StartUpgrade = ShowAccount;

        try
        {
            // First run: models aren't bundled; download with visible progress.
            var modelStore = Host.Services.GetRequiredService<IModelStore>();
            if (!modelStore.ModelsArePresent)
            {
                var progress = new Progress<ModelDownloadProgress>(ui.ShowModelDownloadProgress);
                await modelStore.EnsureModelsAsync(progress);
                ui.Hide();
            }
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
    }

    private SettingsWindow? _settingsWindow;

    private void ShowSettings()
    {
        // One settings window; re-activate rather than stacking duplicates.
        _settingsWindow ??= new SettingsWindow(
            Host.Services.GetRequiredService<ISettingsStore>(),
            Host.Services.GetRequiredService<ISecretStore>());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private AccountWindow? _accountWindow;

    private void ShowAccount()
    {
        _accountWindow ??= new AccountWindow(
            Host.Services.GetRequiredService<IAccountSession>(),
            Host.Services.GetRequiredService<IBillingPortal>(),
            Host.Services.GetRequiredService<IEntitlementService>());
        _accountWindow.Closed += (_, _) => _accountWindow = null;
        _accountWindow.Activate();
    }

    /// <summary>
    /// PRD validation aid: measures real local-LLM cleanup latency on this
    /// machine. Results land in the toast (confirm copies them) and in
    /// benchmark-results.txt under the data directory.
    /// </summary>
    private async Task RunBenchmarkAsync(DictationUiPresenter ui)
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

        ui.ShowPreview(report);
    }
}
