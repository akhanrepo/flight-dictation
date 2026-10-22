using System.Runtime.InteropServices;

namespace Flight.App.Win32;

/// <summary>DPI scale and window-style helpers for the indicator pill.</summary>
public static class WindowMetrics
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>The scale of the monitor the window is on (1.0 = 100%).</summary>
    public static double ScaleFor(nint hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>
    /// Clicks and drags on the window never make it the foreground window, so
    /// keyboard focus stays in the app the user is dictating into.
    /// </summary>
    public static void MakeNonActivating(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)((long)style | WS_EX_NOACTIVATE));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
}
