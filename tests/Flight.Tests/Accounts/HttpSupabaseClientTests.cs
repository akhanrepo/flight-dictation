using System.Net;
using System.Text;
using Flight.Core;
using Flight.Core.Accounts;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Accounts;

/// <summary>
/// The wire contract with Supabase GoTrue and the Edge Functions in
/// <c>supabase/functions</c>.
/// </summary>
public class HttpSupabaseClientTests
{
    private const string SessionJson =
        """{"access_token":"jwt","refresh_token":"rt","user":{"id":"user-1","email":"ali@example.com"}}""";

    private static readonly FlightOptions Options = new()
    {
        SupabaseUrl = new Uri("https://project.supabase.co/"),
        SupabaseAnonKey = "sb_publishable_test",
    };

    private static (HttpSupabaseClient Client, StubHttpMessageHandler Handler) Create(
        HttpStatusCode status, string json)
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));
        return (new HttpSupabaseClient(new HttpClient(handler), Options), handler);
    }

    [Fact]
    public async Task SignUp_With_A_Session_Returns_Both_Tokens()
    {
        var (client, _) = Create(HttpStatusCode.OK, SessionJson);

        var account = await client.SignUpAsync("ali@example.com", "password");

        Assert.Equal("user-1", account.Id);
        Assert.Equal("jwt", account.AccessToken);
        Assert.Equal("rt", account.RefreshToken);
    }

    // With "Confirm email" on (Supabase's default) signup returns the bare
    // user and no session; treating that as signed in would 401 on every call.
    [Fact]
    public async Task SignUp_Without_A_Session_Requires_Email_Confirmation()
    {
        var (client, _) = Create(HttpStatusCode.OK,
            """{"id":"user-1","email":"ali@example.com","confirmation_sent_at":"2026-10-04T00:00:00Z"}""");

        var ex = await Assert.ThrowsAsync<EmailConfirmationRequiredException>(
            () => client.SignUpAsync("ali@example.com", "password"));

        Assert.Contains("confirm", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_Posts_The_Refresh_Token_Grant()
    {
        var (client, handler) = Create(HttpStatusCode.OK, SessionJson);

        var account = await client.RefreshAsync("old-rt");

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("auth/v1/token?grant_type=refresh_token", request.RequestUri!.ToString());
        Assert.Contains("\"refresh_token\":\"old-rt\"", handler.LastRequestBody);
        Assert.Equal("rt", account.RefreshToken);
    }

    [Fact]
    public async Task Unauthorized_Response_Is_Flagged_For_Refresh()
    {
        var (client, _) = Create(HttpStatusCode.Unauthorized, """{"msg":"JWT expired"}""");
        var account = new Account { Id = "user-1", Email = "ali@example.com", AccessToken = "old" };

        var ex = await Assert.ThrowsAsync<SupabaseException>(() => client.GetEntitlementAsync(account));

        Assert.True(ex.IsUnauthorized);
    }

    [Fact]
    public async Task Entitlement_Sends_The_Users_Jwt_And_Reads_The_Answer()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"entitlement":"paid"}""");
        var account = new Account { Id = "user-1", Email = "ali@example.com", AccessToken = "jwt" };

        var entitlement = await client.GetEntitlementAsync(account);

        Assert.True(entitlement.IsPaid);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer jwt", request.Headers.Authorization?.ToString());
        Assert.Equal("sb_publishable_test", request.Headers.GetValues("apikey").Single());
    }
}
