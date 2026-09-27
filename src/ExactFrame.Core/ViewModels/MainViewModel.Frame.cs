using System.Collections.ObjectModel;
using System.Drawing;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

// Size, position and display.
public sealed partial class MainViewModel
{
    public const int MinSize = 100;
    public const int MaxSize = 16384;
    private const int MaxCoordinate = 100_000;

    private IReadOnlyList<DisplayInfo> _displays = [];
    private DisplayInfo? _display;
    private ResolutionPreset _preset = ResolutionPreset.Match(1920, 1080);
    private int _width = 1920;
    private int _height = 1080;
    private double _ratio = 16d / 9;
    private bool _aspectLocked = true;
    private FrameAnchor? _anchor = FrameAnchor.Center;
    private int _x;
    private int _y;
    private bool _keepClear;

    /// <summary>The measured rectangle after a drag or a window resize; <c>null</c> means "use the request".</summary>
    private Rectangle? _activeFrame;

    public ObservableCollection<ChoiceItem> Presets { get; }

    public IReadOnlyList<AnchorItem> Anchors { get; }

    public ObservableCollection<DisplayItem> Displays { get; } = [];

    public double WidthValue
    {
        get => _width;
        set
        {
            if (!TryPixels(value, out int width) || width == _width) return;
            int height = _aspectLocked ? Clamp(width / _ratio) : _height;
            ApplySize(width, height, updateRatio: !_aspectLocked);
        }
    }

    public double HeightValue
    {
        get => _height;
        set
        {
            if (!TryPixels(value, out int height) || height == _height) return;
            int width = _aspectLocked ? Clamp(height * _ratio) : _width;
            ApplySize(width, height, updateRatio: !_aspectLocked);
        }
    }

    public bool IsAspectLocked
    {
        get => _aspectLocked;
        set
        {
            if (!SetProperty(ref _aspectLocked, value)) return;
            _ratio = (double)_width / _height;
            NotifySliderRanges();
        }
    }

    public string LogicalSizeNote => _display is null
        ? string.Empty
        : $"{Math.Round(_width / _display.Scale)} × {Math.Round(_height / _display.Scale)} logical px at {_display.ScalePercent}% scaling";

    public double XValue
    {
        get => CurrentFrame.X;
        set
        {
            if (!TryCoordinate(value, out int x) || x == CurrentFrame.X) return;
            int y = CurrentFrame.Y;
            _anchor = null;
            _x = x;
            _y = y;
            UpdateFrame();
        }
    }

    public double YValue
    {
        get => CurrentFrame.Y;
        set
        {
            if (!TryCoordinate(value, out int y) || y == CurrentFrame.Y) return;
            int x = CurrentFrame.X;
            _anchor = null;
            _x = x;
            _y = y;
            UpdateFrame();
        }
    }

    public string AnchorName => _anchor?.DisplayName() ?? "Custom position";

    public bool KeepClearOfTaskbar
    {
        get => _keepClear;
        set
        {
            if (!SetProperty(ref _keepClear, value)) return;
            UpdateFrame();
        }
    }

    /// <summary>The rectangle the user asked for, in physical pixels.</summary>
    public Rectangle RequestedFrame => _anchor is { } anchor && _display is not null
        ? FrameGeometry.Place(new Size(_width, _height), _display.UsableArea(_keepClear), anchor)
        : new Rectangle(_x, _y, _width, _height);

    /// <summary>The measured rectangle when there is one, otherwise the request.</summary>
    public Rectangle CurrentFrame => _activeFrame ?? RequestedFrame;

    private FitResult CurrentFit => _display is null
        ? new FitResult(false, FitProblem.OutsideDisplay, "No display found")
        : FrameGeometry.CheckFit(CurrentFrame, _display.Bounds, _display.WorkArea, _keepClear);

    private void SelectPreset(ResolutionPreset preset)
    {
        if (preset.IsCustom)
        {
            _preset = preset;
            IsAspectLocked = false;
            SelectOnly(Presets, p => p.Key == preset.Id);
            SetStatus("Enter your own width and height.");
            return;
        }

        _anchor ??= FrameAnchor.Center;
        ApplySize(preset.Width, preset.Height, updateRatio: true);
    }

    private void SelectAnchor(FrameAnchor anchor)
    {
        _anchor = anchor;
        UpdateFrame();
    }

    private void ApplySize(int width, int height, bool updateRatio)
    {
        _width = width;
        _height = height;
        if (updateRatio) _ratio = (double)width / height;
        _preset = ResolutionPreset.Match(width, height);
        SelectOnly(Presets, p => p.Key == _preset.Id);
        OnPropertyChanged(nameof(WidthValue));
        OnPropertyChanged(nameof(HeightValue));
        OnPropertyChanged(nameof(LogicalSizeNote));
        OnPropertyChanged(nameof(MeasureNote));
        UpdateFrame();
    }

    private void RefreshDisplays()
    {
        string? previous = _display?.DeviceName;
        _displays = _displayService.GetDisplays();
        _display = _displays.FirstOrDefault(d => d.DeviceName == previous)
            ?? _displays.FirstOrDefault(d => d.IsPrimary)
            ?? _displays.FirstOrDefault();

        Displays.Clear();
        double widest = _displays.Count == 0 ? 1 : _displays.Max(d => Math.Max(d.Bounds.Width, d.Bounds.Height * 26d / 16));
        double glyphScale = 26d / widest;
        foreach (var display in _displays)
        {
            Displays.Add(new DisplayItem(display, glyphScale, () => SelectDisplay(display))
            {
                IsSelected = display.DeviceName == _display?.DeviceName
            });
        }
        OnPropertyChanged(nameof(LogicalSizeNote));
    }

    private void SelectDisplay(DisplayInfo display)
    {
        _display = display;
        SelectOnly(Displays, d => d.DeviceName == display.DeviceName);
        _anchor ??= FrameAnchor.Center;
        OnPropertyChanged(nameof(LogicalSizeNote));
        UpdateFrame();
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => Guard(() =>
    {
        RefreshDisplays();
        UpdateFrame("Display settings changed, so the frame was recalculated.");
    });

    /// <summary>Recomputes the requested frame after any edit, keeps a visible outline in sync and refreshes the UI.</summary>
    private void UpdateFrame(string? message = null)
    {
        _activeFrame = null;
        if (_anchor is not null && _display is not null)
        {
            var frame = RequestedFrame;
            _x = frame.X;
            _y = frame.Y;
        }

        SyncOverlayToRequest();
        NotifyFrame();
        if (!_initialized) return;

        var fit = CurrentFit;
        if (!fit.IsFit && _display is not null)
            SetStatus($"{fit.Message} on {_display.Title}. Choose a smaller size or another position.", warning: true);
        else
            SetStatus(message ?? Summary());
    }

    private void SyncOverlayToRequest()
    {
        if (!_overlay.IsVisible || _display is null) return;

        // In Resize window mode the outline marks the measured window; a new request invalidates it.
        if (IsResizeMode)
        {
            _overlay.Hide();
            return;
        }

        var frame = RequestedFrame;
        if (FrameGeometry.CheckFit(frame, _display.Bounds, _display.WorkArea, _keepClear).IsFit)
            _overlay.Show(frame, _display, _keepClear);
        else
            _overlay.Hide();
    }

    private string Summary()
    {
        if (_display is null) return "No display found.";
        var frame = CurrentFrame;
        if (IsResizeMode)
        {
            return _targetWindow is null
                ? "Choose a window to resize."
                : $"{_targetWindow.ProcessName} → {frame.Width} × {frame.Height} {AreaName} on {_display.Title}";
        }
        return $"{AnchorName} on {_display.Title} · {frame.X}, {frame.Y}";
    }

    private void NotifyFrame()
    {
        SelectOnly(Anchors, a => a.Anchor == _anchor);
        SelectOnly(PlacementOptions, o => o.Key == (_anchor == FrameAnchor.Center ? "center" : _anchor is null ? "keep" : string.Empty));
        OnPropertyChanged(nameof(XValue));
        OnPropertyChanged(nameof(YValue));
        OnPropertyChanged(nameof(AnchorName));
        NotifySliderRanges();
        NotifyStage();
        NotifyFooter();
        NotifyProfileHeader();
    }

    /// <summary>Loads a saved frame (a profile or the last session) without announcing it.</summary>
    private void ApplyFrameState(FrameProfile state)
    {
        _mode = state.Mode;
        _display = (state.DisplayDeviceName is null ? null : _displays.FirstOrDefault(d => d.DeviceName == state.DisplayDeviceName))
            ?? _displays.FirstOrDefault(d => d.IsPrimary)
            ?? _displays.FirstOrDefault();
        SelectOnly(Displays, d => d.DeviceName == _display?.DeviceName);

        _keepClear = state.KeepClearOfTaskbar;
        _width = Math.Clamp(state.Width, MinSize, MaxSize);
        _height = Math.Clamp(state.Height, MinSize, MaxSize);
        _ratio = (double)_width / _height;
        _preset = ResolutionPreset.Match(_width, _height);
        _anchor = state.Anchor;
        _x = state.X;
        _y = state.Y;
        if (state.Mode == FrameMode.Resize) _area = state.Area;

        if (state.Mode == FrameMode.Resize && !string.IsNullOrEmpty(state.TargetProcessName))
        {
            var match = _allWindows.FirstOrDefault(w =>
                string.Equals(w.ProcessName, state.TargetProcessName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) SetTarget(match);
        }

        SyncSelections();
        PrimaryCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Captures the current setup as a profile. Positions are only stored when not anchored.</summary>
    private FrameProfile Snapshot(string id, string name) => Normalize(new FrameProfile
    {
        Id = id,
        Name = name,
        Mode = _mode,
        Width = _width,
        Height = _height,
        Anchor = _anchor,
        X = _x,
        Y = _y,
        DisplayDeviceName = _display is null || _display.IsPrimary ? null : _display.DeviceName,
        KeepClearOfTaskbar = _keepClear,
        Area = _area,
        TargetProcessName = _targetWindow?.ProcessName
    });

    private static FrameProfile Normalize(FrameProfile profile) => profile with
    {
        X = profile.Anchor is null ? profile.X : 0,
        Y = profile.Anchor is null ? profile.Y : 0,
        Area = profile.Mode == FrameMode.Resize ? profile.Area : WindowArea.Client,
        TargetProcessName = profile.Mode == FrameMode.Resize ? profile.TargetProcessName : null
    };

    private void SyncSelections()
    {
        SelectOnly(Presets, p => p.Key == _preset.Id);
        SelectOnly(Anchors, a => a.Anchor == _anchor);
        SelectOnly(MeasureOptions, o => o.Key == _area.ToString());
        SelectOnly(Swatches, s => s.Color == _style.Color);
        SelectOnly(Thicknesses, t => t.Key == _style.Thickness.ToString());
        SelectOnly(Lines, l => l.Key == _style.Line.ToString());
    }

    private static bool TryPixels(double value, out int pixels)
    {
        pixels = 0;
        if (double.IsNaN(value) || double.IsInfinity(value)) return false;
        pixels = Clamp(value);
        return true;
    }

    private static int Clamp(double value) => (int)Math.Clamp(Math.Round(value), MinSize, MaxSize);

    private static bool TryCoordinate(double value, out int coordinate)
    {
        coordinate = 0;
        if (double.IsNaN(value) || double.IsInfinity(value)) return false;
        coordinate = (int)Math.Clamp(Math.Round(value), -MaxCoordinate, MaxCoordinate);
        return true;
    }
}
