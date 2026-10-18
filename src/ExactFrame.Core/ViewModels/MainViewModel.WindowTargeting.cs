using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

public sealed partial class MainViewModel
{
    private string? _targetProcessName;
    private string _targetExecutablePath = string.Empty;
    private string _targetTitle = string.Empty;
    private string _targetClassName = string.Empty;
    private WindowTitleMatchMode _targetTitleMode;

    public ObservableCollection<WindowItem> MatchingWindows { get; } = [];
    public IRelayCommand UseSelectedWindowMatchCommand { get; private set; } = null!;
    public IRelayCommand ClearWindowMatchCommand { get; private set; } = null!;
    public IAsyncRelayCommand UpdateTargetProfileCommand { get; private set; } = null!;
    public bool HasActiveResizeProfile => IsResizeMode && ActiveProfile?.Mode == FrameMode.Resize;
    public string TargetApplicationText => _targetProcessName is null ? "Choose a window or enter its application process name." : $"Application: {_targetProcessName}.exe";
    public string TargetProcessName
    {
        get => _targetProcessName ?? string.Empty;
        set
        {
            string? process = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (process?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true) process = process[..^4];
            if (SetProperty(ref _targetProcessName, process)) TargetMatchChanged();
        }
    }
    public string TargetExecutablePath { get => _targetExecutablePath; set { if (SetProperty(ref _targetExecutablePath, value ?? string.Empty)) TargetMatchChanged(); } }
    public string TargetTitle { get => _targetTitle; set { if (SetProperty(ref _targetTitle, value ?? string.Empty)) TargetMatchChanged(); } }
    public string TargetClassName { get => _targetClassName; set { if (SetProperty(ref _targetClassName, value ?? string.Empty)) TargetMatchChanged(); } }
    public int TargetTitleModeIndex
    {
        get => (int)_targetTitleMode;
        set { if (value is >= 0 and <= 1 && SetProperty(ref _targetTitleMode, (WindowTitleMatchMode)value)) TargetMatchChanged(); }
    }

    private WindowMatchCriteria DraftTargetMatch => new()
    { ExecutablePath = _targetExecutablePath, Title = _targetTitle, ClassName = _targetClassName, TitleMode = _targetTitleMode };
    private WindowMatchCriteria? CurrentTargetMatch => WindowMatcher.Normalize(DraftTargetMatch);
    public string TargetMatchError => _targetProcessName is { } process && (process.Length > 120 ||
        process.Any(char.IsControl) || process.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        ? "Use the application process name, such as msedge, without a folder path."
        : WindowMatcher.ValidationError(DraftTargetMatch) ?? string.Empty;
    public bool CanResizeSelectedWindow => _targetWindow is { } window && TargetMatchError.Length == 0 &&
        WindowMatcher.Matches(window, _targetProcessName, CurrentTargetMatch);
    public string TargetMatchSummary => TargetMatchError.Length > 0 ? TargetMatchError : _targetProcessName is null
        ? "Choose an application to preview matching windows."
        : MatchingWindows.Count == 0 ? "No matching windows. Check the conditions or refresh after opening the app."
        : MatchingWindows.Count == 1 ? "1 matching window. Select it below to preview; resizing is a separate action."
        : $"{MatchingWindows.Count} matching windows. Select one below or narrow the conditions. Nothing is resized by previewing.";
    public string TargetWindowPreview => _targetWindow is not { } window ? "No window selected."
        : $"Selected: {window.Title}\n{window.ExecutablePath ?? "Executable path unavailable"}\nClass: {window.ClassName ?? "unavailable"}\n" +
          $"{window.Bounds.Width} × {window.Bounds.Height} px at {window.Bounds.X}, {window.Bounds.Y}" +
          (window.IsMinimized ? " · minimized" : string.Empty) +
          (CanResizeSelectedWindow ? string.Empty : "\nThis window does not match the current conditions.");

    private void LoadTargetMatch(string? processName, WindowMatchCriteria? criteria)
    {
        _targetProcessName = processName;
        _targetExecutablePath = criteria?.ExecutablePath ?? string.Empty;
        _targetTitle = criteria?.Title ?? string.Empty;
        _targetClassName = criteria?.ClassName ?? string.Empty;
        _targetTitleMode = criteria?.TitleMode ?? WindowTitleMatchMode.Contains;
        OnPropertyChanged(nameof(TargetExecutablePath)); OnPropertyChanged(nameof(TargetTitle));
        OnPropertyChanged(nameof(TargetClassName)); OnPropertyChanged(nameof(TargetTitleModeIndex));
        OnPropertyChanged(nameof(TargetProcessName));
        TargetMatchChanged();
    }

    private void UseSelectedWindowMatch()
    {
        if (_targetWindow is not { } window) return;
        LoadTargetMatch(window.ProcessName, new()
        { ExecutablePath = window.ExecutablePath, Title = window.Title, ClassName = window.ClassName, TitleMode = WindowTitleMatchMode.Exact });
    }

    private void TargetMatchChanged()
    {
        NotifyResizeState();
        NotifyProfileHeader();
    }

    private void RefreshMatchingWindows()
    {
        MatchingWindows.Clear();
        if (TargetMatchError.Length == 0)
            foreach (var window in _allWindows.Where(w => WindowMatcher.Matches(w, _targetProcessName, CurrentTargetMatch)).OrderBy(w => w.ZOrder))
                MatchingWindows.Add(new WindowItem(window, () => { PreviewMatchingWindow(window); return Task.CompletedTask; })
                { IsSelected = window.IsSameWindow(_targetWindow) });
        OnPropertyChanged(nameof(TargetApplicationText)); OnPropertyChanged(nameof(TargetMatchError));
        OnPropertyChanged(nameof(TargetMatchSummary)); OnPropertyChanged(nameof(TargetWindowPreview));
        OnPropertyChanged(nameof(CanResizeSelectedWindow));
        PrimaryCommand.NotifyCanExecuteChanged();
        UseSelectedWindowMatchCommand?.NotifyCanExecuteChanged();
    }

    private void PreviewMatchingWindow(WindowInfo window)
    {
        if (IsBusy) return;
        // Refresh metadata first: titles and the open-window set can change while the preview is visible.
        try { RefreshWindows(); }
        catch (Exception ex) when (IsExpected(ex)) { ReportError(ex); return; }
        var current = _allWindows.FirstOrDefault(w => w.IsSameWindow(window));
        if (current is null || !WindowMatcher.Matches(current, _targetProcessName, CurrentTargetMatch))
        { SetStatus("That window no longer matches. Refresh the matches and choose again.", warning: true); return; }
        SetTarget(current);
        if (_anchor is null) KeepWindowPosition(); else UpdateFrame();
        SetStatus($"Previewing “{current.Title}”. Choose Resize window to apply the size.");
    }

    private bool ValidateTargetMatchForSave()
    {
        if (!IsResizeMode) return true;
        if (TargetMatchError.Length == 0 && (_targetProcessName is not null || CurrentTargetMatch is null)) return true;
        SetStatus(TargetMatchError.Length > 0 ? TargetMatchError : "Choose an application before saving matching conditions.", warning: true);
        return false;
    }
}
