using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

// Read-only values shown on the dark preview stage and in the footer.
public sealed partial class MainViewModel
{
    private static readonly string[] StageProperties =
    [
        nameof(StageEyebrow), nameof(SizeText), nameof(StageSubtitle), nameof(DisplayLabel), nameof(IsFit),
        nameof(IsNotFit), nameof(FitText), nameof(IsChipLive), nameof(ChipText), nameof(ShowRecorderChip),
        nameof(ShowLockChip), nameof(BoundsLeft), nameof(BoundsTop), nameof(BoundsRight), nameof(BoundsBottom),
        nameof(BoundsText), nameof(HudPositionText), nameof(Preview)
    ];

    private static readonly string[] FooterProperties =
    [
        nameof(PrimaryLabel), nameof(PrimaryIsHide), nameof(ShowPrimaryHotkey), nameof(PrimaryHotkeyKeys),
        nameof(CanRestore), nameof(StatusTitle), nameof(StatusKind), nameof(IsOutlineVisible)
    ];

    public string StageEyebrow => IsOutlineMode
        ? "Recording frame"
        : _area == WindowArea.Client ? "Target client area" : "Target window size";

    public string SizeText
    {
        get
        {
            var frame = CurrentFrame;
            return $"{frame.Width} × {frame.Height}";
        }
    }

    public string StageSubtitle
    {
        get
        {
            var frame = CurrentFrame;
            if (IsResizeMode)
            {
                return _targetWindow switch
                {
                    null => "Choose a window to resize",
                    { IsMinimized: true } => $"{_targetWindow.ProcessName} · minimized, it will be restored",
                    _ => $"{_targetWindow.ProcessName} · currently {_targetWindow.Bounds.Width} × {_targetWindow.Bounds.Height}"
                };
            }
            string display = _display?.Title ?? "No display";
            string nested = NestedFrames.Count > 0
                ? $" · {NestedFrames.Count} extra {(NestedFrames.Count == 1 ? "frame" : "frames")}" : string.Empty;
            return $"{AspectRatio.Describe(frame.Width, frame.Height)} · {AspectRatio.Orientation(frame.Width, frame.Height)} · {display}{nested}";
        }
    }

    public string DisplayLabel => _display is null
        ? "No display found"
        : $"{_display.Title} · {_display.Bounds.Width} × {_display.Bounds.Height} · {_display.ScalePercent}%" +
          (_display.IsPrimary ? " · Primary" : string.Empty);

    public bool IsFit => CurrentFit.IsFit;

    public bool IsNotFit => !IsFit;

    public string FitText => CurrentFit.Message;

    public bool IsChipLive => IsResizeMode ? CanRestore : _overlay.IsVisible;

    public string ChipText => IsResizeMode
        ? (CanRestore ? "Applied" : "Preview")
        : (_overlay.IsVisible ? "Outline live" : "Outline hidden");

    public bool ShowRecorderChip => IsOutlineMode && _hideFromRecorders;

    public bool ShowLockChip => IsOutlineMode && _clickThrough;

    public string BoundsLeft => CurrentFrame.Left.ToString();

    public string BoundsTop => CurrentFrame.Top.ToString();

    public string BoundsRight => CurrentFrame.Right.ToString();

    public string BoundsBottom => CurrentFrame.Bottom.ToString();

    public string BoundsText
    {
        get
        {
            var frame = CurrentFrame;
            return $"{frame.X}, {frame.Y}, {frame.Width}, {frame.Height}";
        }
    }

    public string HudPositionText
    {
        get
        {
            var frame = CurrentFrame;
            return $"{frame.X}, {frame.Y}";
        }
    }

    public PreviewState Preview
    {
        get
        {
            if (_display is null) return PreviewState.Empty;
            // A minimized window has no on-screen rectangle to show.
            var ghost = IsResizeMode && _targetWindow is { IsMinimized: false } target ? target.Bounds : (System.Drawing.Rectangle?)null;
            return new PreviewState(
                _display.Bounds,
                _display.WorkArea,
                CurrentFrame,
                IsResizeMode,
                _style,
                ShowHandles: IsOutlineMode && !_clickThrough,
                ShowWindowChrome: IsResizeMode && _area == WindowArea.Client && _targetWindow is not null,
                TitleBarHeight: (int)Math.Round(32 * _display.Scale),
                ghost,
                SizeText,
                IsFit,
                NestedBounds(CurrentFrame));
        }
    }

    // ---- Footer ---------------------------------------------------------------------------------

    public string PrimaryLabel => IsBusy ? "Working…"
        : IsResizeMode ? "Resize window"
        : _overlay.IsVisible ? "Hide outline"
        : "Show outline";

    /// <summary>True when the primary button hides the outline, so it can use the dark style.</summary>
    public bool PrimaryIsHide => IsOutlineMode && _overlay.IsVisible;

    public bool ShowPrimaryHotkey => IsOutlineMode && _toggleHotkeyRegistered;

    public IReadOnlyList<string> PrimaryHotkeyKeys => _settings.ToggleOutlineHotkey.Keys;

    private void NotifyStage()
    {
        foreach (string name in StageProperties) OnPropertyChanged(name);
    }

    private void NotifyFooter()
    {
        foreach (string name in FooterProperties) OnPropertyChanged(name);
    }
}
