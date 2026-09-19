using Flight.Core.Audio;
using Flight.Core.Clipboard;
using Flight.Core.Dictation;
using Flight.Core.Hosting;
using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Stt;
using Flight.Tests.Fakes;
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
}
