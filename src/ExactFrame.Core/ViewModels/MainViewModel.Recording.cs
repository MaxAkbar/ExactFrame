namespace ExactFrame.Core.ViewModels;

public sealed partial class MainViewModel
{
    private bool? _hudCaptureExclusion;
    private int _hudCaptureError;

    public bool HudCaptureExclusionFailed => _overlay.IsVisible && ShowHud && _hideFromRecorders && _hudCaptureExclusion == false;
    public bool CaptureExclusionFailed => _overlay.IsVisible && _hideFromRecorders && (!_overlay.CaptureExclusionApplied || HudCaptureExclusionFailed);

    /// <summary>Windows acceptance is not evidence about the recorder's output.</summary>
    public string CaptureExclusionStatusText => !_hideFromRecorders
        ? "Hide from recorders is off. Overlays may appear in the recording."
        : !_overlay.IsVisible
            ? "Not checked: show the outline to request capture exclusion."
            : !_overlay.CaptureExclusionApplied
                ? "Windows rejected exclusion for an overlay. Hide the outline before recording."
                : HudCaptureExclusionFailed
                    ? $"Windows rejected control bar exclusion (error {_hudCaptureError}). Hide the outline or control bar before recording."
                    : ShowHud && _hudCaptureExclusion is null
                        ? "Control bar exclusion has not been checked."
                        : "Windows accepted capture exclusion. The recorded result is still unverified.";

    /// <summary>Called by the native control bar; null means it is hidden.</summary>
    public void ReportHudCaptureExclusion(bool? succeeded, int errorCode = 0)
    {
        if (_hudCaptureExclusion == succeeded && _hudCaptureError == errorCode) return;
        bool recovered = _hudCaptureExclusion == false && succeeded == true;
        _hudCaptureExclusion = succeeded;
        _hudCaptureError = errorCode;
        OnPropertyChanged(nameof(CaptureExclusionStatusText));
        OnPropertyChanged(nameof(HudCaptureExclusionFailed));
        OnPropertyChanged(nameof(CaptureExclusionFailed));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusKind));
        if (HudCaptureExclusionFailed) SetStatus(CaptureExclusionStatusText, warning: true);
        else if (recovered && _statusMessage.Contains("control bar exclusion", StringComparison.Ordinal))
            SetStatus(CaptureExclusionStatusText);
    }

    public string RecordingSetupContext()
    {
        var frame = CurrentFrame;
        return $"Mode: {_mode}; area: {(IsResizeMode ? AreaName : "outline")}; app: {(IsResizeMode ? _targetWindow?.ProcessName ?? "none" : "not selected")}\n" +
               $"Frame in physical pixels: X={frame.X}, Y={frame.Y}, Width={frame.Width}, Height={frame.Height}\n" +
               $"Display: {_display?.Title}, {_display?.ScalePercent}%\n" +
               $"Outline visible: {_overlay.IsVisible}; control bar enabled: {ShowHud}\n" +
               $"Guides: thirds={ShowThirds}, center={ShowCenterMark}, safe area={ShowSafeArea}; dimming={DimOutside}; magnifier={ShowEdgeMagnifier}; additional guides={NestedFrames.Count}\n" +
               $"Capture exclusion: {CaptureExclusionStatusText}";
    }

    internal string RecordingSetupIdentity => $"{_targetWindow?.Handle}/{_targetWindow?.ProcessId}/{_targetWindow?.ThreadId}/{_style}/" +
        string.Join(";", NestedFrames.Select(f => f.Frame));
}
