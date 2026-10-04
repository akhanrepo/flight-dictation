using Flight.App.Win32;
using Flight.Core.Toast;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Flight.App.Platform;

/// <summary>
/// Shared look for Flight's utility windows (feature #7): Mica backdrop,
/// content drawn into a slim custom title bar, a fixed size in DIPs
/// converted for the monitor's scale, centered in the primary work area.
/// </summary>
public static class WindowChrome
{
    public static void Apply(Window window, UIElement titleBar, double widthDip, double heightDip)
    {
        window.SystemBackdrop = new MicaBackdrop();
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        window.AppWindow.SetIcon(AppIcons.App);

        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false; // close only: these are quick, transient windows
        }

        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        var scale = WindowMetrics.ScaleFor(hwnd);
        var width = ToastLayout.ToPhysical(widthDip, scale);
        var height = ToastLayout.ToPhysical(heightDip, scale);
        var area = DisplayArea.Primary.WorkArea;
        window.AppWindow.MoveAndResize(new RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width,
            height));
    }
}
