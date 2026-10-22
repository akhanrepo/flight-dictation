using Flight.Core.Dictation;
using Flight.Core.Vocabulary;
using Microsoft.Extensions.Logging;

namespace Flight.Core.Tray;

/// <summary>
/// The Quit sequence. Keeps the dictation invariants on the way out: a
/// dictation still processing is abandoned without touching the clipboard,
/// and finished dictations get a bounded chance to reach vocabulary learning.
/// </summary>
public sealed class ShutdownCoordinator
{
    private readonly DictationOrchestrator _orchestrator;
    private readonly IVocabularyLearningQueue _vocabulary;
    private readonly FlightOptions _options;
    private readonly ILogger<ShutdownCoordinator> _logger;

    public ShutdownCoordinator(
        DictationOrchestrator orchestrator,
        IVocabularyLearningQueue vocabulary,
        FlightOptions options,
        ILogger<ShutdownCoordinator> logger)
    {
        _orchestrator = orchestrator;
        _vocabulary = vocabulary;
        _options = options;
        _logger = logger;
    }

    public async Task ShutdownAsync()
    {
        // Unhooks the hotkey, stops any capture in progress, and cancels any
        // processing so its result is never copied after Quit.
        _orchestrator.Dispose();

        var idle = _vocabulary.WaitForIdleAsync();
        if (await Task.WhenAny(idle, Task.Delay(_options.ShutdownVocabularyGrace)) != idle)
        {
            _logger.LogWarning("Vocabulary learning didn't finish within {Grace}; exiting anyway",
                _options.ShutdownVocabularyGrace);
        }
    }
}
