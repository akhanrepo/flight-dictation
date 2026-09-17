using Flight.Core.Hosting;
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
}
