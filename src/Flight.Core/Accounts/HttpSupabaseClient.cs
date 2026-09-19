using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flight.Core.Entitlement;

namespace Flight.Core.Accounts;

/// <summary>
/// Supabase GoTrue auth plus two Edge Functions — one returning the account's
/// entitlement, one returning a Stripe hosted URL.
/// </summary>
/// <remarks>
/// Entitlement and billing live behind functions rather than direct table
/// reads so the Stripe secret key never leaves the backend, and so future
/// MeetingSession/DocumentTemplate endpoints can be added alongside them
/// without reworking this client.
/// </remarks>
public class HttpSupabaseClient : ISupabaseClient
{
    private readonly HttpClient _httpClient;
    private readonly FlightOptions _options;

    public HttpSupabaseClient(HttpClient httpClient, FlightOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public Task<Account> SignUpAsync(string email, string password, CancellationToken cancellationToken = default) =>
        AuthenticateAsync("auth/v1/signup", email, password, cancellationToken);

    public Task<Account> SignInAsync(string email, string password, CancellationToken cancellationToken = default) =>
        AuthenticateAsync("auth/v1/token?grant_type=password", email, password, cancellationToken);

    public async Task<RemoteEntitlement> GetEntitlementAsync(
        Account account,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, "functions/v1/entitlement", account, null, cancellationToken);
        var body = await ReadAsync<EntitlementResponse>(response, cancellationToken);
        return new RemoteEntitlement(body.Entitlement ?? RemoteEntitlements.None);
    }

    public Task<string> CreateCheckoutUrlAsync(Account account, CancellationToken cancellationToken = default) =>
        GetStripeUrlAsync("functions/v1/stripe-checkout", account, cancellationToken);

    public Task<string> CreatePortalUrlAsync(Account account, CancellationToken cancellationToken = default) =>
        GetStripeUrlAsync("functions/v1/stripe-portal", account, cancellationToken);

    private async Task<string> GetStripeUrlAsync(string path, Account account, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Post, path, account, new { }, cancellationToken);
        var body = await ReadAsync<StripeUrlResponse>(response, cancellationToken);

        return string.IsNullOrWhiteSpace(body.Url)
            ? throw new SupabaseException("The backend didn't return a Stripe URL.")
            : body.Url;
    }

    private async Task<Account> AuthenticateAsync(
        string path,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            HttpMethod.Post, path, account: null, new { email, password }, cancellationToken);
        var body = await ReadAsync<AuthResponse>(response, cancellationToken);

        if (body.User?.Id is not { Length: > 0 } id)
        {
            throw new SupabaseException("The backend didn't return an account.");
        }

        return new Account
        {
            Id = id,
            Email = body.User.Email ?? email,
            AccessToken = body.AccessToken,
        };
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        Account? account,
        object? body,
        CancellationToken cancellationToken)
    {
        if (_options.SupabaseUrl is null || string.IsNullOrWhiteSpace(_options.SupabaseAnonKey))
        {
            throw new SupabaseException("Flight isn't configured with a backend in this build.");
        }

        using var request = new HttpRequestMessage(method, new Uri(_options.SupabaseUrl, path));
        request.Headers.Add("apikey", _options.SupabaseAnonKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", account?.AccessToken ?? _options.SupabaseAnonKey);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            throw new SupabaseException($"Couldn't reach the Flight backend: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw new SupabaseException(
                $"The Flight backend returned {(int)response.StatusCode}. {detail.Trim()}".Trim());
        }

        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                    ?? throw new SupabaseException("The Flight backend returned an empty response.");
            }
            catch (Exception ex) when (ex is not SupabaseException and not OperationCanceledException)
            {
                throw new SupabaseException("The Flight backend returned a response Flight couldn't read.", ex);
            }
        }
    }

    private sealed class AuthResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("user")]
        public AuthUser? User { get; set; }
    }

    private sealed class AuthUser
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    private sealed class EntitlementResponse
    {
        [JsonPropertyName("entitlement")]
        public string? Entitlement { get; set; }
    }

    private sealed class StripeUrlResponse
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
