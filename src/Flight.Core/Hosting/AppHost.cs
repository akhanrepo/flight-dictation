using Flight.Core.Accounts;
using Flight.Core.Billing;
using Flight.Core.Dictation;
using Flight.Core.Entitlement;
using Flight.Core.Llm;
using Flight.Core.Settings;
using Flight.Core.Storage;
using Flight.Core.Vocabulary;
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
    public static IHost Build(Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton<FlightOptions>();
        builder.Services.AddSingleton<FlightDatabase>();
        builder.Services.AddSingleton<IDictationSessionRepository, SqliteDictationSessionRepository>();
        builder.Services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        builder.Services.AddSingleton<DictationOrchestrator>();

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
        // The entitlement gate wraps the cloud engine only, so local-mode
        // dictation never runs a check and a block always precedes the API call.
        builder.Services.AddSingleton(sp => new CloudLlmEngine(
            new EntitlementGatedLlmCompletion(
                sp.GetRequiredService<CloudLlmCompletion>(),
                sp.GetRequiredService<IEntitlementService>())));
        builder.Services.AddSingleton<ILlmCompletion, LlmModeRouter>();

        // Cleanup runs over whichever engine the router selects.
        builder.Services.AddSingleton<ITextCleaner, PromptTextCleaner>();

        // Accounts, entitlement, and billing (feature #5).
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IDeviceStore, SqliteDeviceStore>();
        builder.Services.AddSingleton<ISupabaseClient, HttpSupabaseClient>();
        builder.Services.AddSingleton<IAccountSession, AccountSession>();
        builder.Services.AddSingleton<IEntitlementService, EntitlementService>();
        builder.Services.AddSingleton<IBillingPortal, StripeBillingPortal>();

        // Platform-bound services (hotkey, audio, STT/LLM engines, clipboard, UI)
        // are registered by the caller: the WinUI shell provides real
        // implementations, tests provide fakes.
        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }
}
