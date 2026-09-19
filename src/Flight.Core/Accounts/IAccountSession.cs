namespace Flight.Core.Accounts;

/// <summary>
/// The signed-in account, if any. Null throughout local-only use and
/// throughout the cloud trial — an account is required only once the trial
/// ends and a subscription is needed.
/// </summary>
public interface IAccountSession
{
    Account? Current { get; }

    Task<Account> SignUpAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<Account> SignInAsync(string email, string password, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
