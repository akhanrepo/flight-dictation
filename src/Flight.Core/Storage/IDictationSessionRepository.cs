namespace Flight.Core.Storage;

public interface IDictationSessionRepository
{
    Task<DictationSession> AddAsync(DictationSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DictationSession>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creation time of the earliest cloud-mode dictation, or null if there has
    /// never been one. This is the trial-start anchor: history that predates
    /// feature #5 still counts, so reinstalling can't reset the trial.
    /// </summary>
    Task<DateTimeOffset?> GetEarliestCloudDictationAsync(CancellationToken cancellationToken = default);
}
