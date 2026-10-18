using System.Drawing;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record EditSnapshot(Rectangle Frame, Rectangle? ActiveFrame, int Width, int Height, int X, int Y,
        FrameAnchor? Anchor, double Ratio, bool AspectLocked, DisplayInfo Display, ResolutionPreset Preset);
    private EditSnapshot? _frameEdit;
    private Rectangle _lastEditedFrame;
    private IReadOnlyList<Rectangle> _frameSnapTargets = [];
    private CancellationTokenSource? _framePickCancellation;
    private string _frameEditAnnouncement = string.Empty;
    private int _frameEditRevision;

    public int FrameEditRevision => _frameEditRevision;

    public bool CanEditFrame => IsOutlineMode && !IsBusy && !IsClickThrough && _display is not null;
    public double? FrameEditAspectRatio => _aspectLocked ? _ratio : null;
    public Rectangle SelectedFrameArea => _display?.UsableArea(_keepClear) ?? Rectangle.Empty;
    public IReadOnlyList<Rectangle> FrameSnapTargets => _frameSnapTargets;
    public string FrameEditorHint => !IsOutlineMode ? "Window size is measured after you select Resize window."
        : IsClickThrough ? "Turn click-through off to edit the frame."
        : (_frameEditAnnouncement.Length == 0 ? string.Empty : _frameEditAnnouncement + " ") +
          "Drag to move, use handles to resize, or drag outside to draw. Arrows: 1 px · Shift: 10 px · Ctrl: resize.";

    public bool SnapToEdges
    {
        get => _settings.SnapToEdges;
        set
        {
            if (_settings.SnapToEdges == value) return;
            _settings.SnapToEdges = value;
            SaveSettings();
            OnPropertyChanged();
            ConfigureFrameEditing();
        }
    }

    public void BeginFrameInteraction()
    {
        if (!CanEditFrame || _frameEdit is not null || _display is null) return;
        _frameEdit = new(CurrentFrame, _activeFrame, _width, _height, _x, _y, _anchor, _ratio, _aspectLocked, _display, _preset);
        _lastEditedFrame = CurrentFrame;
        RefreshSnapTargets();
        ConfigureFrameEditing();
    }

    /// <summary>Adopts the whole requested rectangle atomically; never resizes another application.</summary>
    public void SetFrameFromInteraction(Rectangle frame, bool freeform = false)
    {
        if (!CanEditFrame || !IsEditableFrame(frame, SelectedFrameArea)) return;
        _lastEditedFrame = frame;
        AdoptInteractiveFrame(frame, freeform);
    }

    public void FinishFrameInteraction(bool cancel = false)
    {
        if (_frameEdit is not { } original) return;
        _frameEdit = null;
        if (cancel) _overlay.CancelFrameInteraction();
        // A preset, mode or display change belongs to the newer action, not the canceled drag.
        if (cancel && IsOutlineMode && _display?.DeviceName == original.Display.DeviceName && CurrentFrame == _lastEditedFrame)
        {
            _width = original.Width; _height = original.Height; _x = original.X; _y = original.Y;
            _anchor = original.Anchor; _ratio = original.Ratio; _aspectLocked = original.AspectLocked;
            _activeFrame = original.ActiveFrame; _preset = original.Preset;
            NotifyEditedFrame();
            _frameEditAnnouncement = "Frame edit canceled.";
        }
        else if (!cancel)
        {
            var frame = CurrentFrame;
            _frameEditAnnouncement = $"Frame {frame.Width} × {frame.Height} px at {frame.X}, {frame.Y}.";
            SetStatus(CaptureExclusionFailed ? CaptureExclusionStatusText : _frameEditAnnouncement, warning: CaptureExclusionFailed);
        }
        OnPropertyChanged(nameof(FrameEditorHint));
    }

    private void InterruptFrameInteraction()
    {
        ClearFrameEditAnnouncement();
        if (_frameEdit is null) return;
        _frameEdit = null;
        _overlay.CancelFrameInteraction();
        _frameEditRevision++;
        OnPropertyChanged(nameof(FrameEditRevision));
    }

    private void ClearFrameEditAnnouncement()
    {
        if (_frameEditAnnouncement.Length == 0) return;
        _frameEditAnnouncement = string.Empty;
        OnPropertyChanged(nameof(FrameEditorHint));
    }

    public void NudgeFrame(int dx, int dy, bool resize)
    {
        if (!CanEditFrame || _frameEdit is not null || dx is < -10 or > 10 || dy is < -10 or > 10) return;
        var original = CurrentFrame;
        var frame = FrameManipulation.Drag(original, Point.Empty, new Point(dx, dy),
            resize ? FrameHandle.BottomRight : FrameHandle.Move, SelectedFrameArea,
            aspectRatio: resize ? FrameEditAspectRatio : null);
        BeginFrameInteraction();
        SetFrameFromInteraction(frame);
        FinishFrameInteraction();
    }

    private static bool IsEditableFrame(Rectangle frame, Rectangle area) =>
        frame.Width is >= MinSize and <= MaxSize && frame.Height is >= MinSize and <= MaxSize && area.Contains(frame);

    private void AdoptInteractiveFrame(Rectangle frame, bool freeform)
    {
        ClearFrameEditAnnouncement();
        _anchor = null; _x = frame.X; _y = frame.Y; _width = frame.Width; _height = frame.Height;
        _activeFrame = null;
        if (freeform) _aspectLocked = false;
        if (!_aspectLocked) _ratio = (double)_width / _height;
        _preset = ResolutionPreset.Match(_width, _height);
        NotifyEditedFrame();
    }

    private void NotifyEditedFrame()
    {
        SelectOnly(Presets, p => p.Key == _preset.Id);
        OnPropertyChanged(nameof(WidthValue)); OnPropertyChanged(nameof(HeightValue));
        OnPropertyChanged(nameof(IsAspectLocked)); OnPropertyChanged(nameof(LogicalSizeNote));
        OnPropertyChanged(nameof(MeasureNote));
        ConfigureFrameEditing();
        SyncOverlayToRequest();
        NotifyFrame();
    }

    private void RefreshSnapTargets()
    {
        try
        {
            _frameSnapTargets = [.. _windowService.GetWindows().Where(w => !w.IsMinimized &&
                w.Bounds.Width > 0 && w.Bounds.Height > 0 && w.Bounds.IntersectsWith(SelectedFrameArea))
                .OrderBy(w => w.ZOrder).Take(32).Select(w => w.Bounds)];
        }
        catch (Exception ex) when (IsExpected(ex)) { _frameSnapTargets = []; }
    }

    private void ConfigureFrameEditing() => _overlay.SetEditingOptions(new(
        FrameEditAspectRatio, SnapToEdges ? _frameSnapTargets : [],
        SnapToEdges && _display is not null ? (int)Math.Round(8 * _display.Scale) : 0));

    private void NotifyFrameEditor()
    {
        OnPropertyChanged(nameof(CanEditFrame)); OnPropertyChanged(nameof(SelectedFrameArea));
        OnPropertyChanged(nameof(FrameEditAspectRatio));
        OnPropertyChanged(nameof(FrameEditorHint));
        DrawFrameCommand.NotifyCanExecuteChanged(); UseWindowBoundsCommand.NotifyCanExecuteChanged();
    }

    private void OnFrameInteractionStarted(object? sender, EventArgs e) => BeginFrameInteraction();
    private void OnFrameInteractionFinished(object? sender, bool canceled) => FinishFrameInteraction(canceled);

    private async Task DrawFrameAsync()
    {
        if (_framePicker is null || !IsOutlineMode) return;
        bool wasVisible = _overlay.IsVisible;
        bool canceled = false;
        _overlay.Hide();
        using var selection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _framePickCancellation = selection;
        try
        {
            SetStatus("Drag a rectangle on one display. Right-click or Esc cancels.");
            var frame = await _framePicker.PickAsync(_displays, _keepClear, selection.Token);
            if (selection.IsCancellationRequested || frame is null) { canceled = true; return; }
            var display = _displays.FirstOrDefault(d => IsEditableFrame(frame.Value, d.UsableArea(_keepClear)))
                ?? throw new InvalidOperationException("Draw at least 100 × 100 physical pixels inside one display.");
            _display = display;
            SelectOnly(Displays, d => d.DeviceName == display.DeviceName);
            AdoptInteractiveFrame(frame.Value, freeform: true);
            ShowOutline(frame.Value, validate: true);
            wasVisible = false;
            if (!CaptureExclusionFailed)
                SetStatus($"Drew {frame.Value.Width} × {frame.Value.Height} px. Use handles or the preview arrow keys to adjust it.");
        }
        finally
        {
            _framePickCancellation = null;
            if (wasVisible && IsFit && _display is not null) ShowOutline(CurrentFrame, validate: true);
            if ((canceled || selection.IsCancellationRequested) && !CaptureExclusionFailed)
                SetStatus("Frame drawing canceled.");
        }
    }

    private async Task UseWindowBoundsAsync()
    {
        if (!IsOutlineMode) return;
        SetStatus("Click an existing window to use its visible bounds. The window will keep its size. Esc cancels.");
        var window = await _windowPicker.PickAsync(_lifetime.Token);
        if (window is null) { SetStatus("Window picking canceled."); return; }
        var frame = _windowService.Measure(window, WindowArea.VisibleFrame);
        var display = _displays.FirstOrDefault(d => IsEditableFrame(frame, d.UsableArea(_keepClear)))
            ?? throw new InvalidOperationException("That window crosses a display edge, overlaps the taskbar clearance, or is smaller than 100 × 100 px. Draw a frame or adjust the window first.");
        _display = display;
        SelectOnly(Displays, d => d.DeviceName == display.DeviceName);
        AdoptInteractiveFrame(frame, freeform: true);
        ShowOutline(frame, validate: true);
        if (!CaptureExclusionFailed)
            SetStatus($"Using {window.ProcessName}'s {frame.Width} × {frame.Height} px window bounds. The app was not resized.");
    }
}
