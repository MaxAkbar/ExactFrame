using System.Drawing;
using ExactFrame.Core.Geometry;

namespace ExactFrame.Core.ViewModels;

// Showing, hiding, locking and copying the on-screen outline.
public sealed partial class MainViewModel
{
    private bool _clickThrough;
    private bool _hideFromRecorders;
    private bool _copied;
    private int _copyVersion;

    public bool IsOutlineVisible => _overlay.IsVisible;

    public bool IsClickThrough
    {
        get => _clickThrough;
        set
        {
            if (!SetProperty(ref _clickThrough, value)) return;
            _overlay.SetLocked(value);
            NotifyStage();
            if (_initialized)
            {
                SetStatus(value
                    ? "Click-through is on. Clicks pass through the outline."
                    : "Click-through is off. Drag the border to move the frame.");
            }
        }
    }

    public bool HideFromRecorders
    {
        get => _hideFromRecorders;
        set
        {
            if (!SetProperty(ref _hideFromRecorders, value)) return;
            _settings.HideFromRecorders = value;
            SaveSettings();
            _overlay.SetCaptureExclusion(value);
            NotifyStage();
            if (value && _overlay.IsVisible && !_overlay.CaptureExclusionApplied)
                SetStatus("Capture exclusion is unavailable. Hide the outline before recording.", warning: true);
        }
    }

    public bool IsCopied
    {
        get => _copied;
        private set
        {
            if (!SetProperty(ref _copied, value)) return;
            OnPropertyChanged(nameof(IsNotCopied));
            OnPropertyChanged(nameof(CopyLabel));
        }
    }

    public bool IsNotCopied => !_copied;

    public string CopyLabel => _copied ? "Copied" : "Copy bounds";

    private async Task PrimaryAsync()
    {
        if (IsOutlineMode)
        {
            Guard(ToggleOutline);
            return;
        }
        await RunBusyAsync(ResizeTargetAsync);
    }

    private void ToggleOutline()
    {
        if (_overlay.IsVisible) HideOutline();
        else ShowOutline(CurrentFrame, validate: true);
    }

    private void ShowOutline(Rectangle frame, bool validate)
    {
        if (_display is null) throw new InvalidOperationException("No display is available.");

        if (validate)
        {
            var fit = FrameGeometry.CheckFit(frame, _display.Bounds, _display.WorkArea, _keepClear);
            if (!fit.IsFit)
            {
                throw new InvalidOperationException(
                    $"The {frame.Width} × {frame.Height} frame at ({frame.X}, {frame.Y}) doesn’t fit: " +
                    $"{fit.Message.ToLowerInvariant()}. Center it, choose a larger display or pick a smaller size.");
            }
        }

        _overlay.SetLocked(_clickThrough);
        _overlay.Show(frame, _display, _keepClear);

        if (_hideFromRecorders && !_overlay.CaptureExclusionApplied)
            SetStatus("Windows couldn’t exclude the outline from capture. Hide it before recording.", warning: true);
        else if (IsOutlineMode)
            SetStatus($"Outline shown at {frame.X}, {frame.Y}. Drag the border to move it.");
    }

    private void HideOutline()
    {
        if (!_overlay.IsVisible) return;
        _overlay.Hide();
        if (IsOutlineMode && !_statusWarning) SetStatus(Summary());
    }

    private void OnOverlayVisibilityChanged(object? sender, EventArgs e)
    {
        NotifyStage();
        NotifyFooter();
    }

    private void OnOverlayFrameMoved(object? sender, EventArgs e)
    {
        var frame = _overlay.Frame;
        _anchor = null;
        _x = frame.X;
        _y = frame.Y;
        _activeFrame = frame;
        NotifyFrame();
        SetStatus($"Moved to {frame.X}, {frame.Y}.");
    }

    private async Task CopyBoundsAsync()
    {
        var frame = CurrentFrame;
        try
        {
            _clipboard.SetText($"X={frame.X}, Y={frame.Y}, Width={frame.Width}, Height={frame.Height}");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ReportError(ex);
            return;
        }

        SetStatus("Frame bounds copied. Paste them into your recorder’s region settings.");
        IsCopied = true;
        int version = ++_copyVersion;
        try
        {
            await Task.Delay(1600, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (version == _copyVersion) IsCopied = false;
    }
}
