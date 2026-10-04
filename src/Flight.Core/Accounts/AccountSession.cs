using Flight.Core.Security;

namespace Flight.Core.Accounts;

/// <summary>
/// Signs in against Supabase and links this install's <see cref="Device"/> row
/// to the resulting account. The access token goes to <see cref="ISecretStore"/>
/// (encrypted at rest), never to the database.
/// </summary>
public class AccountSession : IAccountSession
{
    private readonly ISupabaseClient _supabase;
    private readonly IDeviceStore _devices;
    private readonly ISecretStore _secrets;

    public AccountSession(ISupabaseClient supabase, IDeviceStore devices, ISecretStore secrets)
    {
        _supabase = supabase;
        _devices = devices;
        _secrets = secrets;

        // Restore across restarts: the device row remembers the account id and
        // the secret store holds the token.
        var device = _devices.Current;
        if (device.AccountId is { } accountId)
        {
            var email = _secrets.Get(SecretNames.AccountEmail);
            if (email is not null)
            {
                Current = new Account
                {
                    Id = accountId,
                    Email = email,
                    AccessToken = _secrets.Get(SecretNames.AccountAccessToken),
                    RefreshToken = _secrets.Get(SecretNames.AccountRefreshToken),
                };
            }
        }
    }

    public Account? Current { get; private set; }

    public async Task<Account> SignUpAsync(string email, string password, CancellationToken cancellationToken = default) =>
        await AdoptAsync(await _supabase.SignUpAsync(email, password, cancellationToken), cancellationToken);

    public async Task<Account> SignInAsync(string email, string password, CancellationToken cancellationToken = default) =>
        await AdoptAsync(await _supabase.SignInAsync(email, password, cancellationToken), cancellationToken);

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        Current = null;
        _secrets.Remove(SecretNames.AccountAccessToken);
        _secrets.Remove(SecretNames.AccountRefreshToken);
        _secrets.Remove(SecretNames.AccountEmail);

        // Clear the cached entitlement too: it belonged to the account that
        // just left, and leaving it would grant cloud access to the next one.
        await _devices.UpdateAsync(_devices.Current with
        {
            AccountId = null,
            CachedEntitlement = null,
            LastSyncedAt = null,
        }, cancellationToken);
    }

    public async Task<T> CallAsync<T>(
        Func<Account, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken = default)
    {
        var account = Current ?? throw new InvalidOperationException("Sign in first.");
        try
        {
            return await call(account, cancellationToken);
        }
        catch (SupabaseException ex) when (ex.IsUnauthorized)
        {
            return await call(await RefreshAsync(account, cancellationToken), cancellationToken);
        }
    }

    private async Task<Account> RefreshAsync(Account account, CancellationToken cancellationToken)
    {
        Account refreshed;
        try
        {
            refreshed = account.RefreshToken is { } refreshToken
                ? await _supabase.RefreshAsync(refreshToken, cancellationToken)
                : throw new SupabaseException("No refresh token is stored.", statusCode: 401);
        }
        catch (SupabaseException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            // The backend answered and said no: the session is gone for good.
            // An unreachable backend (no status) propagates as offline.
            await SignOutAsync(cancellationToken);
            throw new SessionExpiredException(ex);
        }

        return await AdoptAsync(refreshed, cancellationToken);
    }

    private async Task<Account> AdoptAsync(Account account, CancellationToken cancellationToken)
    {
        Current = account;
        _secrets.Set(SecretNames.AccountEmail, account.Email);
        if (account.AccessToken is { } token)
        {
            _secrets.Set(SecretNames.AccountAccessToken, token);
        }
        if (account.RefreshToken is { } refresh)
        {
            _secrets.Set(SecretNames.AccountRefreshToken, refresh);
        }

        await _devices.UpdateAsync(_devices.Current with { AccountId = account.Id }, cancellationToken);
        return account;
    }
}
