using Flight.Core.Accounts;

namespace Flight.Core.Billing;

/// <summary>
/// Gets hosted Stripe URLs from Supabase (which holds the Stripe secret key)
/// and hands them to the browser.
/// </summary>
public class StripeBillingPortal : IBillingPortal
{
    private readonly ISupabaseClient _supabase;
    private readonly IAccountSession _account;
    private readonly IUrlLauncher _launcher;

    public StripeBillingPortal(ISupabaseClient supabase, IAccountSession account, IUrlLauncher launcher)
    {
        _supabase = supabase;
        _account = account;
        _launcher = launcher;
    }

    public Task StartCheckoutAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(_supabase.CreateCheckoutUrlAsync, cancellationToken);

    public Task OpenPortalAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(_supabase.CreatePortalUrlAsync, cancellationToken);

    private async Task OpenAsync(
        Func<Account, CancellationToken, Task<string>> getUrl,
        CancellationToken cancellationToken)
    {
        if (_account.Current is null)
        {
            throw new InvalidOperationException("Sign in before opening Stripe checkout or the billing portal.");
        }

        _launcher.Open(await _account.CallAsync(getUrl, cancellationToken));
    }
}
