using System.Drawing;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Services;

namespace ExactFrame.Core.ViewModels;

public sealed record SnapStudioFrameChoice(string Id, string Name, Rectangle Bounds);

public sealed partial class MainViewModel
{
    private IReadOnlyList<SnapStudioFrameChoice> _snapStudioFrames = [];
    private string _snapStudioFrameId = "main";
    public IAsyncRelayCommand CaptureInSnapStudioCommand { get; }
    public IAsyncRelayCommand ChooseSnapStudioCommand { get; }
    public IReadOnlyList<SnapStudioFrameChoice> SnapStudioFrames => _snapStudioFrames;
    public string SnapStudioFrameId
    {
        get => _snapStudioFrameId;
        set { if (value is not null && _snapStudioFrames.Any(frame => frame.Id == value)) SetProperty(ref _snapStudioFrameId, value); }
    }
    public string SnapStudioApplication => string.IsNullOrWhiteSpace(_settings.SnapStudioExecutablePath)
        ? "Choose SnapStudio the first time you capture." : _settings.SnapStudioExecutablePath;

    private void NotifySnapStudioFrames()
    {
        _snapStudioFrames = [new("main", $"Main frame · {CurrentFrame.Width} × {CurrentFrame.Height}", CurrentFrame),
            .. NestedFrames.Select(item => new SnapStudioFrameChoice(item.Frame.Id, $"{item.Name} · {item.Bounds.Width} × {item.Bounds.Height}", item.Bounds))];
        if (!_snapStudioFrames.Any(frame => frame.Id == _snapStudioFrameId)) _snapStudioFrameId = "main";
        OnPropertyChanged(nameof(SnapStudioFrames)); OnPropertyChanged(nameof(SnapStudioFrameId));
        CaptureInSnapStudioCommand?.NotifyCanExecuteChanged();
    }
    private async Task ChooseSnapStudioAsync()
    {
        if (_snapStudioCapture is null) return;
        string? selected = await _snapStudioCapture.ChooseApplicationAsync(_lifetime.Token);
        if (selected is null) return;
        _settings.SnapStudioExecutablePath = selected;
        SaveSettings(); OnPropertyChanged(nameof(SnapStudioApplication));
    }
    private async Task CaptureInSnapStudioAsync()
    {
        if (_snapStudioCapture is null) return;
        if (string.IsNullOrWhiteSpace(_settings.SnapStudioExecutablePath)) await ChooseSnapStudioAsync();
        if (string.IsNullOrWhiteSpace(_settings.SnapStudioExecutablePath)) return;
        var frame = _snapStudioFrames.SingleOrDefault(frame => frame.Id == _snapStudioFrameId)
            ?? new SnapStudioFrameChoice("main", "Main frame", CurrentFrame);
        SnapStudioCaptureConnection.ValidateRegion(frame.Bounds);
        if (!_displayService.GetDisplays().Any(display => display.Bounds.Contains(frame.Bounds)))
            throw new InvalidOperationException("The entire frame must fit on one connected display. Move it fully onto a display before capturing.");
        SetStatus("Capturing the frame in SnapStudio…");
        await _snapStudioCapture.CaptureAsync(_settings.SnapStudioExecutablePath, frame.Bounds,
            frame.Id == "main" ? "Main frame" : NestedFrames.Single(item => item.Frame.Id == frame.Id).Name, _lifetime.Token);
        SetStatus("Opened the frame in SnapStudio. Add annotations, then copy or export.");
    }
}
