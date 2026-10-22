using Flight.Core.Dictation;
using Flight.Core.Models;

namespace Flight.Core.Toast;

/// <summary>
/// The orchestrator's <see cref="IDictationUi"/>: turns each dictation state
/// into a <see cref="ToastView"/> for the indicator pill to render. Calls
/// arrive on hook, audio and background threads; <see cref="Changed"/> and
/// <see cref="LevelChanged"/> fire on the caller's thread and the window
/// marshals.
/// </summary>
public sealed class ToastPresenter : IDictationUi
{
    /// <summary>Titles are a single line; anything longer is trimmed with an ellipsis.</summary>
    public const int MaxTitleLength = 48;

    public static readonly TimeSpan CopiedDuration = TimeSpan.FromMilliseconds(800);
    public static readonly TimeSpan NothingHeardDuration = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(6);

    public ToastView View { get; private set; } = ToastView.Hidden;

    public event Action<ToastView>? Changed;

    /// <summary>
    /// Microphone level while listening. Separate from <see cref="Changed"/>
    /// because it fires ~20 times a second and only moves the level bars.
    /// </summary>
    public event Action<float>? LevelChanged;

    public void ShowCapturing() => Show(new ToastView { Tone = ToastTone.Listening, Title = "Listening" });

    public void ShowLevel(float level)
    {
        if (View.Tone == ToastTone.Listening && !View.IsHidden)
        {
            LevelChanged?.Invoke(Math.Clamp(level, 0f, 1f));
        }
    }

    public void ShowProcessing() => Show(new ToastView { Tone = ToastTone.Processing, Title = "Processing" });

    public void ShowCopied() => Show(new ToastView
    {
        Tone = ToastTone.Success,
        Title = "Copied to clipboard",
        AutoHideAfter = CopiedDuration,
    });

    public void ShowNothingHeard() => Show(new ToastView
    {
        Title = "Didn't catch that",
        ShowsText = true,
        AutoHideAfter = NothingHeardDuration,
    });

    public void ShowError(string message) => Show(new ToastView
    {
        Tone = ToastTone.Error,
        Title = FirstLine(message),
        Detail = message,
        ShowsText = true,
        AutoHideAfter = ErrorDuration,
    });

    public void ShowModelDownloadProgress(ModelDownloadProgress progress) => Show(new ToastView
    {
        Title = progress.Percent is { } percent
            ? $"Downloading model {percent:0}%"
            : "Downloading model…",
        ShowsText = true,
        Progress = progress.Percent ?? double.NaN,
    });

    public void Hide() => Show(ToastView.Hidden);

    private void Show(ToastView view)
    {
        View = view with { Title = Trim(view.Title) };
        Changed?.Invoke(View);
    }

    private static string FirstLine(string text)
    {
        var line = text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return line?.Trim() ?? string.Empty;
    }

    private static string Trim(string title) =>
        title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)].TrimEnd() + "…";
}
