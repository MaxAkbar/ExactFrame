using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Core.Settings;

namespace ExactFrame.Core.ViewModels;

/// <summary>
/// State and behavior behind the main window and the on-screen HUD. Split by concern across partial files:
/// Frame (size, position, display), NestedFrames, Stage (read-only preview values), Outline, Style, Profiles and Resize.
/// All members are expected to run on the UI thread.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDisplayService _displayService;
    private readonly IWindowService _windowService;
    private readonly IWindowPicker _windowPicker;
    private readonly IOutlineOverlay _overlay;
    private readonly IHotkeyService _hotkeys;
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _store;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetime = new();

    private FrameMode _mode;
    private SettingsTab _selectedTab;
    private bool _isBusy;
    private string _statusMessage = "Choose a size, then show the outline.";
    private bool _statusWarning;
    private bool _initialized;
    private bool _disposed;

    public MainViewModel(
        IDisplayService displayService,
        IWindowService windowService,
        IWindowPicker windowPicker,
        IOutlineOverlay overlay,
        IHotkeyService hotkeys,
        IClipboardService clipboard,
        IDialogService dialogs,
        ISettingsStore store)
    {
        _displayService = displayService;
        _windowService = windowService;
        _windowPicker = windowPicker;
        _overlay = overlay;
        _hotkeys = hotkeys;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _store = store;
        _settings = store.Load();
        _style = _settings.Style;
        _hideFromRecorders = _settings.HideFromRecorders;

        Presets = [.. ResolutionPreset.All.Select(p => new ChoiceItem(p.Id, p.Name, () => SelectPreset(p), p.Dimensions))];
        Anchors = [.. Enum.GetValues<FrameAnchor>().Select(a => new AnchorItem(a, () => SelectAnchor(a)))];
        Swatches = [.. OutlinePalette.All.Select(c => new SwatchItem(c, () => UpdateStyle(_style with { Color = c })))];
        Thicknesses = [.. OutlineStyle.Thicknesses.Select(t => new ChoiceItem(t.ToString(), $"{t} px", () => UpdateStyle(_style with { Thickness = t })))];
        Lines = [.. Enum.GetValues<OutlineLine>().Select(l => new ChoiceItem(l.ToString(), l.ToString(), () => UpdateStyle(_style with { Line = l })))];
        MeasureOptions =
        [
            new ChoiceItem(nameof(WindowArea.Client), "Client area", () => SelectArea(WindowArea.Client)),
            new ChoiceItem(nameof(WindowArea.VisibleFrame), "Whole window", () => SelectArea(WindowArea.VisibleFrame))
        ];
        PlacementOptions =
        [
            new ChoiceItem("center", "Center", () => SelectAnchor(FrameAnchor.Center)),
            new ChoiceItem("keep", "Keep position", KeepWindowPosition)
        ];

        ShowOutlineModeCommand = new RelayCommand(() => SetMode(FrameMode.Outline));
        ShowResizeModeCommand = new RelayCommand(() => SetMode(FrameMode.Resize));
        OpenProfilesCommand = new RelayCommand(() =>
        {
            SetMode(FrameMode.Outline);
            SelectedTab = SettingsTab.Profiles;
        });
        PrimaryCommand = new AsyncRelayCommand(PrimaryAsync, () => !IsBusy && (IsOutlineMode || _targetWindow is not null));
        RestoreCommand = new AsyncRelayCommand(() => RunBusyAsync(RestoreTargetAsync), () => !IsBusy && CanRestore);
        CopyBoundsCommand = new AsyncRelayCommand(CopyBoundsAsync);
        CenterCommand = new RelayCommand(() => SelectAnchor(FrameAnchor.Center));
        HideOutlineCommand = new RelayCommand(HideOutline);
        ToggleOutlineCommand = new RelayCommand(() => Guard(ToggleOutline));
        ToggleClickThroughCommand = new RelayCommand(() => IsClickThrough = !IsClickThrough);
        ToggleHideFromRecordersCommand = new RelayCommand(() => HideFromRecorders = !HideFromRecorders);
        ToggleThirdsCommand = new RelayCommand(() => ShowThirds = !ShowThirds);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync);
        AddShortsFrameCommand = new RelayCommand(() => AddNestedFrame("Shorts 9:16", 9, 16));
        AddSquareFrameCommand = new RelayCommand(() => AddNestedFrame("Square 1:1", 1, 1));
        AddPortraitFrameCommand = new RelayCommand(() => AddNestedFrame("Portrait 4:5", 4, 5));
        AddCustomFrameCommand = new RelayCommand(() => AddNestedFrame("Custom frame", 16, 9));
        RefreshWindowsCommand = new RelayCommand(() => Guard(RefreshWindows));
        PickWindowCommand = new AsyncRelayCommand(PickWindowAsync);

        _overlay.FrameMoved += OnOverlayFrameMoved;
        _overlay.VisibilityChanged += OnOverlayVisibilityChanged;
        _windowService.TrackedWindowChanged += OnTrackedWindowChanged;
        _displayService.DisplaysChanged += OnDisplaysChanged;
        _hotkeys.Pressed += OnHotkeyPressed;

        SyncSelections();
    }

    // ---- Commands -------------------------------------------------------------------------------

    public IRelayCommand ShowOutlineModeCommand { get; }
    public IRelayCommand ShowResizeModeCommand { get; }
    public IRelayCommand OpenProfilesCommand { get; }
    public IAsyncRelayCommand PrimaryCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
    public IAsyncRelayCommand CopyBoundsCommand { get; }
    public IRelayCommand CenterCommand { get; }
    public IRelayCommand HideOutlineCommand { get; }
    public IRelayCommand ToggleOutlineCommand { get; }
    public IRelayCommand ToggleClickThroughCommand { get; }
    public IRelayCommand ToggleHideFromRecordersCommand { get; }
    public IRelayCommand ToggleThirdsCommand { get; }
    public IAsyncRelayCommand SaveProfileCommand { get; }
    public IRelayCommand RefreshWindowsCommand { get; }
    public IAsyncRelayCommand PickWindowCommand { get; }

    // ---- Mode, tabs and busy state --------------------------------------------------------------

    public FrameMode Mode => _mode;

    public bool IsOutlineMode => _mode == FrameMode.Outline;

    public bool IsResizeMode => _mode == FrameMode.Resize;

    public SettingsTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value)) NotifyPanels();
        }
    }

    public bool ShowFramePanel => IsOutlineMode && _selectedTab == SettingsTab.Frame;

    public bool ShowStylePanel => IsOutlineMode && _selectedTab == SettingsTab.Style;

    public bool ShowProfilesPanel => IsOutlineMode && _selectedTab == SettingsTab.Profiles;

    private void NotifyPanels()
    {
        OnPropertyChanged(nameof(ShowFramePanel));
        OnPropertyChanged(nameof(ShowStylePanel));
        OnPropertyChanged(nameof(ShowProfilesPanel));
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            NotifyFooter();
        }
    }

    public bool IsIdle => !_isBusy;

    // ---- Lifetime -------------------------------------------------------------------------------

    /// <summary>Loads displays, windows, the last frame, style and hotkeys. Call once, on the UI thread.</summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        RefreshDisplays();
        Guard(RefreshWindows);

        var start = _settings.LastFrame ?? _settings.Profiles.FirstOrDefault(p => p.Id == _settings.ActiveProfileId);
        if (start is not null) ApplyFrameState(start);

        _overlay.ApplyStyle(_style);
        _overlay.SetCaptureExclusion(_hideFromRecorders);
        RegisterHotkeys();
        RebuildProfiles();
        UpdateFrame(Summary());
        SetStatus("Choose a size, then show the outline.");
    }

    /// <summary>Remembers the current frame and hides the outline. Call when the main window closes.</summary>
    public void Shutdown()
    {
        StopFollowingWindow();
        _settings.LastFrame = Snapshot("last-session", "Last session");
        SaveSettings();
        if (_overlay.IsVisible) _overlay.Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _overlay.FrameMoved -= OnOverlayFrameMoved;
        _overlay.VisibilityChanged -= OnOverlayVisibilityChanged;
        _windowService.TrackedWindowChanged -= OnTrackedWindowChanged;
        StopFollowingWindow();
        _displayService.DisplaysChanged -= OnDisplaysChanged;
        _hotkeys.Pressed -= OnHotkeyPressed;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void SetMode(FrameMode mode)
    {
        if (IsBusy || _mode == mode) return;
        if (_mode == FrameMode.Resize && mode != FrameMode.Resize) StopFollowingWindow();
        _mode = mode;
        if (_overlay.IsVisible && mode == FrameMode.Resize && _activeFrame is null) _overlay.Hide();
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsOutlineMode));
        OnPropertyChanged(nameof(IsResizeMode));
        NotifyPanels();
        PrimaryCommand.NotifyCanExecuteChanged();
        NotifyStage();
        NotifyFooter();
        NotifyProfileHeader();
        SetStatus(Summary());
    }

    // ---- Status ---------------------------------------------------------------------------------

    public string StatusTitle =>
        _statusWarning ? "Needs attention"
        : IsBusy ? "Updating window"
        : _overlay.IsVisible ? "Outline is live"
        : IsResizeMode ? (CanRestore ? "Window resized" : "Ready to resize")
        : "Ready to frame";

    public string StatusDetail => _statusMessage;

    public StatusKind StatusKind =>
        _statusWarning ? StatusKind.Warning
        : IsBusy ? StatusKind.Busy
        : _overlay.IsVisible || (IsResizeMode && CanRestore) ? StatusKind.Live
        : StatusKind.Ready;

    private void SetStatus(string message, bool warning = false)
    {
        _statusMessage = message;
        _statusWarning = warning;
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusKind));
    }

    private void ReportError(Exception error)
    {
        string message = error is Win32Exception { NativeErrorCode: 5 }
                ? "Windows denied the change. If the selected app runs as administrator, ExactFrame needs matching permissions."
            : error.Message;
        SetStatus(message, warning: true);
    }

    private static bool IsExpected(Exception ex) =>
        ex is InvalidOperationException or ExternalException or ArgumentException or IOException or UnauthorizedAccessException;

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ReportError(ex);
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        PrimaryCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (!_disposed) ReportError(ex);
        }
        finally
        {
            IsBusy = false;
            PrimaryCommand.NotifyCanExecuteChanged();
            RestoreCommand.NotifyCanExecuteChanged();
            NotifyResizeState();
        }
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Settings couldn’t be saved: {ex.Message}", warning: true);
        }
    }

    private static void SelectOnly<T>(IEnumerable<T> items, Func<T, bool> isSelected) where T : SelectableItem
    {
        foreach (var item in items) item.IsSelected = isSelected(item);
    }
}
