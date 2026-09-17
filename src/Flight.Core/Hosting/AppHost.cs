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

        // Feature services are registered here as they are built.
        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }
}
