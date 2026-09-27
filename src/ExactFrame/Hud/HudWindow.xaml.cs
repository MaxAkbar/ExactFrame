using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ExactFrame.Core.Services;
using ExactFrame.Core.ViewModels;
using ExactFrame.Interop;
using ExactFrame.Native;
using Windows.Graphics;
using DPoint = System.Drawing.Point;
using DRect = System.Drawing.Rectangle;

namespace ExactFrame.Hud;

/// <summary>
/// The floating control bar next to the on-screen outline. It never takes focus (so the app being recorded
/// keeps it), is excluded from capture along with the outline, and has three states:
/// full controls, idle (size readout only) and click-through (unlock hint only).
/// </summary>
public sealed partial class HudWindow : Window
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(3);
    private const int GapDip = 10;
    private const int PointerReach = 48;
    private const double DefaultWidthDip = 560;
    private const double HeightDip = 48;

    private enum HudState
    {
        Full,
        Idle,
        Locked
    }

    private readonly IOutlineOverlay _overlay;
    private readonly DispatcherQueueTimer _timer;
    private readonly nint _hwnd;
    private HudState _state = HudState.Full;
    private DateTime _lastNear = DateTime.UtcNow;
    private bool _shown;
    private bool? _excluded;

    public HudWindow(MainViewModel viewModel, IOutlineOverlay overlay)
    {
        ViewModel = viewModel;
        _overlay = overlay;
        InitializeComponent();

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        long exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle,
            (nint)(exStyle | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
        int corner = NativeMethods.DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DwmwaWindowCornerPreference, ref corner, sizeof(int));

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += OnTimerTick;

        _overlay.VisibilityChanged += OnOverlayChanged;
        _overlay.FrameMoved += OnOverlayChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Pill.SizeChanged += (_, _) => Place();
        Root.Loaded += (_, _) =>
        {
            if (Root.XamlRoot is { } xamlRoot) xamlRoot.Changed += (_, _) => Place();
        };
        Closed += OnClosed;
    }

    public event EventHandler? OpenMainRequested;

    public MainViewModel ViewModel { get; }

    private void OnOverlayChanged(object? sender, EventArgs e) => Refresh();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ShowHud):
            case nameof(MainViewModel.IsClickThrough):
            case nameof(MainViewModel.IsOutlineMode):
            case nameof(MainViewModel.HideFromRecorders):
            case nameof(MainViewModel.SizeText):
            case "":
            case null:
                Refresh();
                break;
        }
    }

    private void Refresh()
    {
        if (!_overlay.IsVisible || !ViewModel.ShowHud)
        {
            HideBar();
            return;
        }

        ApplyCaptureExclusion();
        if (ViewModel.IsClickThrough && ViewModel.IsOutlineMode) SetState(HudState.Locked);
        else if (_state == HudState.Locked || !_shown) SetState(HudState.Full);

        Place();
        if (!_shown)
        {
            _lastNear = DateTime.UtcNow;
            AppWindow.Show(false);
            _shown = true;
            _timer.Start();
        }

        // Stay above the outline's windows, which are also topmost.
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    private void HideBar()
    {
        if (!_shown) return;
        AppWindow.Hide();
        _shown = false;
        _timer.Stop();
    }

    private void SetState(HudState state)
    {
        if (_state == state && _shown) return;
        _state = state;
        FullControls.Visibility = state == HudState.Full ? Visibility.Visible : Visibility.Collapsed;
        LockedHint.Visibility = state == HudState.Locked ? Visibility.Visible : Visibility.Collapsed;
        Place();
    }

    /// <summary>Sits centered under the frame; moves above it, then inside it, when there's no room.</summary>
    private void Place()
    {
        if (!_overlay.IsVisible || _overlay.Display is not { } display) return;

        double scale = Root.XamlRoot?.RasterizationScale ?? display.Scale;
        Pill.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double widthDip = Pill.DesiredSize.Width > 0 ? Pill.DesiredSize.Width : DefaultWidthDip;
        int width = (int)Math.Ceiling(widthDip * scale);
        int height = (int)Math.Ceiling(HeightDip * scale);

        var frame = _overlay.Frame;
        var work = display.WorkArea;
        int gap = (int)Math.Round(GapDip * display.Scale) + FrameBorderWindow.GrabMargin;
        int labelSpace = (int)Math.Round(24 * display.Scale);

        int x = frame.Left + (frame.Width - width) / 2;
        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - width - gap));

        int y;
        if (frame.Bottom + gap + height <= work.Bottom) y = frame.Bottom + gap;
        else if (frame.Top - gap - labelSpace - height >= work.Top) y = frame.Top - gap - labelSpace - height;
        else y = frame.Bottom - height - gap;

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_shown || _state == HudState.Locked) return;

        NativeMethods.GetCursorPos(out var point);
        var cursor = new DPoint(point.X, point.Y);
        var bar = new DRect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        var frame = _overlay.Frame;

        bool nearBar = DRect.Inflate(bar, PointerReach, PointerReach).Contains(cursor);
        bool nearEdge = DRect.Inflate(frame, PointerReach, PointerReach).Contains(cursor) &&
                        !DRect.Inflate(frame, -PointerReach, -PointerReach).Contains(cursor);

        if (nearBar || nearEdge)
        {
            _lastNear = DateTime.UtcNow;
            if (_state == HudState.Idle) SetState(HudState.Full);
        }
        else if (_state == HudState.Full && DateTime.UtcNow - _lastNear > IdleDelay)
        {
            SetState(HudState.Idle);
        }
    }

    private void ApplyCaptureExclusion()
    {
        bool exclude = ViewModel.HideFromRecorders;
        if (_excluded == exclude) return;
        NativeMethods.SetWindowDisplayAffinity(_hwnd, exclude ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
        _excluded = exclude;
    }

    private void OnOpenMainClick(object sender, RoutedEventArgs e) => OpenMainRequested?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _timer.Stop();
        _overlay.VisibilityChanged -= OnOverlayChanged;
        _overlay.FrameMoved -= OnOverlayChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }
}
