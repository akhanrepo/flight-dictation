using System.Runtime.Versioning;

namespace Flight.Core.Hosting;

/// <summary>
/// Keeps Flight to one running copy per user session — two copies would mean
/// two keyboard hooks, and every dictation would trigger twice. A second
/// launch signals the first (which opens Settings) and exits.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _registration;
    private bool _owned;

    public SingleInstanceGate(string name = "Flight")
    {
        // Local\ = per user session, so two signed-in users each get their own copy.
        _mutex = new Mutex(initiallyOwned: false, $@"Local\{name}.Instance");
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
    }

    /// <summary>Raised on a thread-pool thread when another launch asks this copy to come forward.</summary>
    public event Action? ActivationRequested;

    /// <returns>True when this is the first copy; it then listens for later launches.</returns>
    public bool TryAcquire()
    {
        try
        {
            _owned = _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed; the mutex is ours now.
            _owned = true;
        }

        if (_owned)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _activate, (_, _) => ActivationRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        return _owned;
    }

    /// <summary>Asks the running copy to come forward.</summary>
    public void SignalExisting() => _activate.Set();

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Disposed from a thread other than the owner. Windows releases
                // the mutex when the owning thread or process ends anyway.
            }
        }
        _mutex.Dispose();
        _activate.Dispose();
    }
}
