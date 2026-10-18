using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Services;

namespace ExactFrame.Core.ViewModels;

/// <summary>A fresh, user-reported clip check. Native measurements never mark a recording as verified.</summary>
public sealed class RecordingSetupViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly IAppInfo _app;
    private readonly IDisplayService _displays;
    private readonly IClipboardService _clipboard;
    private string _context;
    private string _identity;
    private string _recorder = string.Empty, _captureMethod = "Select capture method", _browser = string.Empty, _clip = string.Empty;
    private bool _dimensions, _edges, _overlays;
    private string _copyStatus = string.Empty;

    public RecordingSetupViewModel(MainViewModel main, IAppInfo app, IDisplayService displays, IClipboardService clipboard)
    {
        _main = main; _app = app; _displays = displays; _clipboard = clipboard;
        _context = main.RecordingSetupContext();
        _identity = main.RecordingSetupIdentity;
        main.PropertyChanged += OnMainChanged;
        displays.DisplaysChanged += OnDisplaysChanged;
        CopyReportCommand = new RelayCommand(CopyReport);
    }

    public IReadOnlyList<string> CaptureMethods { get; } =
    ["Select capture method", "OBS Display Capture / DXGI", "OBS Display Capture / Windows Graphics Capture",
     "OBS Window Capture / Windows Graphics Capture", "OBS Window Capture / BitBlt", "Other (describe in recorder field)"];
    public IRelayCommand CopyReportCommand { get; }
    public string Context => _context;
    public string Recorder { get => _recorder; set { if (SetProperty(ref _recorder, value ?? string.Empty)) Reset(); } }
    public string CaptureMethod { get => _captureMethod; set { if (SetProperty(ref _captureMethod, value ?? CaptureMethods[0])) Reset(); } }
    public string Browser { get => _browser; set { if (SetProperty(ref _browser, value ?? string.Empty)) Reset(); } }
    public string Clip { get => _clip; set { if (SetProperty(ref _clip, value ?? string.Empty)) Reset(); } }
    public bool DimensionsMatch { get => _dimensions; set { if (SetProperty(ref _dimensions, value)) Changed(); } }
    public bool EdgesVisible { get => _edges; set { if (SetProperty(ref _edges, value)) Changed(); } }
    public bool OverlaysAbsent { get => _overlays; set { if (SetProperty(ref _overlays, value)) Changed(); } }
    public string CopyStatus { get => _copyStatus; private set => SetProperty(ref _copyStatus, value); }
    public bool CheckComplete => _dimensions && _edges && _overlays &&
        !string.IsNullOrWhiteSpace(Recorder) && !string.IsNullOrWhiteSpace(Browser) &&
        !string.IsNullOrWhiteSpace(Clip) && CaptureMethod != CaptureMethods[0] && CaptureMethods.Contains(CaptureMethod);
    public string Result => CheckComplete
        ? "User-reported pass for this clip and setup. ExactFrame has not inspected the video."
        : "Recorded result: unverified. Complete the clip checks below.";

    public string Report()
    {
        var text = new StringBuilder().AppendLine("ExactFrame recording setup check")
            .AppendLine($"UTC: {DateTimeOffset.UtcNow:O}")
            .AppendLine($"ExactFrame: {_app.Version}; Windows: {_app.OperatingSystem}")
            .AppendLine(Context).AppendLine($"Recorder and version: {Recorder}")
            .AppendLine($"Capture method: {CaptureMethod}").AppendLine($"Browser/app and version: {Browser}")
            .AppendLine($"Clip reference (user supplied): {Clip}").AppendLine(Result)
            .AppendLine($"User checked file dimensions and scaling: {DimensionsMatch}")
            .AppendLine($"User checked all four edges and alignment: {EdgesVisible}")
            .AppendLine($"User checked outline, guides, dimming, magnifier and control bar absent: {OverlaysAbsent}")
            .AppendLine("Monitor transition: unverified unless separately recorded and checked.");
        foreach (var display in _displays.GetDisplays())
            text.AppendLine($"Display {display.Number}: {display.Bounds.Width}x{display.Bounds.Height} at ({display.Bounds.X},{display.Bounds.Y}); {display.ScalePercent}%");
        return text.ToString();
    }

    private void Reset()
    {
        _dimensions = _edges = _overlays = false;
        OnPropertyChanged(nameof(DimensionsMatch)); OnPropertyChanged(nameof(EdgesVisible)); OnPropertyChanged(nameof(OverlaysAbsent));
        Changed();
    }
    private void Changed() { OnPropertyChanged(nameof(Result)); OnPropertyChanged(nameof(CheckComplete)); CopyStatus = string.Empty; }
    private void RefreshContext(bool force = false)
    {
        string context = _main.RecordingSetupContext();
        string identity = _main.RecordingSetupIdentity;
        if (!force && context == _context && identity == _identity) return;
        _identity = identity;
        _context = context; OnPropertyChanged(nameof(Context)); Reset();
    }
    private void OnMainChanged(object? sender, PropertyChangedEventArgs e) => RefreshContext();
    private void OnDisplaysChanged(object? sender, EventArgs e) => RefreshContext(force: true);
    private void CopyReport()
    {
        try { _clipboard.SetText(Report()); CopyStatus = "Report copied. Review it before sharing."; }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        { CopyStatus = "Could not copy the report. Try again."; }
    }
    public void Dispose() { _main.PropertyChanged -= OnMainChanged; _displays.DisplaysChanged -= OnDisplaysChanged; }
}
