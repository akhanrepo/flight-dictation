using Flight.App.Platform;
using Flight.App.Win32;
using Flight.Core.Dictation;
using Flight.Core.Toast;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;

namespace Flight.App.Views;

/// <summary>
/// The indicator pill (feature #9). Renders <see cref="ToastPresenter.View"/>;
/// sizing, placement and the remembered position are decided in Core
/// (<see cref="ToastLayout"/>, <see cref="ToastPlacement"/>,
/// <see cref="IToastPositionStore"/>). It never takes keyboard focus, so the
/// app the user is dictating into stays active and Ctrl+V works the moment
/// the text is copied.
/// </summary>
public sealed partial class DictationToastWindow : Window
{
    private const double HorizontalPadding = ToastLayout.HorizontalPadding;

    // Bar heights follow the level with a centre-weighted shape, so the bars
    // read as a waveform rather than a meter.
    private static readonly double[] BarShape = [0.55, 0.8, 1.0, 0.8, 0.55];
    private const double MinBarHeight = 4;
    private const double MaxBarHeight = 18;

    private readonly ToastPresenter _presenter;
    private readonly IToastPositionStore _positions;
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimerWrapper _autoHide;
    private readonly Rectangle[] _bars;
    private readonly Random _jitter = new();

    // Detached copy of TitleText, used only to measure the title: measuring
    // the live panel of a hidden window gave stale widths.
    private readonly TextBlock _titleMeasure = new() { TextWrapping = TextWrapping.NoWrap };

    private ToastView _view = ToastView.Hidden;
    private PointInt32? _lastPlacedAt;
    private bool _placing;
    private double _level;

    /// <summary>Set by App after the orchestrator is resolved (the orchestrator itself needs the UI).</summary>
    public DictationOrchestrator? Orchestrator { get; set; }

    public DictationToastWindow(ToastPresenter presenter, IToastPositionStore positions)
    {
        _presenter = presenter;
        _positions = positions;

        InitializeComponent();
        AppWindow.SetIcon(AppIcons.App);
        _hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        _autoHide = new DispatcherQueueTimerWrapper(DispatcherQueue, OnAutoHide);
        _bars = [Bar0, Bar1, Bar2, Bar3, Bar4];
        Logo.Source = new BitmapImage(new Uri(AppIcons.LogoPng));

        ConfigureWindow();
        Root.SizeChanged += (_, _) => UpdateDragRegion();
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

        // Clicking or dragging the pill must not pull focus from the user's app.
        WindowMetrics.MakeNonActivating(_hwnd);

        AppWindow.Changed += OnAppWindowChanged;
    }

    /// <summary>Called on the UI thread whenever the presenter's view changes.</summary>
    public void Render(ToastView view)
    {
        _view = view;
        _autoHide.Stop();
        FadeOut.Stop();
        Root.Opacity = 1;

        if (view.IsHidden)
        {
            ListeningPulse.Stop();
            AppWindow.Hide();
            return;
        }

        Apply(view);
        SizeAndPlace(view);

        // Never activate: every state is a status, none needs the keyboard.
        AppWindow.Show(activateWindow: false);

        if (view.AutoHideAfter is { } after)
        {
            _autoHide.Start(after);
        }
    }

    /// <summary>Called on the UI thread with the microphone level while listening.</summary>
    public void RenderLevel(float level)
    {
        if (_view.Tone != ToastTone.Listening)
        {
            return;
        }

        // Speech RMS mostly sits well under 0.3; the square root spreads it
        // across the bar range. Fast attack, slow decay keeps it readable.
        var target = Math.Min(1.0, Math.Sqrt(level) * 2.2);
        _level = Math.Max(target, _level * 0.8);

        for (var i = 0; i < _bars.Length; i++)
        {
            var wobble = 0.85 + _jitter.NextDouble() * 0.3;
            _bars[i].Height = MinBarHeight + (MaxBarHeight - MinBarHeight) * Math.Min(1.0, _level * BarShape[i] * wobble);
        }
    }

    private void Apply(ToastView view)
    {
        AutomationProperties.SetName(Root, view.Title);
        ToolTipService.SetToolTip(Root, view.Detail ?? (view.ShowsText ? null : view.Title));

        var listening = view.Tone == ToastTone.Listening;
        ListeningDot.Visibility = Show(listening);
        LevelBars.Visibility = Show(listening);
        if (listening)
        {
            _level = 0;
            foreach (var bar in _bars)
            {
                bar.Height = MinBarHeight;
            }
            ListeningPulse.Begin();
        }
        else
        {
            ListeningPulse.Stop();
        }

        var processing = view.Tone == ToastTone.Processing;
        Spinner.IsActive = processing;
        Spinner.Visibility = Show(processing);

        ToneIcon.Visibility = Show(view.Tone is ToastTone.Error or ToastTone.Success);
        if (view.Tone == ToastTone.Error)
        {
            ToneIcon.Glyph = ""; // Error
            ToneIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        }
        else if (view.Tone == ToastTone.Success)
        {
            ToneIcon.Glyph = ""; // CheckMark
            ToneIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }

        Logo.Visibility = Show(view.Tone == ToastTone.Neutral && view.Progress is null);

        TitleText.Text = view.Title;
        TitleText.Visibility = Show(view.ShowsText);
        // Leave room for the indicator (16) and the gap (10); past that, trim.
        TitleText.MaxWidth = ToastLayout.MaxWidth - HorizontalPadding - 26
            - (view.Progress is not null ? DownloadBar.Width + 10 : 0);

        DownloadBar.Visibility = Show(view.Progress is not null);
        DownloadBar.IsIndeterminate = view.Progress is { } p && double.IsNaN(p);
        DownloadBar.Value = view.Progress is { } v && !double.IsNaN(v) ? v : 0;
    }

    private void SizeAndPlace(ToastView view)
    {
        var scale = WindowMetrics.ScaleFor(_hwnd);

        var measured = 0d;
        if (view.ShowsText)
        {
            _titleMeasure.FontFamily = TitleText.FontFamily;
            _titleMeasure.FontSize = TitleText.FontSize;
            _titleMeasure.FontWeight = TitleText.FontWeight;
            _titleMeasure.Text = TitleText.Text;
            _titleMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            measured = ToastLayout.ContentWidthFor(view, Math.Min(_titleMeasure.DesiredSize.Width, TitleText.MaxWidth));
        }

        var size = ToastLayout.SizeFor(view, measured, scale);
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

    // The user dragged the pill: remember where, for next time and next launch.
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

    /// <summary>The whole pill drags the window; there are no controls to keep clickable.</summary>
    private void UpdateDragRegion()
    {
        if (Root.XamlRoot is null)
        {
            return;
        }

        var scale = Root.XamlRoot.RasterizationScale;
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Caption,
            [new RectInt32(0, 0, (int)Math.Ceiling(Root.ActualWidth * scale), (int)Math.Ceiling(Root.ActualHeight * scale))]);
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
        if (_view.AutoHideAfter is null)
        {
            // A new state arrived during the fade; it owns the window now.
            return;
        }

        if (_view.Tone == ToastTone.Error && Orchestrator is not null)
        {
            // Back to idle; the orchestrator hides the pill.
            Orchestrator.DismissError();
        }
        else
        {
            _presenter.Hide();
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
