using Flight.Core.Dictation;
using Flight.Core.Models;

namespace Flight.Core.Toast;

/// <summary>
/// The orchestrator's <see cref="IDictationUi"/>: turns each dictation state
/// into a <see cref="ToastView"/> for the window to render. Calls arrive on
/// hook and background threads; <see cref="Changed"/> fires on the caller's
/// thread and the window marshals.
/// </summary>
public sealed class ToastPresenter : IDictationUi
{
    /// <summary>Titles are a single line; anything longer is trimmed with an ellipsis.</summary>
    public const int MaxTitleLength = 40;

    public static readonly TimeSpan CopiedDuration = TimeSpan.FromSeconds(1);

    private bool _copyRequested;

    public ToastView View { get; private set; } = ToastView.Hidden;

    public event Action<ToastView>? Changed;

    /// <summary>
    /// The user pressed Copy. The orchestrator hides the toast once the
    /// clipboard is written; that hide becomes a brief confirmation instead.
    /// </summary>
    public void NotifyCopyRequested() => _copyRequested = true;

    public void ShowCapturing() => Show(new ToastView
    {
        Kind = ToastKind.Pill,
        Tone = ToastTone.Listening,
        Title = "Listening",
        Hint = "Release Right Alt to finish",
    });

    public void ShowProcessing() => Show(new ToastView
    {
        Kind = ToastKind.Pill,
        Title = "Cleaning up…",
        ShowSpinner = true,
    });

    public void ShowPreview(string cleanedText) => Show(new ToastView
    {
        Kind = ToastKind.Card,
        Title = "Preview",
        Body = cleanedText,
        Hint = "Enter to copy · Esc to dismiss",
        Primary = ToastAction.Copy,
        Secondary = ToastAction.Dismiss,
        TakesFocus = true,
    });

    public void ShowError(string message) => Show(new ToastView
    {
        Kind = ToastKind.Card,
        Tone = ToastTone.Error,
        Title = "Something went wrong",
        Body = message,
        Hint = "Esc to dismiss",
        Primary = ToastAction.Dismiss,
        TakesFocus = true,
    });

    public void ShowModelDownloadProgress(ModelDownloadProgress progress) => Show(new ToastView
    {
        Kind = ToastKind.Pill,
        Title = progress.Percent is { } percent
            ? $"Setting up · downloading model {percent:0}%"
            : "Setting up · downloading model…",
        Progress = progress.Percent ?? double.NaN,
    });

    public void Hide()
    {
        if (_copyRequested)
        {
            _copyRequested = false;
            Show(new ToastView
            {
                Kind = ToastKind.Card,
                Tone = ToastTone.Success,
                Title = "Copied to clipboard",
                AutoHideAfter = CopiedDuration,
            });
            return;
        }

        Show(ToastView.Hidden);
    }

    private void Show(ToastView view)
    {
        // A copy request only applies to the hide that follows it.
        if (view.Kind != ToastKind.Hidden && view.Tone != ToastTone.Success)
        {
            _copyRequested = false;
        }

        View = view with { Title = Trim(view.Title) };
        Changed?.Invoke(View);
    }

    private static string Trim(string title) =>
        title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)].TrimEnd() + "…";
}
