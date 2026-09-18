namespace Flight.Core.Storage;

public interface IDictationSessionRepository
{
    Task<DictationSession> AddAsync(DictationSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DictationSession>> GetAllAsync(CancellationToken cancellationToken = default);
}
