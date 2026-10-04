using Flight.Core.Accounts;
using Flight.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Flight.Core.Entitlement;

/// <summary>
/// Cloud entitlement: free for 30 days from the first cloud dictation, then a
/// paid subscription. Local mode is free forever and never reaches this class.
/// </summary>
/// <remarks>
/// Evaluation order matters. The trial window is computed entirely from local
/// <c>dictation_sessions</c> rows, so a trial user is never asked for an
/// account and never waits on the network. Only once the trial has expired
/// does entitlement involve Supabase — and then a cached answer is preferred
/// while it's fresh, with a grace period for being offline, and a fail-closed
/// outcome past it.
/// </remarks>
public class EntitlementService : IEntitlementService
{
    private readonly IDictationSessionRepository _sessions;
    private readonly IDeviceStore _devices;
    private readonly IAccountSession _account;
    private readonly ISupabaseClient _supabase;
    private readonly FlightOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<EntitlementService> _logger;

    public EntitlementService(
        IDictationSessionRepository sessions,
        IDeviceStore devices,
        IAccountSession account,
        ISupabaseClient supabase,
        FlightOptions options,
        TimeProvider time,
        ILogger<EntitlementService> logger)
    {
        _sessions = sessions;
        _devices = devices;
        _account = account;
        _supabase = supabase;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async Task<EntitlementDecision> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        // 1. The trial is a purely local computation: no account, no network.
        var trialStart = await _sessions.GetEarliestCloudDictationAsync(cancellationToken);
        if (trialStart is null)
        {
            return new EntitlementDecision(true, EntitlementStatus.TrialNotStarted);
        }

        var trialEndsAt = trialStart.Value + _options.CloudTrialDuration;
        if (now < trialEndsAt)
        {
            return new EntitlementDecision(true, EntitlementStatus.TrialActive, TrialEndsAt: trialEndsAt);
        }

        // 2. Trial over. Paying is the way forward, so entitlement now matters.
        var account = _account.Current;
        if (account is null)
        {
            return new EntitlementDecision(
                false,
                EntitlementStatus.TrialExpired,
                "Your 30-day cloud trial has ended. Create an account and subscribe to keep using cloud " +
                "cleanup — local mode stays free and unlimited.",
                trialEndsAt,
                OffersUpgrade: true);
        }

        return await EvaluateSubscriptionAsync(account, now, trialEndsAt, cancellationToken);
    }

    private async Task<EntitlementDecision> EvaluateSubscriptionAsync(
        Account account,
        DateTimeOffset now,
        DateTimeOffset trialEndsAt,
        CancellationToken cancellationToken)
    {
        var device = _devices.Current;
        var age = device.LastSyncedAt is { } synced ? now - synced : (TimeSpan?)null;
        var cacheIsFresh = age is { } value && value < _options.EntitlementRefreshInterval;

        // A fresh cache answers without touching the network.
        if (cacheIsFresh)
        {
            return FromCache(device, trialEndsAt);
        }

        try
        {
            var remote = await _account.CallAsync(_supabase.GetEntitlementAsync, cancellationToken);
            await _devices.UpdateAsync(device with
            {
                AccountId = account.Id,
                CachedEntitlement = remote.Entitlement,
                LastSyncedAt = now,
            }, cancellationToken);

            // A cancellation made in Stripe's portal lands here: the remote
            // entitlement reads 'none' and cloud access reverts to blocked.
            return remote.IsPaid
                ? new EntitlementDecision(true, EntitlementStatus.Paid)
                : Blocked(trialEndsAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SessionExpiredException ex)
        {
            // Not "offline": the backend answered and the session is gone, and
            // the account has already been signed out (which clears the cache
            // too). The way back is signing in again, not waiting.
            return new EntitlementDecision(false, EntitlementStatus.TrialExpired, ex.Message, trialEndsAt,
                OffersUpgrade: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entitlement sync failed; falling back to the cached entitlement");
        }

        // Offline. A stale cache still counts inside the grace period.
        if (age is { } staleness && staleness < _options.EntitlementOfflineGrace)
        {
            return FromCache(device, trialEndsAt);
        }

        // No usable cache and no backend. Cloud fails closed — deliberately not
        // the usual "fail open to the free tier", because local *is* the free
        // tier and needs no verification.
        return new EntitlementDecision(
            false,
            EntitlementStatus.Unverifiable,
            "Flight can't verify your subscription right now. Cloud cleanup is paused until it can — " +
            "local mode still works normally.",
            trialEndsAt);
    }

    private static EntitlementDecision FromCache(Device device, DateTimeOffset trialEndsAt) =>
        string.Equals(device.CachedEntitlement, RemoteEntitlements.Paid, StringComparison.OrdinalIgnoreCase)
            ? new EntitlementDecision(true, EntitlementStatus.Paid)
            : Blocked(trialEndsAt);

    private static EntitlementDecision Blocked(DateTimeOffset trialEndsAt) => new(
        false,
        EntitlementStatus.TrialExpired,
        "Your 30-day cloud trial has ended and there's no active subscription on this account. " +
        "Subscribe to keep using cloud cleanup — local mode stays free and unlimited.",
        trialEndsAt,
        OffersUpgrade: true);
}
