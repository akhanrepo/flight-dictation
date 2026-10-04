using System.Threading.Channels;
using Flight.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Flight.Core.Vocabulary;

/// <summary>
/// Unbounded channel drained by a single background worker. Enqueue is
/// non-blocking and never throws: a vocabulary failure must not cost the user
/// their dictation, which they have already confirmed and copied.
/// </summary>
public sealed class BackgroundVocabularyLearningQueue : IVocabularyLearningQueue, IAsyncDisposable
{
    private readonly Channel<DictationSession> _channel =
        Channel.CreateUnbounded<DictationSession>(new UnboundedChannelOptions { SingleReader = true });

    private readonly VocabularyLearner _learner;
    private readonly ILogger<BackgroundVocabularyLearningQueue> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private bool _disposed;

    /// <summary>Set whenever the queue is drained; tests await it instead of sleeping.</summary>
    private volatile TaskCompletionSource _idle =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BackgroundVocabularyLearningQueue(
        VocabularyLearner learner,
        ILogger<BackgroundVocabularyLearningQueue> logger)
    {
        _learner = learner;
        _logger = logger;
        _idle.TrySetResult();
        _worker = Task.Run(ProcessAsync);
    }

    public void Enqueue(DictationSession session)
    {
        if (_idle.Task.IsCompleted)
        {
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        if (!_channel.Writer.TryWrite(session))
        {
            _logger.LogWarning("Vocabulary learning queue is closed; dropping session {SessionId}", session.Id);
            _idle.TrySetResult();
        }
    }

    public Task WaitForIdleAsync() => _idle.Task;

    private async Task ProcessAsync()
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_shutdown.Token))
            {
                while (_channel.Reader.TryRead(out var session))
                {
                    try
                    {
                        await _learner.LearnAsync(session, _shutdown.Token);
                    }
                    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Learning is best-effort enrichment; the dictation is
                        // already safely on the clipboard and in the database.
                        _logger.LogError(ex, "Vocabulary extraction failed for session {SessionId}", session.Id);
                    }
                }

                _idle.TrySetResult();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            _idle.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // The generic host disposes singletons through both the sync and async
        // paths, so this has to tolerate being called more than once.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        await _shutdown.CancelAsync();

        try
        {
            await _worker;
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _shutdown.Dispose();
    }
}
