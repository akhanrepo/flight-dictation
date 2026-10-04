using Flight.Core.Hosting;
using Xunit;

namespace Flight.Tests.Hosting;

/// <summary>
/// Feature #6: one running copy. Named wait handles are Windows-only, which
/// is the only platform Flight ships on; elsewhere these return early.
/// </summary>
public class SingleInstanceGateTests
{
    private readonly string _name = "FlightTest-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void SingleInstance_SecondCopyIsRefused()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var first = new SingleInstanceGate(_name);
        Assert.True(first.TryAcquire());

        // A mutex is re-entrant per thread, so the second copy must ask from
        // another thread, as a second process would.
        var secondAcquired = Task.Run(() =>
        {
            using var second = new SingleInstanceGate(_name);
            return second.TryAcquire();
        }).Result;

        Assert.False(secondAcquired);
    }

    [Fact]
    public async Task SingleInstance_SecondLaunchSignalsTheFirst()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var first = new SingleInstanceGate(_name);
        first.TryAcquire();
        var activated = new TaskCompletionSource();
        first.ActivationRequested += () => activated.TrySetResult();

        using (var second = new SingleInstanceGate(_name))
        {
            second.SignalExisting();
        }

        Assert.Same(activated.Task, await Task.WhenAny(activated.Task, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public void SingleInstance_IsReleasedWhenTheFirstCopyExits()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Task.Run(() =>
        {
            using var first = new SingleInstanceGate(_name);
            Assert.True(first.TryAcquire());
        }).Wait();

        using var next = new SingleInstanceGate(_name);
        Assert.True(next.TryAcquire());
    }
}
