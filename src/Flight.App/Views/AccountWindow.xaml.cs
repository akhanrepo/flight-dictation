using Flight.Core.Accounts;
using Flight.Core.Billing;
using Flight.Core.Entitlement;
using Microsoft.UI.Xaml;

namespace Flight.App.Views;

/// <summary>
/// Account creation/sign-in and the route to Stripe's hosted pages. Thin
/// shell: every rule lives in <see cref="IAccountSession"/>,
/// <see cref="IBillingPortal"/>, and <see cref="IEntitlementService"/>.
/// </summary>
public sealed partial class AccountWindow : Window
{
    private readonly IAccountSession _account;
    private readonly IBillingPortal _billing;
    private readonly IEntitlementService _entitlement;

    public AccountWindow(IAccountSession account, IBillingPortal billing, IEntitlementService entitlement)
    {
        _account = account;
        _billing = billing;
        _entitlement = entitlement;
        InitializeComponent();
        Title = "Flight account";
        Refresh();
    }

    private async void Refresh()
    {
        var signedIn = _account.Current is not null;
        CredentialsPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        SubscriptionPanel.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        HeadingText.Text = signedIn ? $"Signed in as {_account.Current!.Email}" : "Your Flight account";

        if (!signedIn)
        {
            return;
        }

        try
        {
            var decision = await _entitlement.EvaluateAsync();
            StatusText.Text = decision.Status switch
            {
                EntitlementStatus.Paid => "Subscription active. Cloud cleanup is available.",
                EntitlementStatus.TrialActive =>
                    $"Cloud trial active until {decision.TrialEndsAt:d}. No payment needed yet.",
                EntitlementStatus.TrialNotStarted =>
                    "Your cloud trial starts with your first cloud dictation.",
                _ => decision.Message,
            };
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't check your subscription: {ex.Message}";
        }
    }

    private Task SignInAsync(bool createAccount) => RunAsync(async () =>
    {
        if (createAccount)
        {
            await _account.SignUpAsync(EmailBox.Text.Trim(), PasswordBox.Password);
        }
        else
        {
            await _account.SignInAsync(EmailBox.Text.Trim(), PasswordBox.Password);
        }

        PasswordBox.Password = string.Empty;
        Refresh();
    });

    private async void OnSignUpClicked(object sender, RoutedEventArgs e) => await SignInAsync(createAccount: true);

    private async void OnSignInClicked(object sender, RoutedEventArgs e) => await SignInAsync(createAccount: false);

    private async void OnCheckoutClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(async () =>
        {
            await _billing.StartCheckoutAsync();
            StatusText.Text = "Stripe checkout opened in your browser. " +
                              "Come back here once it's done and Flight will pick up the subscription.";
        });

    private async void OnPortalClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _billing.OpenPortalAsync());

    private async void OnSignOutClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(async () =>
        {
            await _account.SignOutAsync();
            Refresh();
        });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            StatusText.Text = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
