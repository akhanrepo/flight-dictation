using Flight.Core.Storage;

namespace Flight.Core.Vocabulary;

/// <summary>
/// Hands a confirmed dictation off for vocabulary extraction without blocking
/// the caller. The dictation loop calls this <em>after</em> the clipboard
/// write, so learning latency is never in front of the user's paste.
/// </summary>
public interface IVocabularyLearningQueue
{
    void Enqueue(DictationSession session);
}
