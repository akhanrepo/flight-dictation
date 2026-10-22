namespace Flight.Core.Dictation;

/// <summary>
/// Whether a dictation is capturing or processing right now. Background work
/// that competes for the CPU (vocabulary extraction runs the LLM) waits for
/// <see cref="WaitForIdleAsync"/> so it never slows down the dictation the
/// user is waiting on.
/// </summary>
/// <remarks>
/// A separate object rather than an orchestrator event, because the
/// orchestrator already depends on the learning queue that needs to wait.
/// </remarks>
public sealed class DictationActivity
{
    private readonly object _lock = new();
    private TaskCompletionSource _idle = Completed();

    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return !_idle.Task.IsCompleted;
            }
        }
    }

    public void SetBusy(bool busy)
    {
        lock (_lock)
        {
            if (busy && _idle.Task.IsCompleted)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            else if (!busy)
            {
                _idle.TrySetResult();
            }
        }
    }

    public Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        Task idle;
        lock (_lock)
        {
            idle = _idle.Task;
        }

        return idle.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
