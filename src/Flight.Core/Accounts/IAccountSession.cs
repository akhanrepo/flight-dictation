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

    /// <summary>
    /// Runs a backend call as <see cref="Current"/>. An expired access token
    /// is refreshed once and the call retried; if the refresh itself is
    /// rejected the account is signed out and <see cref="SessionExpiredException"/>
    /// is thrown. Network failures propagate unchanged, so callers can still
    /// treat them as "offline".
    /// </summary>
    /// <exception cref="InvalidOperationException">No account is signed in.</exception>
    Task<T> CallAsync<T>(Func<Account, CancellationToken, Task<T>> call, CancellationToken cancellationToken = default);
}
