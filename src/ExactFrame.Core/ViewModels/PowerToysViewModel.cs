using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Integration;
using ExactFrame.Core.Services;

namespace ExactFrame.Core.ViewModels;

/// <summary>Created only when the user opens optional PowerToys setup.</summary>
public sealed class PowerToysViewModel : ObservableObject, IDisposable
{
    private readonly IPowerToysService _service;
    private readonly IClipboardService _clipboard;
    private readonly CancellationTokenSource _lifetime = new();
    private PowerToysCapabilities _capabilities = new(false, false, false);
    private bool _busy;
    private string _status = "Check for PowerToys to see the available optional tools.";
    private PowerToysLayout? _layout;
    private double _awakeMinutes = 60;

    public PowerToysViewModel(MainViewModel main, IPowerToysService service, IClipboardService clipboard)
    {
        Main = main; _service = service; _clipboard = clipboard;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(RefreshAsync), () => IsIdle);
        ApplyLayoutCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            await _service.ApplyLayoutAsync(SelectedLayout!.Id, _lifetime.Token);
            Status = "FancyZones layout applied to its first monitor. Snap your windows, then apply an ExactFrame profile for precise sizing.";
        }), () => CanUseFancyZones && SelectedLayout is not null);
        OpenEditorCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            await _service.OpenLayoutEditorAsync(_lifetime.Token); Status = "Opened the FancyZones layout editor.";
        }), () => CanUseFancyZones);
        LinkProfileLayoutCommand = new RelayCommand(() =>
        {
            Main.LinkProfilePowerToysLayout(SelectedLayout!.Id);
            NotifyCommands();
        }, () => CanUseFancyZones && SelectedLayout is not null && Main.WorkspaceArguments.StartsWith("--profile", StringComparison.Ordinal));
        UnlinkProfileLayoutCommand = new RelayCommand(() => { Main.LinkProfilePowerToysLayout(null); NotifyCommands(); }, () => IsIdle && Main.HasProfilePowerToysLayout);
        StartAwakeCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            await _service.StartAwakeAsync((int)AwakeMinutes, _lifetime.Token);
            Status = $"Requested Awake for {(int)AwakeMinutes} minutes with the display on. The session ends if ExactFrame exits.";
        }), () => CanUseAwake);
        StopAwakeCommand = new RelayCommand(() =>
        {
            try { _service.StopAwake(); Status = "Stopped any Awake session started by ExactFrame."; }
            catch (Exception ex) when (IsExpected(ex)) { Status = ex.Message; }
        }, () => IsIdle);
        CopyWorkspaceCommand = new RelayCommand(() =>
        {
            try { _clipboard.SetText(Main.WorkspaceArguments); Status = "Copied launch arguments for the active profile. Paste them into ExactFrame's entry in PowerToys Workspaces."; }
            catch (Exception ex) when (IsExpected(ex)) { Status = ex.Message; }
        }, () => IsIdle && Main.HasProfiles && Main.WorkspaceArguments.StartsWith("--profile", StringComparison.Ordinal));
    }

    public MainViewModel Main { get; }
    public ObservableCollection<PowerToysLayout> Layouts { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ApplyLayoutCommand { get; }
    public IAsyncRelayCommand OpenEditorCommand { get; }
    public IRelayCommand LinkProfileLayoutCommand { get; }
    public IRelayCommand UnlinkProfileLayoutCommand { get; }
    public IAsyncRelayCommand StartAwakeCommand { get; }
    public IRelayCommand StopAwakeCommand { get; }
    public IRelayCommand CopyWorkspaceCommand { get; }
    public bool IsIdle => !_busy;
    public bool IsBusy => _busy;
    public bool CanUseFancyZones => IsIdle && _capabilities.FancyZones;
    public bool CanUseAwake => IsIdle && _capabilities.Awake;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public PowerToysLayout? SelectedLayout
    {
        get => _layout;
        set
        {
            if (!SetProperty(ref _layout, value)) return;
            ApplyLayoutCommand.NotifyCanExecuteChanged(); LinkProfileLayoutCommand.NotifyCanExecuteChanged();
        }
    }
    public double AwakeMinutes
    {
        get => _awakeMinutes;
        set { if (double.IsFinite(value)) SetProperty(ref _awakeMinutes, Math.Clamp(Math.Round(value), 1, 240)); }
    }

    private async Task RefreshAsync()
    {
        _capabilities = new(false, false, false);
        Layouts.Clear(); SelectedLayout = null;
        _capabilities = await _service.DiscoverAsync(_lifetime.Token);
        if (_lifetime.IsCancellationRequested) return;
        if (_capabilities.FancyZones)
        {
            foreach (var layout in await _service.GetLayoutsAsync(_lifetime.Token)) Layouts.Add(layout);
            SelectedLayout = Layouts.FirstOrDefault();
        }
        Main.RefreshCommandCatalog();
        Status = _capabilities.Installed
            ? "PowerToys found. Enable FancyZones in PowerToys before applying a layout. Each action below is optional."
            : "PowerToys is not installed. All ExactFrame framing, profiles, shortcuts, OBS and SnapStudio features work normally.";
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; NotifyCommands();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsExpected(ex)) { Status = ex.Message; }
        finally { _busy = false; NotifyCommands(); }
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanUseFancyZones)); OnPropertyChanged(nameof(CanUseAwake));
        RefreshCommand.NotifyCanExecuteChanged(); ApplyLayoutCommand.NotifyCanExecuteChanged(); OpenEditorCommand.NotifyCanExecuteChanged();
        StartAwakeCommand.NotifyCanExecuteChanged(); StopAwakeCommand.NotifyCanExecuteChanged(); CopyWorkspaceCommand.NotifyCanExecuteChanged();
        LinkProfileLayoutCommand.NotifyCanExecuteChanged(); UnlinkProfileLayoutCommand.NotifyCanExecuteChanged();
    }

    private static bool IsExpected(Exception ex) => ex is InvalidOperationException or IOException or UnauthorizedAccessException or
        ArgumentException or Win32Exception or ExternalException or JsonException or System.Security.SecurityException;

    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
