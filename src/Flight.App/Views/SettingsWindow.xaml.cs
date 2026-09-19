using Flight.Core.Languages;
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
    private bool _loading;

    public SettingsWindow(ISettingsStore settings)
    {
        _settings = settings;
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
            UpdateStatus(current);
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
