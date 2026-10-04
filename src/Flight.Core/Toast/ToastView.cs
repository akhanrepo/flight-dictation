namespace Flight.Core.Toast;

public enum ToastKind
{
    Hidden,

    /// <summary>Compact one-line status (listening, processing, download). Never takes focus.</summary>
    Pill,

    /// <summary>Content card with actions (preview, error, upgrade, copied).</summary>
    Card,
}

public enum ToastTone
{
    Neutral,
    Listening,
    Error,
    Success,
}

/// <summary>Buttons a toast can offer. The window maps them to orchestrator or shell calls.</summary>
public enum ToastAction
{
    Copy,
    Dismiss,
    Subscribe,
    KeepLocal,
}

/// <summary>
/// Everything the toast window renders for one state. Built by
/// <see cref="ToastPresenter"/>; the window holds no decisions of its own.
/// </summary>
public sealed record ToastView
{
    public static ToastView Hidden { get; } = new() { Kind = ToastKind.Hidden, Title = string.Empty };

    public required ToastKind Kind { get; init; }
    public ToastTone Tone { get; init; } = ToastTone.Neutral;

    /// <summary>One line, at most <see cref="ToastPresenter.MaxTitleLength"/> characters.</summary>
    public required string Title { get; init; }

    public string? Body { get; init; }

    /// <summary>Muted helper line, e.g. the keyboard shortcuts.</summary>
    public string? Hint { get; init; }

    /// <summary>The accent button.</summary>
    public ToastAction? Primary { get; init; }

    public ToastAction? Secondary { get; init; }

    public bool ShowSpinner { get; init; }

    /// <summary>0–100 for a determinate bar, NaN for indeterminate, null for none.</summary>
    public double? Progress { get; init; }

    /// <summary>
    /// True only for cards that need Enter/Esc. Pills never take focus from
    /// the app the user is working in.
    /// </summary>
    public bool TakesFocus { get; init; }

    /// <summary>Hide on its own after this long (the "Copied" confirmation).</summary>
    public TimeSpan? AutoHideAfter { get; init; }
}
