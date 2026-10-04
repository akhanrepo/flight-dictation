namespace Flight.Core.Accounts;

/// <summary>
/// The PRD's <c>Account</c> entity, as this device knows it. Subscription
/// state is deliberately not stored here — it lives in the device's
/// entitlement cache, which has a freshness timestamp attached.
/// </summary>
public record Account
{
    public required string Id { get; init; }
    public required string Email { get; init; }

    /// <summary>Supabase access token for authenticated calls. Never persisted in plaintext.</summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// Exchanged for a new access token when the current one expires (Supabase
    /// issues hour-long JWTs). Never persisted in plaintext.
    /// </summary>
    public string? RefreshToken { get; init; }
}

/// <summary>
/// The PRD's <c>Device</c> entity: this install, its account link, and the
/// cached entitlement that lets cloud gating work offline.
/// </summary>
public record Device
{
    public required string Id { get; init; }
    public string? AccountId { get; init; }

    /// <summary>Last entitlement seen from Supabase; null before any sync.</summary>
    public string? CachedEntitlement { get; init; }

    /// <summary>When <see cref="CachedEntitlement"/> was last confirmed against Supabase.</summary>
    public DateTimeOffset? LastSyncedAt { get; init; }
}
