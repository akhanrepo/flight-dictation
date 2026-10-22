namespace Flight.Core.Toast;

public enum ToastTone
{
    /// <summary>Plain message: "Didn't catch that", download progress.</summary>
    Neutral,

    /// <summary>Red pulsing dot and live level bars.</summary>
    Listening,

    /// <summary>Spinner.</summary>
    Processing,

    /// <summary>Check mark: the text is on the clipboard.</summary>
    Success,

    Error,
}

/// <summary>
/// Everything the indicator pill renders for one state. Built by
/// <see cref="ToastPresenter"/>; the window holds no decisions of its own.
/// The pill never takes keyboard focus, in any state.
/// </summary>
public sealed record ToastView
{
    public static ToastView Hidden { get; } = new() { IsHidden = true, Title = string.Empty };

    public bool IsHidden { get; init; }

    public ToastTone Tone { get; init; } = ToastTone.Neutral;

    /// <summary>
    /// One line, at most <see cref="ToastPresenter.MaxTitleLength"/>
    /// characters. Always the accessible name; drawn only when
    /// <see cref="ShowsText"/>.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>False for the icon-only states (listening, processing, copied).</summary>
    public bool ShowsText { get; init; }

    /// <summary>The untrimmed message, for the tooltip.</summary>
    public string? Detail { get; init; }

    /// <summary>0–100 for a determinate bar, NaN for indeterminate, null for none.</summary>
    public double? Progress { get; init; }

    /// <summary>Hide on its own after this long.</summary>
    public TimeSpan? AutoHideAfter { get; init; }
}
