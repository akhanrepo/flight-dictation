namespace Flight.Core.Tray;

/// <summary>
/// Tray commands that need the WinUI shell: windows, and leaving the app.
/// Implemented by <c>App</c>; faked in tests.
/// </summary>
public interface IShellActions
{
    void OpenSettings();

    void OpenAccount();

    /// <summary>Shuts down cleanly (see <see cref="ShutdownCoordinator"/>) and exits.</summary>
    void Quit();

    /// <summary>Shuts down and relaunches into the downloaded update.</summary>
    void RestartToUpdate();
}
