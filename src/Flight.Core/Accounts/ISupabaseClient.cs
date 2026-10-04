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

    /// <summary>Exchanges a refresh token for a new access token.</summary>
    Task<Account> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

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
    public SupabaseException(string message, Exception? innerException = null, int? statusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status from the backend; null when it couldn't be reached at all.</summary>
    public int? StatusCode { get; }

    /// <summary>The access token was rejected, usually because it expired.</summary>
    public bool IsUnauthorized => StatusCode == 401;
}

/// <summary>
/// Signup succeeded but the project requires email confirmation, so there is
/// no session yet. The user confirms from their inbox, then signs in.
/// </summary>
public class EmailConfirmationRequiredException : SupabaseException
{
    public EmailConfirmationRequiredException()
        : base("Check your email to confirm your account, then sign in.")
    {
    }
}

/// <summary>
/// The refresh token was rejected, so the session can't be renewed. The
/// account has been signed out on this device.
/// </summary>
public class SessionExpiredException : SupabaseException
{
    public SessionExpiredException(Exception? innerException = null)
        : base("Your sign-in has expired. Sign in again to keep using your subscription.", innerException)
    {
    }
}
