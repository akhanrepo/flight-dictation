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

    private async Task<Account> AdoptAsync(Account account, CancellationToken cancellationToken)
    {
        Current = account;
        _secrets.Set(SecretNames.AccountEmail, account.Email);
        if (account.AccessToken is { } token)
        {
            _secrets.Set(SecretNames.AccountAccessToken, token);
        }

        await _devices.UpdateAsync(_devices.Current with { AccountId = account.Id }, cancellationToken);
        return account;
    }
}
