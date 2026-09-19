using Flight.Core.Dictation;
using Flight.Core.Entitlement;
using Flight.Core.Models;
using Microsoft.UI.Dispatching;

namespace Flight.App.Views;

/// <summary>
/// IDictationUi implementation that marshals orchestrator callbacks (which
/// arrive on hook/background threads) onto the UI thread.
/// </summary>
public sealed class DictationUiPresenter : IDictationUi
{
    private readonly DictationToastWindow _window;
    private readonly DispatcherQueue _dispatcher;

    public DictationUiPresenter(DictationToastWindow window, DispatcherQueue dispatcher)
    {
        _window = window;
        _dispatcher = dispatcher;
    }

    private void OnUiThread(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(() => action());
        }
    }

    public void ShowCapturing() => OnUiThread(_window.ShowCapturing);
    public void ShowProcessing() => OnUiThread(_window.ShowProcessing);
    public void ShowPreview(string cleanedText) => OnUiThread(() => _window.ShowPreview(cleanedText));
    public void ShowError(string message) => OnUiThread(() => _window.ShowError(message));
    public void ShowUpgradeRequired(EntitlementDecision decision) =>
        OnUiThread(() => _window.ShowUpgradeRequired(decision));
    public void ShowModelDownloadProgress(ModelDownloadProgress progress) =>
        OnUiThread(() => _window.ShowModelDownloadProgress(progress));
    public void Hide() => OnUiThread(_window.HideToast);
}
