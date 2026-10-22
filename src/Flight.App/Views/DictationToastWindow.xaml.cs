using Flight.App.Platform;
using Flight.App.Win32;
using Flight.Core.Dictation;
using Flight.Core.Toast;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace Flight.App.Views;

/// <summary>
/// The dictation toast. Renders <see cref="ToastPresenter.View"/>; sizing,
/// placement, the remembered position and focus return are decided in Core
/// (<see cref="ToastLayout"/>, <see cref="ToastPlacement"/>,
/// <see cref="IToastPositionStore"/>, <see cref="FocusReturn"/>).
/// </summary>
public sealed partial class DictationToastWindow : Window
{
    private readonly ToastPresenter _presenter;
    private readonly FocusReturn _focus;
    private readonly IToastPositionStore _positions;
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimerWrapper _autoHide;

    private ToastView _view = ToastView.Hidden;
    private PointInt32? _lastPlacedAt;
    private bool _placing;

    /// <summary>Set by App after the orchestrator is resolved (the orchestrator itself needs the UI).</summary>
    public DictationOrchestrator? Orchestrator { get; set; }

    /// <summary>Opens the settings window. Supplied by App, which owns the DI container.</summary>
    public Action? OpenSettings { get; set; }

    public DictationToastWindow(ToastPresenter presenter, FocusReturn focus, IToastPositionStore positions)
    {
        _presenter = presenter;
        _focus = focus;
        _positions = positions;

        InitializeComponent();
        AppWindow.SetIcon(AppIcons.App);
        _hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        _autoHide = new DispatcherQueueTimerWrapper(DispatcherQueue, OnAutoHide);
        Logo.Source = new BitmapImage(new Uri(AppIcons.LogoPng));

        ConfigureWindow();
        Root.SizeChanged += (_, _) => UpdateDragRegions();
    }

    private void ConfigureWindow()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        // Keep the border (Windows 11 draws rounded corners and a shadow), drop the title bar.
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new TranslucentBackdrop();

        AppWindow.Changed += OnAppWindowChanged;
    }

    /// <summary>Called on the UI thread whenever the presenter's view changes.</summary>
    public void Render(ToastView view)
    {
        var tookFocus = _view.TakesFocus;
        _view = view;
        _autoHide.Stop();
        FadeOut.Stop();
        Root.Opacity = 1;

        // Done with the card that needed Enter/Esc: hand focus back so the
        // user can paste straight away.
        if (tookFocus && !view.TakesFocus)
        {
            _focus.Restore(_hwnd);
        }

        if (view.Kind == ToastKind.Hidden)
        {
            ListeningPulse.Stop();
            AppWindow.Hide();
            return;
        }

        Apply(view);
        SizeAndPlace(view);

        if (view.TakesFocus)
        {
            _focus.Remember(_hwnd);
            AppWindow.Show(activateWindow: true);
            Activate();
            (PrimaryButton.Visibility == Visibility.Visible ? PrimaryButton : CloseButton)
                .Focus(FocusState.Programmatic);
        }
        else
        {
            // Pills never take focus from the app the user is working in.
            AppWindow.Show(activateWindow: false);
        }

        if (view.AutoHideAfter is { } after)
        {
            _autoHide.Start(after);
        }
    }

    private void Apply(ToastView view)
    {
        var isCard = view.Kind == ToastKind.Card;

        TitleText.Text = view.Title;

        // Indicator: one of dot / spinner / tone icon / logo.
        ListeningDot.Visibility = Show(view.Tone == ToastTone.Listening);
        if (view.Tone == ToastTone.Listening)
        {
            ListeningPulse.Begin();
        }
        else
        {
            ListeningPulse.Stop();
        }

        Spinner.IsActive = view.ShowSpinner;
        Spinner.Visibility = Show(view.ShowSpinner);
        ToneIcon.Visibility = Show(view.Tone is ToastTone.Error or ToastTone.Success);
        if (view.Tone == ToastTone.Error)
        {
            ToneIcon.Glyph = "";
            ToneIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        }
        else if (view.Tone == ToastTone.Success)
        {
            ToneIcon.Glyph = "";
            ToneIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
        Logo.Visibility = Show(view.Tone == ToastTone.Neutral && !view.ShowSpinner && view.Progress is null);

        // Pills: hint sits under the title. Cards: hint goes in the footer.
        PillHintText.Text = view.Hint ?? string.Empty;
        PillHintText.Visibility = Show(!isCard && view.Hint is not null);

        HeaderButtons.Visibility = Show(isCard && view.Tone != ToastTone.Success);

        DownloadBar.Visibility = Show(view.Progress is not null);
        DownloadBar.IsIndeterminate = view.Progress is { } p && double.IsNaN(p);
        DownloadBar.Value = view.Progress is { } v && !double.IsNaN(v) ? v : 0;

        BodyText.Text = view.Body ?? string.Empty;
        BodyScroller.Visibility = Show(isCard && view.Body is not null);
        BodyScroller.MaxHeight = double.PositiveInfinity;

        HintText.Text = isCard ? view.Hint ?? string.Empty : string.Empty;
        SetAction(PrimaryButton, view.Primary);
        SetAction(SecondaryButton, view.Secondary);
        Footer.Visibility = Show(isCard && (view.Hint is not null || view.Primary is not null));
    }

    private static void SetAction(Microsoft.UI.Xaml.Controls.Button button, ToastAction? action)
    {
        button.Visibility = Show(action is not null);
        button.Content = action switch
        {
            ToastAction.Copy => "Copy",
            ToastAction.Dismiss => "Dismiss",
            _ => null,
        };
        button.Tag = action;
    }

    private void SizeAndPlace(ToastView view)
    {
        var scale = WindowMetrics.ScaleFor(_hwnd);
        var width = ToastLayout.WidthFor(view.Kind);

        // Measure the content at the toast's width. Past the height cap, the
        // body scroller gives up the difference and scrolls instead.
        ContentPanel.Measure(new Size(width, double.PositiveInfinity));
        var desired = ContentPanel.DesiredSize.Height;
        if (desired > ToastLayout.MaxHeight && BodyScroller.Visibility == Visibility.Visible)
        {
            BodyScroller.MaxHeight = Math.Max(40, BodyScroller.DesiredSize.Height - (desired - ToastLayout.MaxHeight));
            ContentPanel.Measure(new Size(width, double.PositiveInfinity));
            desired = ContentPanel.DesiredSize.Height;
        }

        var size = ToastLayout.SizeFor(view.Kind, desired, scale);
        var at = ToastPlacement.Place(size, _positions.Load(), WorkAreas(scale), Primary(scale));

        _placing = true;
        try
        {
            AppWindow.MoveAndResize(new RectInt32(at.X, at.Y, size.Width, size.Height));
            _lastPlacedAt = new PointInt32(at.X, at.Y);
        }
        finally
        {
            _placing = false;
        }
    }

    private static List<WorkArea> WorkAreas(double scale)
    {
        // Index rather than enumerate: the projected list from FindAll()
        // throws "No such interface supported" when enumerated (WinAppSDK bug).
        var displays = DisplayArea.FindAll();
        var areas = new List<WorkArea>(displays.Count);
        for (var i = 0; i < displays.Count; i++)
        {
            var w = displays[i].WorkArea;
            areas.Add(new WorkArea(new PixelRect(w.X, w.Y, w.Width, w.Height), scale));
        }
        return areas;
    }

    private static WorkArea Primary(double scale)
    {
        var w = DisplayArea.Primary.WorkArea;
        return new WorkArea(new PixelRect(w.X, w.Y, w.Width, w.Height), scale);
    }

    // The user dragged the toast: remember where, for next time and next launch.
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange || _placing || !sender.IsVisible)
        {
            return;
        }

        var position = sender.Position;
        if (_lastPlacedAt is { } placed && placed.X == position.X && placed.Y == position.Y)
        {
            return;
        }

        _positions.Save(new PixelPoint(position.X, position.Y));
        _lastPlacedAt = position;
    }

    /// <summary>
    /// Everything drags the window except the controls and the selectable
    /// text, which stay interactive.
    /// </summary>
    private void UpdateDragRegions()
    {
        if (Root.XamlRoot is null)
        {
            return;
        }

        var scale = Root.XamlRoot.RasterizationScale;
        var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        source.SetRegionRects(NonClientRegionKind.Caption,
            [new RectInt32(0, 0, (int)(Root.ActualWidth * scale), (int)(Root.ActualHeight * scale))]);

        var interactive = new FrameworkElement[] { SettingsButton, CloseButton, PrimaryButton, SecondaryButton, BodyScroller }
            .Where(e => e.Visibility == Visibility.Visible && e.ActualWidth > 0)
            .Select(e =>
            {
                var bounds = e.TransformToVisual(Root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
                return new RectInt32((int)(bounds.X * scale), (int)(bounds.Y * scale),
                    (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale));
            })
            .ToArray();
        source.SetRegionRects(NonClientRegionKind.Passthrough, interactive);
    }

    private void OnAutoHide()
    {
        FadeOut.Completed -= OnFadedOut;
        FadeOut.Completed += OnFadedOut;
        FadeOut.Begin();
    }

    private void OnFadedOut(object? sender, object e)
    {
        FadeOut.Completed -= OnFadedOut;
        if (_view.AutoHideAfter is not null)
        {
            _presenter.Hide();
        }
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => OpenSettings?.Invoke();

    private async void OnPrimaryClicked(object sender, RoutedEventArgs e) => await RunAsync(PrimaryButton.Tag as ToastAction?);

    private async void OnSecondaryClicked(object sender, RoutedEventArgs e) => await RunAsync(SecondaryButton.Tag as ToastAction?);

    private void OnDismissClicked(object sender, RoutedEventArgs e) => Dismiss();

    private async Task RunAsync(ToastAction? action)
    {
        switch (action)
        {
            case ToastAction.Copy:
                await CopyAsync();
                break;
            case ToastAction.Dismiss:
                Dismiss();
                break;
        }
    }

    private async Task CopyAsync()
    {
        if (Orchestrator is null)
        {
            return;
        }

        // The orchestrator hides the toast once the clipboard is written;
        // the presenter turns that hide into the "Copied" confirmation.
        _presenter.NotifyCopyRequested();
        await Orchestrator.ConfirmAsync();
    }

    private void Dismiss()
    {
        Orchestrator?.Reject();
        Orchestrator?.DismissError();
    }

    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && _view.Primary == ToastAction.Copy)
        {
            e.Handled = true;
            await CopyAsync();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Dismiss();
        }
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A one-shot timer on the UI thread.</summary>
    private sealed class DispatcherQueueTimerWrapper
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

        public DispatcherQueueTimerWrapper(Microsoft.UI.Dispatching.DispatcherQueue queue, Action onTick)
        {
            _timer = queue.CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) => onTick();
        }

        public void Start(TimeSpan after)
        {
            _timer.Interval = after;
            _timer.Start();
        }

        public void Stop() => _timer.Stop();
    }
}
