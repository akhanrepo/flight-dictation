namespace Flight.Core.Tray;

/// <summary>What the tray menu can ask for.</summary>
public enum TrayCommand
{
    /// <summary>Not clickable: the status line and separators.</summary>
    None,
    PauseResume,
    Settings,
    Account,
    StartWithWindows,
    RestartToUpdate,
    Quit,
}

/// <summary>Which tray icon to show. Maps to the generated <c>flight*.ico</c> files.</summary>
public enum TrayIconKind
{
    Normal,
    Listening,
    Paused,
}

/// <summary>
/// One menu row. A <see cref="TrayCommand.None"/> item with empty text is a
/// separator. Text is plain; the Win32 renderer escapes '&amp;'.
/// </summary>
public sealed record TrayMenuItem(
    TrayCommand Command,
    string Text,
    bool Enabled = true,
    bool Checked = false,
    bool Visible = true)
{
    public static TrayMenuItem Separator { get; } = new(TrayCommand.None, string.Empty, Enabled: false);

    public bool IsSeparator => Command == TrayCommand.None && Text.Length == 0;
}

/// <summary>Everything the tray shows, rebuilt whenever anything it reflects changes.</summary>
public sealed record TrayState(string Tooltip, string StatusLine, TrayIconKind Icon, IReadOnlyList<TrayMenuItem> Menu);
