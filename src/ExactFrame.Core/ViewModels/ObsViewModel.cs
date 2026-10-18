using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Obs;
using ExactFrame.Core.Services;

namespace ExactFrame.Core.ViewModels;

/// <summary>Explicit connect, preview, apply and undo workflow. Passwords and undo stay in memory.</summary>
public sealed class ObsViewModel : ObservableObject, IDisposable
{
    private readonly IObsService _service;
    private readonly MainViewModel _frame;
    private readonly IDisplayService _displays;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    private string _password = string.Empty;
    private double _port = 4455;
    private string _status = "In OBS, open Tools → WebSocket Server Settings and enable the server. Keep authentication enabled, then enter its port and password here.";
    private ObsScene? _scene;
    private ObsSource? _source;
    private ObsRegion? _region;
    private ObsPlan? _plan;
    private int _placementIndex;
    private long _contextVersion;

    public ObsViewModel(IObsService service, MainViewModel frame, IDisplayService displays)
    {
        _service = service;
        _frame = frame;
        _displays = displays;
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && !IsConnected);
        DisconnectCommand = new RelayCommand(Disconnect, () => !IsBusy && IsConnected);
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadScenesAsync), () => !IsBusy && IsConnected);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && IsConnected && _scene is not null && _source is not null && _region is not null);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !IsBusy && IsConnected && _plan is not null);
        UndoCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            await _service.UndoAsync(_lifetime.Token);
            InvalidatePreview();
            Status = "The previous OBS crop and placement were restored and verified.";
        }), () => !IsBusy && _service.CanUndo);
        _frame.PropertyChanged += OnFrameChanged;
        _displays.DisplaysChanged += OnDisplaysChanged;
        SyncRegions();
    }

    public ObservableCollection<ObsScene> Scenes { get; } = [];
    public ObservableCollection<ObsSource> Sources { get; } = [];
    public ObservableCollection<ObsRegion> Regions { get; } = [];
    public IAsyncRelayCommand ConnectCommand { get; }
    public IRelayCommand DisconnectCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    public bool IsBusy => _busy;
    public bool IsIdle => !_busy;
    public bool IsConnected => _service.IsConnected;
    public bool IsDisconnected => !IsConnected;
    public bool CanChoose => IsConnected && !IsBusy;
    public bool HasPreview => _plan is not null;
    public ObsPlan? Plan => _plan;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Port { get => _port; set => SetProperty(ref _port, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public ObsScene? SelectedScene => _scene;
    public ObsSource? SelectedSource
    {
        get => _source;
        set { if (SetProperty(ref _source, value)) InvalidatePreview(); }
    }
    public ObsRegion? SelectedRegion
    {
        get => _region;
        set { if (SetProperty(ref _region, value)) InvalidatePreview(); }
    }
    public int PlacementIndex
    {
        get => _placementIndex;
        set { if (value is >= 0 and <= 1 && SetProperty(ref _placementIndex, value)) InvalidatePreview(); }
    }

    public string PreviewDetails => _plan is not { } p ? string.Empty :
        $"{p.Region.Name}: {p.Region.Bounds.Width} × {p.Region.Bounds.Height} source pixels from {p.Before.DisplayName}.\n" +
        $"Crop margins: left {p.CropLeft}, top {p.CropTop}, right {p.CropRight}, bottom {p.CropBottom} px.\n" +
        $"Centered on the {p.Before.Video.CanvasWidth} × {p.Before.Video.CanvasHeight} canvas at {p.Scale * 100:0.##}% scale.\n" +
        $"OBS output setting: {p.Before.Video.OutputWidth} × {p.Before.Video.OutputHeight}.";

    public string ChangeDescription => _plan is null ? string.Empty :
        $"Apply changes the crop and placement of “{_plan.Before.Source.Name}” in “{_plan.Before.Scene.Name}”, resets its rotation and flip, and removes its bounding box. You can undo the last apply while this connection stays open.";

    public void SyncRegions()
    {
        var frame = _frame.CurrentFrame;
        var regions = new List<ObsRegion> { new("main", "Main frame", frame) };
        regions.AddRange(_frame.NestedFrames.Select(n => new ObsRegion(n.Frame.Id, n.Name, NestedFrameGeometry.Place(frame, n.Frame)))
            .Where(n => n.Bounds.Width > 0 && n.Bounds.Height > 0));
        if (Regions.SequenceEqual(regions)) return;
        string? selected = _region?.Key;
        Regions.Clear();
        foreach (var region in regions) Regions.Add(region);
        SelectedRegion = Regions.FirstOrDefault(r => r.Key == selected) ?? Regions.FirstOrDefault();
        InvalidatePreview();
    }

    public Task SelectSceneAsync(ObsScene scene) => RunAsync(async () =>
    {
        if (_scene == scene) return;
        _scene = scene;
        OnPropertyChanged(nameof(SelectedScene));
        await LoadSourcesAsync();
    });

    private async Task ConnectAsync()
    {
        string password = Password;
        Password = string.Empty;
        await RunAsync(async () =>
        {
            if (!double.IsFinite(Port) || Port != Math.Truncate(Port) || Port is < 1 or > 65535)
                throw new ObsException("Choose an OBS server port between 1 and 65535.");
            await _service.ConnectAsync((int)Port, password, _lifetime.Token);
            await LoadScenesAsync();
        });
    }

    private async Task LoadScenesAsync()
    {
        InvalidatePreview();
        string? previous = _scene?.Name;
        _scene = null;
        Scenes.Clear();
        Sources.Clear();
        SelectedSource = null;
        var list = await _service.GetScenesAsync(_lifetime.Token);
        foreach (var scene in list.Scenes) Scenes.Add(scene);
        _scene = Scenes.FirstOrDefault(s => s.Name == previous) ?? Scenes.FirstOrDefault(s => s.Name == list.CurrentSceneName) ?? Scenes.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedScene));
        await LoadSourcesAsync();
    }

    private async Task LoadSourcesAsync()
    {
        InvalidatePreview();
        var previous = _source;
        Sources.Clear();
        SelectedSource = null;
        if (_scene is null) { Status = "Connected. Add a scene in OBS, then refresh."; return; }
        foreach (var source in await _service.GetSourcesAsync(_scene, _lifetime.Token)) Sources.Add(source);
        SelectedSource = Sources.FirstOrDefault(s => s == previous) ?? Sources.FirstOrDefault();
        Status = Sources.Count == 0
            ? "Connected. Add a Display Capture directly to this scene in OBS, then refresh. Grouped sources, Window Capture and other source types are not supported yet."
            : "Connected to OBS on this PC. Choose a frame and Display Capture, then preview the change.";
    }

    private Task PreviewAsync() => RunAsync(async () =>
    {
        InvalidatePreview();
        if (_scene is null || _source is null || _region is null) return;
        long context = _contextVersion;
        var plan = await _service.PreviewAsync(_scene, _source, _region, (ObsPlacement)_placementIndex, _lifetime.Token);
        if (context != _contextVersion) { Status = "The ExactFrame region changed. Preview the change again."; return; }
        _plan = plan;
        NotifyPreview();
        Status = "Preview ready. Review the crop, scale and placement before applying.";
    });

    private Task ApplyAsync() => RunAsync(async () =>
    {
        var plan = _plan ?? throw new ObsException("Preview the change before applying it.");
        InvalidatePreview();
        await _service.ApplyAsync(plan, _lifetime.Token);
        Status = "The selected frame was applied to OBS and its transform was verified. Check the OBS preview before recording.";
    });

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        NotifyState();
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        {
            InvalidatePreview();
            Status = ex is ObsException ? ex.Message : "OBS returned an unexpected response. Reconnect and refresh before trying again.";
        }
        finally { _busy = false; NotifyState(); }
    }

    private void Disconnect()
    {
        _service.Disconnect();
        Password = string.Empty;
        _scene = null;
        SelectedSource = null;
        Scenes.Clear(); Sources.Clear();
        OnPropertyChanged(nameof(SelectedScene));
        InvalidatePreview();
        Status = "Disconnected from OBS. The current OBS layout is kept; undo history has been cleared.";
        NotifyState();
    }

    private void InvalidatePreview() { _contextVersion++; _plan = null; NotifyPreview(); }
    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(Plan)); OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewDetails)); OnPropertyChanged(nameof(ChangeDescription));
        PreviewCommand.NotifyCanExecuteChanged(); ApplyCommand.NotifyCanExecuteChanged();
    }
    private void NotifyState()
    {
        foreach (string property in new[] { nameof(IsBusy), nameof(IsIdle), nameof(IsConnected), nameof(IsDisconnected), nameof(CanChoose) })
            OnPropertyChanged(property);
        ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged(); ApplyCommand.NotifyCanExecuteChanged(); UndoCommand.NotifyCanExecuteChanged();
    }
    private void OnFrameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Preview) or "" or null) SyncRegions();
    }
    private void OnDisplaysChanged(object? sender, EventArgs e) => InvalidatePreview();
    public void Dispose()
    {
        _frame.PropertyChanged -= OnFrameChanged;
        _displays.DisplaysChanged -= OnDisplaysChanged;
        _lifetime.Cancel();
        _service.Disconnect();
        _lifetime.Dispose();
        Password = string.Empty;
    }
}
