using Flight.Core.Dictation;
using Flight.Core.Entitlement;
using Flight.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Flight.App.Views;

/// <summary>
/// The preview toast: a small always-on-top window near the bottom of the
/// primary display. Confirm = Enter or button; dismiss = Esc or button.
/// State changes arrive via <see cref="DictationUiPresenter"/>.
/// </summary>
public sealed partial class DictationToastWindow : Window
{
    /// <summary>Set by App after the orchestrator is resolved (the orchestrator itself needs the UI).</summary>
    public DictationOrchestrator? Orchestrator { get; set; }

    /// <summary>Opens the settings window. Supplied by App, which owns the DI container.</summary>
    public Action? OpenSettings { get; set; }

    /// <summary>Starts the subscribe flow (account, then Stripe checkout). Supplied by App.</summary>
    public Action? StartUpgrade { get; set; }

    public DictationToastWindow()
    {
        InitializeComponent();
        ConfigureAppWindow();
    }

    private void ConfigureAppWindow()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);

        var displayArea = DisplayArea.Primary.WorkArea;
        const int width = 520;
        const int height = 300;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            (displayArea.Width - width) / 2,
            displayArea.Height - height - 24,
            width,
            height));
    }

    public void ShowCapturing()
    {
        ResetPanels();
        StatusText.Text = "🎙 Listening… (release Right Alt to finish)";
        AppWindow.Show(activateWindow: false);
    }

    public void ShowProcessing()
    {
        ResetPanels();
        StatusText.Text = "Processing…";
        Spinner.IsActive = true;
        Spinner.Visibility = Visibility.Visible;
        AppWindow.Show(activateWindow: false);
    }

    public void ShowPreview(string cleanedText)
    {
        ResetPanels();
        StatusText.Text = "Preview — copy to clipboard?";
        PreviewText.Text = cleanedText;
        PreviewText.Visibility = Visibility.Visible;
        PreviewButtons.Visibility = Visibility.Visible;
        AppWindow.Show();
        Activate();
    }

    public void ShowError(string message)
    {
        ResetPanels();
        StatusText.Text = "Something went wrong";
        ErrorText.Text = message + " (Esc to dismiss)";
        ErrorText.Visibility = Visibility.Visible;
        AppWindow.Show();
        Activate();
    }

    /// <summary>
    /// Cloud blocked by entitlement. Deliberately not styled as an error: the
    /// app still works, just not in cloud mode.
    /// </summary>
    public void ShowUpgradeRequired(EntitlementDecision decision)
    {
        ResetPanels();
        StatusText.Text = decision.OffersUpgrade ? "Cloud mode needs a subscription" : "Cloud mode is paused";
        UpgradeText.Text = decision.Message;
        UpgradeText.Visibility = Visibility.Visible;
        UpgradeButton.Visibility = decision.OffersUpgrade ? Visibility.Visible : Visibility.Collapsed;
        UpgradeButtons.Visibility = Visibility.Visible;
        AppWindow.Show();
        Activate();
    }

    public void ShowModelDownloadProgress(ModelDownloadProgress progress)
    {
        StatusText.Text = progress.Percent is { } percent
            ? $"First-run setup: downloading {progress.FileName} ({percent:F0}%)"
            : $"First-run setup: downloading {progress.FileName}…";
        DownloadBar.Visibility = Visibility.Visible;
        DownloadBar.IsIndeterminate = progress.Percent is null;
        DownloadBar.Value = progress.Percent ?? 0;
        AppWindow.Show(activateWindow: false);
    }

    public void HideToast()
    {
        ResetPanels();
        AppWindow.Hide();
    }

    private void ResetPanels()
    {
        Spinner.IsActive = false;
        Spinner.Visibility = Visibility.Collapsed;
        PreviewText.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        DownloadBar.Visibility = Visibility.Collapsed;
        PreviewButtons.Visibility = Visibility.Collapsed;
        UpgradeText.Visibility = Visibility.Collapsed;
        UpgradeButtons.Visibility = Visibility.Collapsed;
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => OpenSettings?.Invoke();

    private void OnUpgradeClicked(object sender, RoutedEventArgs e) => StartUpgrade?.Invoke();

    private async void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (Orchestrator is not null)
        {
            await Orchestrator.ConfirmAsync();
        }
    }

    private void OnDismissClicked(object sender, RoutedEventArgs e)
    {
        Orchestrator?.Reject();
        Orchestrator?.DismissError();
    }

    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Orchestrator is not null)
        {
            e.Handled = true;
            await Orchestrator.ConfirmAsync();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Orchestrator?.Reject();
            Orchestrator?.DismissError();
        }
    }
}
