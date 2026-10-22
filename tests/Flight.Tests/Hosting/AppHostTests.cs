using Flight.Core.Audio;
using Flight.Core.Clipboard;
using Flight.Core.Dictation;
using Flight.Core.Hosting;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Stt;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flight.Tests.Hosting;

public class AppHostTests
{
    [Fact]
    public void AppHost_Builds_And_Resolves_Core_Services()
    {
        using var host = AppHost.Build();

        // The host boots and its DI container serves fundamental services —
        // the same container the WinUI shell will pull feature services from.
        Assert.NotNull(host.Services.GetRequiredService<ILoggerFactory>());
    }

    [Fact]
    public void AppHost_Resolves_Dictation_Pipeline_Services()
    {
        // The shell registers platform implementations the same way the fakes
        // are registered here; the orchestrator and storage come from AppHost.
        using var host = AppHost.Build(services =>
        {
            // Keep test data out of the real %LOCALAPPDATA%\Flight
            services.AddSingleton(new Flight.Core.FlightOptions
            {
                DataDirectory = Path.Combine(Path.GetTempPath(), "flight-tests-" + Guid.NewGuid().ToString("N")),
            });
            services.AddSingleton<IPushToTalkHotkey, FakeHotkey>();
            services.AddSingleton<IAudioCapturer, FakeAudioCapturer>();
            services.AddSingleton<ISpeechToText, FakeSpeechToText>();
            services.AddSingleton(new LocalLlmEngine(new FakeLlmCompletion("local")));
            services.AddSingleton<Flight.Core.Security.ISecretStore>(new InMemorySecretStore());
            services.AddSingleton<IClipboardWriter, FakeClipboardWriter>();
            services.AddSingleton<IDictationUi, FakeDictationUi>();
        });

        Assert.NotNull(host.Services.GetRequiredService<DictationOrchestrator>());
    }

    [Fact]
    public void AppHost_BindsSettingsFromConfiguration()
    {
        // The shell ships appsettings.json (and Flight__* environment variables
        // override it); without binding, the update feed would be ignored.
        using var host = AppHost.Build(configureConfiguration: config => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Flight:UpdateFeed"] = @"C:/releases",
                ["Flight:CloudLlmModel"] = "deepseek-chat",
            }));

        var options = host.Services.GetRequiredService<Flight.Core.FlightOptions>();

        Assert.Equal(@"C:/releases", options.UpdateFeed);
        Assert.Equal("deepseek-chat", options.CloudLlmModel);
    }

    // A malformed appsettings.json crashes the shell before any window opens,
    // so the file that actually ships is checked here.
    [Fact]
    public void AppHost_BindsTheShippedAppSettings()
    {
        var appSettings = RepoPaths.Combine("src", "Flight.App", "appsettings.json");

        using var host = AppHost.Build(configureConfiguration: config => config.AddJsonFile(appSettings));
        var options = host.Services.GetRequiredService<Flight.Core.FlightOptions>();

        // The public build updates from GitHub Releases, never a personal path.
        Assert.Equal("https://github.com/akhanrepo/flight-dictation", options.UpdateFeed);
    }

    [Fact]
    public void AppHost_UsesTheToastPresenterAsTheDictationUi()
    {
        using var host = AppHost.Build();

        Assert.Same(
            host.Services.GetRequiredService<Flight.Core.Toast.ToastPresenter>(),
            host.Services.GetRequiredService<IDictationUi>());
    }

    [Fact]
    public void AppHost_KeepsOptionDefaultsWhenConfigurationIsAbsent()
    {
        using var host = AppHost.Build();

        var options = host.Services.GetRequiredService<Flight.Core.FlightOptions>();

        Assert.Equal("ggml-base-q5_1.bin", options.SttModel.FileName);
        Assert.Null(options.UpdateFeed);
    }
}
