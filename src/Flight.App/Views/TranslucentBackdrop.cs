using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Flight.App.Views;

/// <summary>
/// Desktop Acrylic tuned for the toast: the window behind shows through
/// faintly, but not so much that text gets hard to read.
/// </summary>
/// <remarks>
/// Two things the stock backdrop gets wrong for a toast: it goes opaque
/// whenever its window isn't active (pills never take focus), and once any
/// appearance property is customised the controller stops following the
/// theme — it keeps its light tint under dark-theme text. So this pins
/// <see cref="SystemBackdropConfiguration.IsInputActive"/> on and sets every
/// color per theme itself. Windows still swaps in the fallback color when
/// transparency effects are off or battery saver is on.
/// </remarks>
public sealed class TranslucentBackdrop : SystemBackdrop
{
    private readonly record struct Look(Color Tint, float TintOpacity, float LuminosityOpacity, Color Fallback);

    // Tuned by eye against busy backgrounds for comfortable text contrast;
    // lower opacities = more see-through.
    private static readonly Look Dark = new(Color.FromArgb(255, 0x20, 0x20, 0x20), 0.72f, 0.88f, Color.FromArgb(255, 0x2B, 0x2B, 0x2B));
    private static readonly Look Light = new(Color.FromArgb(255, 0xF3, 0xF3, 0xF3), 0.62f, 0.90f, Color.FromArgb(255, 0xF9, 0xF9, 0xF9));

    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private FrameworkElement? _root;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        _root = xamlRoot.Content as FrameworkElement;
        if (_root is not null)
        {
            _root.ActualThemeChanged += OnThemeChanged;
            _root.Loaded += OnRootLoaded;
        }

        _configuration = new SystemBackdropConfiguration { IsInputActive = true };
        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
        UpdateTheme();
        _controller.SetSystemBackdropConfiguration(_configuration);
        _controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        if (_root is not null)
        {
            _root.ActualThemeChanged -= OnThemeChanged;
            _root.Loaded -= OnRootLoaded;
        }
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => UpdateTheme();

    private void OnRootLoaded(object sender, RoutedEventArgs e) => UpdateTheme();

    private void UpdateTheme()
    {
        // Match the text: window content follows the app theme. Before the
        // root has loaded its ActualTheme isn't meaningful yet.
        var dark = _root is { IsLoaded: true }
            ? _root.ActualTheme == ElementTheme.Dark
            : Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var look = dark ? Dark : Light;

        if (_configuration is not null)
        {
            _configuration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        }

        if (_controller is not null)
        {
            _controller.TintColor = look.Tint;
            _controller.TintOpacity = look.TintOpacity;
            _controller.LuminosityOpacity = look.LuminosityOpacity;
            _controller.FallbackColor = look.Fallback;
        }
    }
}
