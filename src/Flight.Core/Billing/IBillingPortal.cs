namespace Flight.Core.Billing;

/// <summary>
/// Stripe's hosted checkout and customer portal. Flight never renders a
/// payment form or handles card data — it opens Stripe's own pages.
/// </summary>
public interface IBillingPortal
{
    /// <summary>Opens Stripe hosted checkout for the signed-in account.</summary>
    Task StartCheckoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens Stripe's hosted portal, where the user manages or cancels.</summary>
    Task OpenPortalAsync(CancellationToken cancellationToken = default);
}

/// <summary>Opens a URL in the user's browser. Implemented by the shell.</summary>
public interface IUrlLauncher
{
    void Open(string url);
}
