using Flight.Core.Entitlement;

namespace Flight.Core.Accounts;

/// <param name="Entitlement">
/// <see cref="RemoteEntitlements.Paid"/> or <see cref="RemoteEntitlements.None"/>.
/// A cancelled subscription reads as <c>none</c> on the next sync.
/// </param>
public record RemoteEntitlement(string Entitlement)
{
    public bool IsPaid => string.Equals(Entitlement, RemoteEntitlements.Paid, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Backend seam: Supabase auth plus the entitlement/billing endpoints layered
/// on it. Kept generic enough to host future MeetingSession/DocumentTemplate
/// metadata without a rework, per the PRD's architecture provision.
/// </summary>
public interface ISupabaseClient
{
    Task<Account> SignUpAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<Account> SignInAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Current subscription state for the account. Throws when unreachable.</summary>
    Task<RemoteEntitlement> GetEntitlementAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>A Stripe hosted-checkout URL for this account.</summary>
    Task<string> CreateCheckoutUrlAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>A Stripe hosted-portal URL, where the user manages or cancels.</summary>
    Task<string> CreatePortalUrlAsync(Account account, CancellationToken cancellationToken = default);
}

/// <summary>The backend couldn't be reached or refused the request.</summary>
public class SupabaseException : Exception
{
    public SupabaseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
