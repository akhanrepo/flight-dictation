using Flight.Core.Accounts;
using Flight.Core.Billing;
using Flight.Core.Security;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Accounts;

/// <summary>
/// Supabase access tokens expire after an hour. Without refresh, entitlement
/// sync silently fails and paying users are locked out once the offline grace
/// runs out, so these cover the renew-or-sign-out path.
/// </summary>
public class SessionRefreshTests
{
    private readonly FakeSupabaseClient _supabase = new();
    private readonly InMemoryDeviceStore _devices = new();
    private readonly InMemorySecretStore _secrets = new();

    private AccountSession CreateSession() => new(_supabase, _devices, _secrets);

    [Fact]
    public async Task SignIn_Stores_The_Refresh_Token_As_A_Secret()
    {
        await CreateSession().SignInAsync("ali@example.com", "password");

        Assert.Equal("refresh", _secrets.Get(SecretNames.AccountRefreshToken));
    }

    [Fact]
    public async Task Restart_Restores_The_Refresh_Token()
    {
        await CreateSession().SignInAsync("ali@example.com", "password");

        var restored = CreateSession();

        Assert.Equal("refresh", restored.Current?.RefreshToken);
    }

    [Fact]
    public async Task ExpiredAccessToken_Is_Refreshed_And_The_Call_Retried()
    {
        var session = CreateSession();
        await session.SignInAsync("ali@example.com", "password");
        _supabase.ValidAccessToken = "token-2";

        var entitlement = await session.CallAsync(_supabase.GetEntitlementAsync);

        Assert.False(entitlement.IsPaid);
        Assert.Equal(1, _supabase.RefreshCallCount);
        Assert.Equal(2, _supabase.EntitlementCallCount);
        Assert.Equal("token-2", session.Current?.AccessToken);
    }

    [Fact]
    public async Task Refresh_Persists_The_Rotated_Tokens()
    {
        var session = CreateSession();
        await session.SignInAsync("ali@example.com", "password");
        _supabase.ValidAccessToken = "token-2";

        await session.CallAsync(_supabase.GetEntitlementAsync);

        Assert.Equal("token-2", _secrets.Get(SecretNames.AccountAccessToken));
        Assert.Equal("refresh-rotated", _secrets.Get(SecretNames.AccountRefreshToken));
    }

    [Fact]
    public async Task ValidAccessToken_Does_Not_Refresh()
    {
        var session = CreateSession();
        await session.SignInAsync("ali@example.com", "password");

        await session.CallAsync(_supabase.GetEntitlementAsync);

        Assert.Equal(0, _supabase.RefreshCallCount);
    }

    [Fact]
    public async Task RejectedRefresh_Signs_Out_And_Throws_SessionExpired()
    {
        var session = CreateSession();
        await session.SignInAsync("ali@example.com", "password");
        _supabase.ValidAccessToken = "token-2";
        _supabase.RefreshException = new SupabaseException("Invalid Refresh Token", statusCode: 400);

        await Assert.ThrowsAsync<SessionExpiredException>(() => session.CallAsync(_supabase.GetEntitlementAsync));

        Assert.Null(session.Current);
        Assert.Null(_devices.Current.AccountId);
        Assert.Null(_secrets.Get(SecretNames.AccountRefreshToken));
    }

    // An unreachable backend during refresh is "offline", not "signed out":
    // the user keeps their account and the entitlement cache's grace applies.
    [Fact]
    public async Task UnreachableRefresh_Keeps_The_Account_Signed_In()
    {
        var session = CreateSession();
        await session.SignInAsync("ali@example.com", "password");
        _supabase.ValidAccessToken = "token-2";
        _supabase.RefreshException = new SupabaseException("Couldn't reach the Flight backend");

        var ex = await Assert.ThrowsAsync<SupabaseException>(() => session.CallAsync(_supabase.GetEntitlementAsync));

        Assert.IsNotType<SessionExpiredException>(ex);
        Assert.NotNull(session.Current);
    }

    [Fact]
    public async Task Checkout_Refreshes_An_Expired_Token()
    {
        var session = CreateSession();
        await session.SignInAsync("ali@example.com", "password");
        _supabase.ValidAccessToken = "token-2";
        var launcher = new FakeUrlLauncher();

        await new StripeBillingPortal(_supabase, session, launcher).StartCheckoutAsync();

        Assert.Equal("https://checkout.stripe.com/test-session", Assert.Single(launcher.Opened));
    }
}
