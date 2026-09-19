namespace Flight.Core.Entitlement;

/// <summary>
/// Decides whether cloud mode is available. Never consulted for local-mode
/// dictation: local is the free tier and needs no verification at all.
/// </summary>
public interface IEntitlementService
{
    /// <summary>
    /// Evaluates cloud availability, refreshing the cached entitlement when it
    /// is stale and the network allows. Must be called before any cloud API
    /// call, never after.
    /// </summary>
    Task<EntitlementDecision> EvaluateAsync(CancellationToken cancellationToken = default);
}
