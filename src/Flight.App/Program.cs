using Flight.App.Platform;
using Flight.Core.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Flight.App;

/// <summary>
/// Replaces the XAML-generated entry point so Velopack can handle its
/// install/update/uninstall hooks (which exit immediately) before any WinUI
/// window exists.
/// </summary>
public static class Program
{
    private const int AsfwAny = -1;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>This copy's single-instance gate; App listens on it to come forward.</summary>
    public static SingleInstanceGate? Instance { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => RegistryStartupRegistration.RemoveEntry())
            .Run();

        // One copy per user: a second would install a second keyboard hook.
        using var instance = new SingleInstanceGate();
        if (!instance.TryAcquire())
        {
            // This launch came from the user, so it may hand foreground
            // rights on; otherwise Windows keeps the running copy's Settings
            // window behind whatever is in front.
            AllowSetForegroundWindow(AsfwAny);
            instance.SignalExisting();
            return;
        }
        Instance = instance;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
