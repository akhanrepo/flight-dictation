using Flight.Core.Input;
using Flight.Core.Languages;
using Flight.Core.Llm;
using Flight.Core.Security;
using Flight.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flight.App.Views;

/// <summary>
/// Settings. Thin shell over <see cref="ISettingsStore"/>: the window holds no
/// rules of its own, and a change here is picked up by the next dictation
/// without a restart because the orchestrator and the hotkey re-read the store
/// on every trigger.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private readonly PushToTalkHotkey _hotkey;
    private bool _loading;

    public SettingsWindow(ISettingsStore settings, ISecretStore secrets, PushToTalkHotkey hotkey)
    {
        _settings = settings;
        _secrets = secrets;
        _hotkey = hotkey;
        InitializeComponent();
        Title = "Flight settings";
        TitleLogo.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Platform.AppIcons.LogoPng));
        Platform.WindowChrome.Apply(this, TitleBar, widthDip: 440, heightDip: 640);
        // Closing mid-recording must not leave the keyboard captured.
        Closed += (_, _) =>
        {
            if (_hotkey.IsRecording)
            {
                _hotkey.CancelRecording();
            }
        };
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
            HotkeyButton.Content = current.Hotkey.ToString();
            AiCleanupToggle.IsOn = current.AiCleanup;
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

    private void OnHotkeyClicked(object sender, RoutedEventArgs e)
    {
        if (_hotkey.IsRecording)
        {
            _hotkey.CancelRecording();
            return;
        }

        HotkeyButton.Content = "Press your keys…";
        HotkeyStatusText.Text = "Hold the keys you want, then let go. Esc cancels.";
        // The result arrives on the keyboard hook's thread.
        _hotkey.BeginRecording(result => DispatcherQueue.TryEnqueue(() => OnHotkeyRecorded(result)));
    }

    private async void OnHotkeyRecorded(HotkeyRecording result)
    {
        var current = _settings.Current;
        if (result.Cancelled)
        {
            HotkeyButton.Content = current.Hotkey.ToString();
            AiCleanupToggle.IsOn = current.AiCleanup;
            HotkeyStatusText.Text = string.Empty;
            return;
        }

        if (!result.IsValid)
        {
            HotkeyButton.Content = current.Hotkey.ToString();
            AiCleanupToggle.IsOn = current.AiCleanup;
            HotkeyStatusText.Text = $"{result.Hotkey} can't be used: {result.Error}";
            return;
        }

        // IsValid guarantees a hotkey.
        var hotkey = result.Hotkey!;
        try
        {
            await _settings.UpdateAsync(current with { Hotkey = hotkey });
            HotkeyButton.Content = hotkey.ToString();
            HotkeyStatusText.Text = $"Saved. Hold {hotkey} to dictate.";
        }
        catch (Exception ex)
        {
            HotkeyButton.Content = current.Hotkey.ToString();
            AiCleanupToggle.IsOn = current.AiCleanup;
            HotkeyStatusText.Text = $"Couldn't save that: {ex.Message}";
        }
    }

    private async void OnAiCleanupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            await _settings.UpdateAsync(_settings.Current with { AiCleanup = AiCleanupToggle.IsOn });
        }
        catch (Exception ex)
        {
            HotkeyStatusText.Text = $"Couldn't save that: {ex.Message}";
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
            : "Cleanup only, no translation. Takes effect on your next dictation.";
    }
}
