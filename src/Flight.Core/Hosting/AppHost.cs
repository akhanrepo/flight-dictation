using Flight.Core.Dictation;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Settings;
using Flight.Core.Storage;
using Flight.Core.Toast;
using Flight.Core.Tray;
using Flight.Core.Updates;
using Flight.Core.Vocabulary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flight.Core.Hosting;

/// <summary>
/// Builds the generic host that owns dependency injection, configuration, and
/// logging for the app. The WinUI shell and the test suite both boot through
/// this entry point so that service registration stays in one place.
/// </summary>
public static class AppHost
{
    public static IHost Build(
        Action<IServiceCollection>? configureServices = null,
        Action<IConfigurationBuilder>? configureConfiguration = null)
    {
        // Content root is the exe directory, not the working directory, so the
        // appsettings.json shipped beside Flight.App.exe is found however the
        // app was launched.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        configureConfiguration?.Invoke(builder.Configuration);

        // The "Flight" section (appsettings.json, or Flight__* environment
        // variables) overrides FlightOptions defaults — this is how the
        // packaged build gets its update feed.
        builder.Services.AddSingleton(sp =>
        {
            var options = new FlightOptions();
            sp.GetRequiredService<IConfiguration>().GetSection(FlightOptions.SectionName).Bind(options);
            return options;
        });
        builder.Services.AddSingleton<FlightDatabase>();
        builder.Services.AddSingleton<IDictationSessionRepository, SqliteDictationSessionRepository>();
        builder.Services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        builder.Services.AddSingleton<DictationOrchestrator>();

        // Push-to-talk (feature #8) over the shell's IKeyboardHook. The same
        // instance records new hotkeys for the Settings window.
        builder.Services.AddSingleton<PushToTalkHotkey>();
        builder.Services.AddSingleton<IPushToTalkHotkey>(sp => sp.GetRequiredService<PushToTalkHotkey>());

        // The toast's view model is the default dictation UI (feature #7);
        // the window only renders ToastPresenter.View.
        builder.Services.AddSingleton<ToastPresenter>();
        builder.Services.AddSingleton<IDictationUi>(sp => sp.GetRequiredService<ToastPresenter>());
        builder.Services.AddSingleton<IToastPositionStore, JsonToastPositionStore>();

        // Vocabulary (feature #2). The embedding generator is platform-neutral
        // and model-free by default, so this pipeline works before the GGUF has
        // downloaded; the shell overrides it with the llama.cpp embedder.
        builder.Services.AddSingleton<IVocabularyRepository, SqliteVocabularyRepository>();
        builder.Services.AddSingleton<IEmbeddingGenerator>(_ => new HashingEmbeddingGenerator());
        builder.Services.AddSingleton<ITermExtractor, LlmTermExtractor>();
        builder.Services.AddSingleton<IVocabularyContextProvider, VocabularyContextProvider>();
        builder.Services.AddSingleton<VocabularyLearner>();
        builder.Services.AddSingleton<BackgroundVocabularyLearningQueue>();
        builder.Services.AddSingleton<IVocabularyLearningQueue>(
            sp => sp.GetRequiredService<BackgroundVocabularyLearningQueue>());

        // LLM routing (feature #4). The caller registers the local engine as
        // LocalLlmEngine; everything downstream resolves ILlmCompletion and
        // gets the router, so it never learns which engine ran.
        builder.Services.AddSingleton(_ => new HttpClient());
        builder.Services.AddSingleton<CloudLlmCompletion>();
        // Cloud is bring-your-own-key: the user's API key, no account needed.
        builder.Services.AddSingleton(sp => new CloudLlmEngine(sp.GetRequiredService<CloudLlmCompletion>()));
        builder.Services.AddSingleton<ILlmCompletion, LlmModeRouter>();

        // Cleanup runs over whichever engine the router selects.
        builder.Services.AddSingleton<ITextCleaner, PromptTextCleaner>();

        // Auto-update: the shell supplies IAppUpdater (Velopack).
        builder.Services.AddSingleton<UpdateCheck>();

        // Tray (feature #6). The shell supplies IShellActions and
        // IStartupRegistration; the presenter holds all tray decisions.
        builder.Services.AddSingleton<TrayPresenter>();
        builder.Services.AddSingleton<ShutdownCoordinator>();
        builder.Services.AddSingleton<FirstRunNotice>();

        // Platform-bound services (hotkey, audio, STT/LLM engines, clipboard, UI)
        // are registered by the caller: the WinUI shell provides real
        // implementations, tests provide fakes.
        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }
}
