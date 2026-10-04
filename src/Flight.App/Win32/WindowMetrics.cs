using System.Runtime.InteropServices;
using Flight.Core.Toast;

namespace Flight.App.Win32;

/// <summary>DPI scale and foreground-window access for the toast.</summary>
public static class WindowMetrics
{
    /// <summary>The scale of the monitor the window is on (1.0 = 100%).</summary>
    public static double ScaleFor(nint hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}

/// <summary><see cref="IForegroundWindow"/> over GetForegroundWindow/SetForegroundWindow.</summary>
public sealed class ForegroundWindow : IForegroundWindow
{
    public nint Current => GetForegroundWindow();

    // Allowed here: Flight is the foreground process when the user presses
    // Copy/Enter/Esc in the toast, which is when this runs.
    public bool TryActivate(nint window) => IsWindow(window) && SetForegroundWindow(window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);
}
