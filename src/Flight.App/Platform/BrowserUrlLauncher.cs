using System.Diagnostics;
using Flight.Core.Billing;

namespace Flight.App.Platform;

/// <summary>Opens Stripe's hosted pages in the user's default browser.</summary>
public sealed class BrowserUrlLauncher : IUrlLauncher
{
    public void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
