using Flight.Core.Dictation;
using Flight.Core.Storage;
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
        builder.Services.AddSingleton<DictationOrchestrator>();

        // Platform-bound services (hotkey, audio, STT/LLM engines, clipboard, UI)
        // are registered by the caller: the WinUI shell provides real
        // implementations, tests provide fakes.
        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }
}
