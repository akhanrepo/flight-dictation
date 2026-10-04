namespace Flight.Core.Toast;

/// <summary>The OS foreground window. Implemented with Win32 in the shell; faked in tests.</summary>
public interface IForegroundWindow
{
    nint Current { get; }

    bool TryActivate(nint window);
}

/// <summary>
/// The preview takes focus so Enter and Esc work. Once the user copies or
/// dismisses, focus goes back to the window they were dictating into, so
/// Ctrl+V works straight away.
/// </summary>
public sealed class FocusReturn
{
    private readonly IForegroundWindow _foreground;
    private nint _previous;

    public FocusReturn(IForegroundWindow foreground) => _foreground = foreground;

    /// <summary>Call just before the toast activates itself.</summary>
    public void Remember(nint toastWindow)
    {
        var current = _foreground.Current;

        // Already focused (e.g. an error card replacing the preview): keep the
        // window remembered the first time.
        if (current != 0 && current != toastWindow)
        {
            _previous = current;
        }
    }

    /// <summary>
    /// Call once the toast hides. Only hands focus back while the toast still
    /// has it — if the user has clicked into another app meanwhile, that's
    /// where they want to be.
    /// </summary>
    /// <returns>True when focus was returned.</returns>
    public bool Restore(nint toastWindow)
    {
        var previous = _previous;
        _previous = 0;

        return previous != 0
               && _foreground.Current == toastWindow
               && _foreground.TryActivate(previous);
    }
}
