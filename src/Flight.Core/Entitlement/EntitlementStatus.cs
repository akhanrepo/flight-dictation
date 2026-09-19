namespace Flight.Core.Entitlement;

public enum EntitlementStatus
{
    /// <summary>No cloud dictation has happened yet; the trial clock hasn't started.</summary>
    TrialNotStarted,

    /// <summary>Within 30 days of the first cloud dictation. No account required.</summary>
    TrialActive,

    /// <summary>Past the trial window with no active paid subscription.</summary>
    TrialExpired,

    /// <summary>An active paid subscription, confirmed by Supabase or a valid cache.</summary>
    Paid,

    /// <summary>
    /// Supabase is unreachable and there is no cached entitlement still inside
    /// the offline grace period. Cloud fails closed; local is unaffected.
    /// </summary>
    Unverifiable,
}

public static class RemoteEntitlements
{
    public const string Paid = "paid";
    public const string None = "none";
}
