using System.Collections.ObjectModel;
using System.Drawing;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

// Resize window mode: choosing, measuring, resizing and restoring another app's window.
public sealed partial class MainViewModel
{
    private IReadOnlyList<WindowInfo> _allWindows = [];
    private WindowInfo? _targetWindow;
    private WindowInfo? _followedWindow;
    private WindowObservation? _lastTrackedObservation;
    private Rectangle? _lastVisibleTrackedFrame;
    private WindowArea _area = WindowArea.Client;
    private string _windowFilter = string.Empty;

    public ObservableCollection<WindowItem> Windows { get; } = [];

    public IReadOnlyList<ChoiceItem> MeasureOptions { get; }

    public IReadOnlyList<ChoiceItem> PlacementOptions { get; }

    public string WindowFilter
    {
        get => _windowFilter;
        set
        {
            if (SetProperty(ref _windowFilter, value ?? string.Empty)) ApplyWindowFilter();
        }
    }

    public bool HasNoWindows => Windows.Count == 0;

    public bool HasTargetWindow => _targetWindow is not null;

    public bool HasRememberedAppSize => _targetWindow is not null && FindRememberedSize(_targetWindow.ProcessName) is not null;

    public string RememberedAppSizeText => _targetWindow is { } window && FindRememberedSize(window.ProcessName) is { } saved
        ? $"Remembered for {window.ProcessName}: {saved.Width} × {saved.Height} px " +
          $"{(saved.Area == WindowArea.Client ? "client area" : "whole window")}. Applied when you select this app."
        : string.Empty;

    public bool CanRestore => _windowService.CanRestore(_targetWindow);

    public string MeasureNote => _area == WindowArea.Client
        ? $"The app’s content area will be exactly {_width} × {_height} px. The title bar and borders sit outside it."
        : $"The visible window, title bar included, will be {_width} × {_height} px. Invisible resize borders aren’t counted.";

    private string AreaName => _area == WindowArea.Client ? "client area" : "window";

    private void RefreshWindows()
    {
        var previous = _targetWindow;
        _allWindows = _windowService.GetWindows();
        _targetWindow = previous is null ? null : _allWindows.FirstOrDefault(w => w.IsSameWindow(previous));
        ApplyWindowFilter();
        NotifyResizeState();
        PrimaryCommand.NotifyCanExecuteChanged();
        if (_allWindows.Count == 0) SetStatus("No app windows found. Open an app, then refresh the list.");
    }

    private void ApplyWindowFilter()
    {
        string filter = _windowFilter.Trim();
        Windows.Clear();
        foreach (var window in _allWindows)
        {
            bool matches = filter.Length == 0
                || window.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                || window.ProcessName.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
            if (!matches) continue;
            Windows.Add(new WindowItem(window, () => SelectWindowAsync(window)) { IsSelected = window.IsSameWindow(_targetWindow) });
        }
        OnPropertyChanged(nameof(HasNoWindows));
    }

    private async Task SelectWindowAsync(WindowInfo window)
    {
        if (IsBusy) return;
        SetTarget(window);
        if (IsResizeMode && FindRememberedSize(window.ProcessName) is { } saved)
        {
            if (!PrepareRememberedSize(window, saved)) return;
            await RunBusyAsync(() => ResizeTargetAsync(rememberSize: false));
        }
        else if (_anchor is null) KeepWindowPosition();
        else UpdateFrame();
    }

    private void SetTarget(WindowInfo? window)
    {
        if (_followedWindow is not null && !_followedWindow.IsSameWindow(window))
        {
            StopFollowingWindow();
            if (IsResizeMode) _overlay.Hide();
            _activeFrame = null;
        }
        _targetWindow = window;
        SelectOnly(Windows, w => w.Window.IsSameWindow(window));
        PrimaryCommand.NotifyCanExecuteChanged();
        NotifyResizeState();
    }

    private void SelectArea(WindowArea area)
    {
        _area = area;
        SelectOnly(MeasureOptions, o => o.Key == area.ToString());
        OnPropertyChanged(nameof(MeasureNote));
        OnPropertyChanged(nameof(StageEyebrow));
        if (_anchor is null && _targetWindow is not null) KeepWindowPosition();
        else UpdateFrame();
    }

    /// <summary>Uses the selected window's current capture position instead of an anchor.</summary>
    private void KeepWindowPosition()
    {
        if (_targetWindow is not { } window)
        {
            SetStatus("Choose a window first; ExactFrame will keep its current position.", warning: true);
            return;
        }

        if (window.IsMinimized)
        {
            // A minimized window has no position on screen, so there's nothing to keep.
            _anchor = FrameAnchor.Center;
            UpdateFrame($"{window.ProcessName} is minimized, so it has no position to keep. It will be restored and centered.");
            return;
        }

        Guard(() =>
        {
            var measured = _windowService.Measure(window, _area);
            _anchor = null;
            _x = measured.X;
            _y = measured.Y;
            UpdateFrame($"Keeping {window.ProcessName} at {measured.X}, {measured.Y}.");
        });
    }

    private async Task PickWindowAsync()
    {
        SetStatus("Click any app window to select it. Right-click or press Esc to cancel.");
        WindowInfo? picked;
        try
        {
            picked = await _windowPicker.PickAsync(_lifetime.Token);
        }
        catch (Exception ex) when (IsExpected(ex) || ex is OperationCanceledException)
        {
            if (ex is not OperationCanceledException) ReportError(ex);
            return;
        }

        if (picked is null)
        {
            SetStatus("Window picking canceled.");
            return;
        }

        WindowFilter = string.Empty;
        Guard(RefreshWindows);
        await SelectWindowAsync(_allWindows.FirstOrDefault(w => w.IsSameWindow(picked)) ?? picked);
    }

    private RememberedAppSize? FindRememberedSize(string processName) =>
        _settings.RememberedAppSizes.LastOrDefault(saved =>
            string.Equals(saved.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

    private bool PrepareRememberedSize(WindowInfo window, RememberedAppSize saved)
    {
        if (saved.Width is < MinSize or > MaxSize || saved.Height is < MinSize or > MaxSize ||
            !Enum.IsDefined(saved.Area))
        {
            SetStatus($"The remembered size for {window.ProcessName} is invalid. Forget it or choose another size.", warning: true);
            return false;
        }

        var display = _displays
            .Select(item => (Display: item, Area: Rectangle.Intersect(item.Bounds, window.Bounds)))
            .Where(item => item.Area.Width > 0 && item.Area.Height > 0)
            .OrderByDescending(item => (long)item.Area.Width * item.Area.Height)
            .Select(item => item.Display)
            .FirstOrDefault() ?? _display;
        if (display is null)
        {
            SetStatus("No display is available for the remembered app size.", warning: true);
            return false;
        }

        var size = new Size(saved.Width, saved.Height);
        var centered = FrameGeometry.Place(size, display.UsableArea(_keepClear), FrameAnchor.Center);
        Rectangle desired = centered;
        bool keepPosition = false;
        if (!window.IsMinimized)
        {
            try
            {
                var current = _windowService.Measure(window, saved.Area);
                var atCurrentPosition = new Rectangle(current.Location, size);
                if (FrameGeometry.CheckFit(atCurrentPosition, display.Bounds, display.WorkArea, _keepClear).IsFit)
                {
                    desired = atCurrentPosition;
                    keepPosition = true;
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                // The window may have become minimized since the list was refreshed; use the display center.
            }
        }

        _display = display;
        _area = saved.Area;
        _width = saved.Width;
        _height = saved.Height;
        _ratio = (double)_width / _height;
        _preset = ResolutionPreset.Match(_width, _height);
        _anchor = keepPosition ? null : FrameAnchor.Center;
        _x = desired.X;
        _y = desired.Y;
        SelectOnly(Displays, item => item.DeviceName == display.DeviceName);
        SelectOnly(MeasureOptions, item => item.Key == saved.Area.ToString());
        SelectOnly(Presets, item => item.Key == _preset.Id);
        OnPropertyChanged(nameof(WidthValue));
        OnPropertyChanged(nameof(HeightValue));
        OnPropertyChanged(nameof(LogicalSizeNote));
        OnPropertyChanged(nameof(MeasureNote));
        OnPropertyChanged(nameof(StageEyebrow));
        UpdateFrame();

        if (FrameGeometry.CheckFit(desired, display.Bounds, display.WorkArea, _keepClear).IsFit) return true;
        SetStatus($"The remembered {saved.Width} × {saved.Height} size for {window.ProcessName} does not fit on {display.Title}. " +
                  "Choose a larger display or a smaller size.", warning: true);
        return false;
    }

    private void RememberAppSize(WindowInfo window, Size actual)
    {
        if (actual.Width is < MinSize or > MaxSize || actual.Height is < MinSize or > MaxSize ||
            string.IsNullOrWhiteSpace(window.ProcessName)) return;

        _settings.RememberedAppSizes.RemoveAll(saved =>
            string.Equals(saved.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase));
        _settings.RememberedAppSizes.Add(new RememberedAppSize(window.ProcessName, actual.Width, actual.Height, _area));
        NotifyResizeState();
        SaveSettings();
    }

    private void ForgetAppSize()
    {
        if (_targetWindow is not { } window) return;
        int removed = _settings.RememberedAppSizes.RemoveAll(saved =>
            string.Equals(saved.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return;
        NotifyResizeState();
        SetStatus($"Forgot the size for {window.ProcessName}. Resize it again to save a new size.");
        SaveSettings();
    }

    private async Task ResizeTargetAsync(bool rememberSize)
    {
        var window = _targetWindow ?? throw new InvalidOperationException("Choose an open app window first.");
        if (_display is null) throw new InvalidOperationException("No display is available.");
        StopFollowingWindow();

        var desired = RequestedFrame;
        var fit = FrameGeometry.CheckFit(desired, _display.Bounds, _display.WorkArea, _keepClear);
        if (!fit.IsFit)
        {
            throw new InvalidOperationException(
                $"The {desired.Width} × {desired.Height} area at ({desired.X}, {desired.Y}) doesn’t fit: " +
                $"{fit.Message.ToLowerInvariant()}. Center it or choose a smaller size.");
        }

        SetStatus("Resizing and measuring the selected window…");
        var result = await _windowService.ResizeAsync(window, desired, _area, _lifetime.Token);
        var actual = result.Actual;

        _activeFrame = actual;
        ShowOutline(actual, validate: false);
        Guard(RefreshWindows); // Picks up the window's new size and state.
        NotifyFrame();

        string restored = result.WasRestored ? $"Restored {window.ProcessName} to a normal window first. " : string.Empty;
        string exclusion = _hideFromRecorders && !_overlay.CaptureExclusionApplied
            ? " Hide the outline before recording; capture exclusion was unavailable."
            : string.Empty;
        if (result.Exact)
        {
            SetStatus(restored + $"Verified {actual.Width} × {actual.Height} px at ({actual.X}, {actual.Y}). " +
                      "The outline follows the app when it moves. Restore original size is available for this session." +
                      (rememberSize ? " Size remembered for this app." : " Remembered app size applied.") + exclusion);
        }
        else
        {
            SetStatus(restored + $"Requested {desired.Width} × {desired.Height} at ({desired.X}, {desired.Y}); " +
                      $"the app accepted {actual.Width} × {actual.Height} at ({actual.X}, {actual.Y}). " +
                      "The outline follows its actual bounds; this app may enforce size or position limits." +
                      (rememberSize ? " Accepted size remembered for this app." : " Remembered app size applied.") + exclusion,
                      warning: true);
        }
        StartFollowingWindow(window);
        if (rememberSize) RememberAppSize(window, actual.Size);
    }

    private async Task RestoreTargetAsync()
    {
        var window = _targetWindow ?? throw new InvalidOperationException("Choose an open app window first.");
        StopFollowingWindow();
        await _windowService.RestoreAsync(window, _lifetime.Token);
        _activeFrame = null;
        if (_overlay.IsVisible) _overlay.Hide();
        Guard(RefreshWindows);
        NotifyFrame();
        SetStatus("The app’s original size, position and minimized or maximized state were restored.");
    }

    private void StartFollowingWindow(WindowInfo window)
    {
        _followedWindow = window;
        _lastVisibleTrackedFrame = _activeFrame;
        try
        {
            _windowService.StartTracking(window, _area);
            if (_followedWindow is not null) _overlay.SetLocked(true);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StopFollowingWindow();
            SetStatus($"The app was resized, but its outline could not follow it: {ex.Message}", warning: true);
        }
    }

    private void StopFollowingWindow()
    {
        if (_followedWindow is null) return;
        _windowService.StopTracking();
        _followedWindow = null;
        _lastTrackedObservation = null;
        _lastVisibleTrackedFrame = null;
        _overlay.SetLocked(_clickThrough);
    }

    private void OnTrackedWindowChanged(object? sender, WindowObservation observation)
    {
        if (_disposed || _followedWindow is null || !IsResizeMode) return;
        _lastTrackedObservation = observation;
        try
        {
            ApplyTrackedObservation(observation);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StopFollowingWindow();
            _overlay.Hide();
            ReportError(ex);
        }
    }

    private void RefreshTrackedOutline()
    {
        if (_lastTrackedObservation is { } observation)
            ApplyTrackedObservation(observation, force: true);
        else if (_activeFrame is { } frame)
            ApplyTrackedObservation(new WindowObservation(WindowObservationKind.Visible, frame), force: true);
    }

    private void ApplyTrackedObservation(WindowObservation observation, bool force = false)
    {
        if (_followedWindow is not { } window) return;
        if (observation.Kind == WindowObservationKind.Closed)
        {
            StopFollowingWindow();
            _overlay.Hide();
            _activeFrame = null;
            Guard(RefreshWindows);
            NotifyFrame();
            SetStatus($"{window.ProcessName} closed. Choose another window to resize.", warning: true);
            return;
        }

        if (observation.Kind == WindowObservationKind.Unavailable)
        {
            _overlay.Hide();
            _activeFrame = null;
            NotifyFrame();
            SetStatus($"{window.ProcessName} is hidden or minimized. Its outline will return when it is visible.");
            return;
        }

        var bounds = observation.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        bool moved = _lastVisibleTrackedFrame is { } previous && previous.Location != bounds.Location;
        bool changed = force || _activeFrame != bounds || !_overlay.IsVisible;
        _activeFrame = bounds;
        _lastVisibleTrackedFrame = bounds;
        if (moved)
        {
            _anchor = null;
            _x = bounds.X;
            _y = bounds.Y;
        }

        var display = _displays
            .Select(item => (Display: item, Area: Rectangle.Intersect(item.Bounds, bounds)))
            .Where(item => item.Area.Width > 0 && item.Area.Height > 0)
            .OrderByDescending(item => (long)item.Area.Width * item.Area.Height)
            .Select(item => item.Display)
            .FirstOrDefault();
        if (display is not null && display != _display)
        {
            _display = display;
            SelectOnly(Displays, item => item.DeviceName == display.DeviceName);
            OnPropertyChanged(nameof(LogicalSizeNote));
            changed = true;
        }

        if (!changed) return;
        if (_display is not null) _overlay.Show(bounds, _display, _keepClear, []);
        NotifyFrame();
        bool exclusionFailed = _hideFromRecorders && !_overlay.CaptureExclusionApplied;
        SetStatus(exclusionFailed
            ? "The outline is following the app, but capture exclusion is unavailable. Hide it before recording."
            : $"Following {window.ProcessName} at {bounds.X}, {bounds.Y} ({bounds.Width} × {bounds.Height} px).",
            warning: exclusionFailed);
    }

    private void NotifyResizeState()
    {
        OnPropertyChanged(nameof(HasTargetWindow));
        OnPropertyChanged(nameof(HasRememberedAppSize));
        OnPropertyChanged(nameof(RememberedAppSizeText));
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(MeasureNote));
        RestoreCommand.NotifyCanExecuteChanged();
        ForgetAppSizeCommand.NotifyCanExecuteChanged();
        NotifyStage();
        NotifyFooter();
    }
}
