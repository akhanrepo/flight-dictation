using Flight.Core.Dictation;
using Flight.Core.Models;

namespace Flight.App.Views;

/// <summary>
/// DI-friendly IDictationUi: the real presenter can only exist after the UI
/// thread creates the toast window in OnLaunched, but the container is built
/// before that. Calls before initialization are dropped (nothing user-facing
/// happens before OnLaunched wires the presenter).
/// </summary>
public sealed class DeferredDictationUi : IDictationUi
{
    public IDictationUi? Inner { get; set; }

    public void ShowCapturing() => Inner?.ShowCapturing();
    public void ShowProcessing() => Inner?.ShowProcessing();
    public void ShowPreview(string cleanedText) => Inner?.ShowPreview(cleanedText);
    public void ShowError(string message) => Inner?.ShowError(message);
    public void ShowModelDownloadProgress(ModelDownloadProgress progress) => Inner?.ShowModelDownloadProgress(progress);
    public void Hide() => Inner?.Hide();
}
