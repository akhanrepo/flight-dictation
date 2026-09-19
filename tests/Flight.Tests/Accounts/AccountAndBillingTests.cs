using Flight.Core.Accounts;
using Flight.Core.Entitlement;
using Flight.Core.Billing;
using Flight.Core.Security;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Accounts;

/// <summary>
/// Feature #5 acceptance criteria: account creation, the device link, and the
/// route to Stripe's hosted pages.
/// </summary>
public class AccountAndBillingTests
{
    private readonly FakeSupabaseClient _supabase = new();
    private readonly InMemoryDeviceStore _devices = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeUrlLauncher _launcher = new();

    private AccountSession CreateSession() => new(_supabase, _devices, _secrets);

    // Criterion: the user can create an account via Supabase auth
    [Fact]
    public async Task SignUp_Creates_An_Account()
    {
        var account = await CreateSession().SignUpAsync("ali@example.com", "password");

        Assert.Equal("ali@example.com", account.Email);
        Assert.NotEmpty(account.Id);
    }

    // Criterion: the app associates the install with that account via a Device row
    [Fact]
    public async Task SignUp_Links_The_Device_To_The_Account()
    {
        var session = CreateSession();
        var deviceIdBefore = _devices.Current.Id;

        var account = await session.SignUpAsync("ali@example.com", "password");

        Assert.Equal(account.Id, _devices.Current.AccountId);

        // The device keeps its identity across the link, so the entitlement
        // cache attached to it survives.
        Assert.Equal(deviceIdBefore, _devices.Current.Id);
    }

    // The access token is a credential: it belongs in the secret store, never
    // in the database
    [Fact]
    public async Task AccessToken_Goes_To_The_Secret_Store()
    {
        await CreateSession().SignInAsync("ali@example.com", "password");

        Assert.Equal("token", _secrets.Get(SecretNames.AccountAccessToken));
    }

    // A restart restores the signed-in account rather than forcing a re-login
    [Fact]
    public async Task SignedInAccount_Survives_A_Restart()
    {
        await CreateSession().SignInAsync("ali@example.com", "password");

        // A second session instance stands in for a fresh app launch.
        var reopened = CreateSession();

        Assert.NotNull(reopened.Current);
        Assert.Equal("ali@example.com", reopened.Current.Email);
    }

    // Signing out must not leave the previous account's entitlement behind for
    // the next one to inherit
    [Fact]
    public async Task SignOut_Clears_The_Account_And_Its_Cached_Entitlement()
    {
        var session = CreateSession();
        await session.SignInAsync("ali@example.com", "password");
        await _devices.UpdateAsync(_devices.Current with
        {
            CachedEntitlement = RemoteEntitlements.Paid,
            LastSyncedAt = DateTimeOffset.UtcNow,
        });

        await session.SignOutAsync();

        Assert.Null(session.Current);
        Assert.Null(_devices.Current.AccountId);
        Assert.Null(_devices.Current.CachedEntitlement);
        Assert.Null(_devices.Current.LastSyncedAt);
    }

    // Criterion: the user can reach Stripe's hosted checkout from the upgrade
    // prompt
    [Fact]
    public async Task Checkout_Opens_Stripes_Hosted_Page()
    {
        var account = new FakeAccountSession(new Account { Id = "account-1", Email = "ali@example.com" });
        var billing = new StripeBillingPortal(_supabase, account, _launcher);

        await billing.StartCheckoutAsync();

        Assert.Equal("https://checkout.stripe.com/test-session", Assert.Single(_launcher.Opened));
    }

    // Criterion: cancelling happens in Stripe's hosted portal, which the app
    // opens rather than reimplementing
    [Fact]
    public async Task Portal_Opens_Stripes_Hosted_Portal()
    {
        var account = new FakeAccountSession(new Account { Id = "account-1", Email = "ali@example.com" });
        var billing = new StripeBillingPortal(_supabase, account, _launcher);

        await billing.OpenPortalAsync();

        Assert.Equal("https://billing.stripe.com/test-portal", Assert.Single(_launcher.Opened));
    }

    [Fact]
    public async Task Checkout_Without_An_Account_Fails_Clearly()
    {
        var billing = new StripeBillingPortal(_supabase, new FakeAccountSession(), _launcher);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => billing.StartCheckoutAsync());

        Assert.Contains("Sign in", ex.Message);
        Assert.Empty(_launcher.Opened);
    }

    // A backend failure must not open a half-built URL in the browser
    [Fact]
    public async Task BillingFailure_Does_Not_Open_A_Browser()
    {
        _supabase.ExceptionToThrow = new SupabaseException("backend down");
        var account = new FakeAccountSession(new Account { Id = "account-1", Email = "ali@example.com" });
        var billing = new StripeBillingPortal(_supabase, account, _launcher);

        await Assert.ThrowsAsync<SupabaseException>(() => billing.StartCheckoutAsync());

        Assert.Empty(_launcher.Opened);
    }
}
