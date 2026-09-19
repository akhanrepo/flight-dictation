using Flight.Core.Languages;
using Flight.Core.Llm;
using Flight.Core.Security;
using Flight.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flight.App.Views;

/// <summary>
/// Language settings. Thin shell over <see cref="ISettingsStore"/>: the window
/// holds no rules of its own, and a change here is picked up by the next
/// dictation without a restart because the orchestrator re-reads the store on
/// every trigger.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private bool _loading;

    public SettingsWindow(ISettingsStore settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
        InitializeComponent();
        Title = "Flight settings";
        Load();
    }

    private void Load()
    {
        _loading = true;
        try
        {
            SourceLanguageBox.ItemsSource = SupportedLanguages.All;
            TargetLanguageBox.ItemsSource = SupportedLanguages.All;

            var current = _settings.Current;
            SourceLanguageBox.SelectedItem = SupportedLanguages.Get(current.SourceLanguage);
            TargetLanguageBox.SelectedItem = SupportedLanguages.Get(current.TargetLanguage);
            CloudModeToggle.IsOn = current.LlmMode == LlmMode.Cloud;

            // The saved key is never rendered back; an empty box with a saved
            // key means "unchanged".
            ApiKeyBox.PlaceholderText = _secrets.Get(SecretNames.CloudLlmApiKey) is null
                ? "Stored encrypted on this device"
                : "A key is saved. Type to replace it.";

            UpdateStatus(current);
            UpdateEngineStatus(current);
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading
            || SourceLanguageBox.SelectedItem is not SupportedLanguage source
            || TargetLanguageBox.SelectedItem is not SupportedLanguage target)
        {
            return;
        }

        var updated = _settings.Current with
        {
            SourceLanguage = source.Code,
            TargetLanguage = target.Code,
        };

        try
        {
            await _settings.UpdateAsync(updated);
            UpdateStatus(updated);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't save that: {ex.Message}";
        }
    }

    private async void OnCloudModeToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var updated = _settings.Current with
        {
            LlmMode = CloudModeToggle.IsOn ? LlmMode.Cloud : LlmMode.Local,
        };

        try
        {
            await _settings.UpdateAsync(updated);
            UpdateEngineStatus(updated);
        }
        catch (Exception ex)
        {
            EngineStatusText.Text = $"Couldn't save that: {ex.Message}";
        }
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var key = ApiKeyBox.Password.Trim();
        if (key.Length == 0)
        {
            return;
        }

        _secrets.Set(SecretNames.CloudLlmApiKey, key);
        EngineStatusText.Text = "Key saved, encrypted on this device.";
    }

    private void UpdateEngineStatus(FlightSettings settings)
    {
        if (settings.LlmMode == LlmMode.Local)
        {
            EngineStatusText.Text = "Local cleanup. Free, unlimited, works offline.";
            return;
        }

        EngineStatusText.Text = _secrets.Get(SecretNames.CloudLlmApiKey) is null
            ? "Cloud cleanup needs an API key. Add one above, or switch back to local."
            : "Cloud cleanup. Takes effect on your next dictation.";
    }

    private void UpdateStatus(FlightSettings settings)
    {
        StatusText.Text = settings.IsTranslating
            ? $"Speak {SupportedLanguages.EnglishNameOf(settings.SourceLanguage)}, " +
              $"get {SupportedLanguages.EnglishNameOf(settings.TargetLanguage)} text. " +
              "Takes effect on your next dictation."
            : $"Cleanup only, no translation. Takes effect on your next dictation.";
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
