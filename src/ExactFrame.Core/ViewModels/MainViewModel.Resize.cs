using System.Collections.ObjectModel;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

// Resize window mode: choosing, measuring, resizing and restoring another app's window.
public sealed partial class MainViewModel
{
    private IReadOnlyList<WindowInfo> _allWindows = [];
    private WindowInfo? _targetWindow;
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
            Windows.Add(new WindowItem(window, () => SelectWindow(window)) { IsSelected = window.IsSameWindow(_targetWindow) });
        }
        OnPropertyChanged(nameof(HasNoWindows));
    }

    private void SelectWindow(WindowInfo window)
    {
        SetTarget(window);
        if (_anchor is null) KeepWindowPosition();
        else UpdateFrame();
    }

    private void SetTarget(WindowInfo? window)
    {
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
        SelectWindow(_allWindows.FirstOrDefault(w => w.IsSameWindow(picked)) ?? picked);
    }

    private async Task ResizeTargetAsync()
    {
        var window = _targetWindow ?? throw new InvalidOperationException("Choose an open app window first.");
        if (_display is null) throw new InvalidOperationException("No display is available.");

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
                      "Restore original size is available for this session." + exclusion);
        }
        else
        {
            SetStatus(restored + $"Requested {desired.Width} × {desired.Height} at ({desired.X}, {desired.Y}); " +
                      $"the app accepted {actual.Width} × {actual.Height} at ({actual.X}, {actual.Y}). " +
                      "The outline shows its actual bounds; this app may enforce size or position limits." + exclusion,
                      warning: true);
        }
    }

    private async Task RestoreTargetAsync()
    {
        var window = _targetWindow ?? throw new InvalidOperationException("Choose an open app window first.");
        await _windowService.RestoreAsync(window, _lifetime.Token);
        _activeFrame = null;
        if (_overlay.IsVisible) _overlay.Hide();
        Guard(RefreshWindows);
        NotifyFrame();
        SetStatus("The app’s original size, position and minimized or maximized state were restored.");
    }

    private void NotifyResizeState()
    {
        OnPropertyChanged(nameof(HasTargetWindow));
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(MeasureNote));
        RestoreCommand.NotifyCanExecuteChanged();
        NotifyStage();
        NotifyFooter();
    }
}
