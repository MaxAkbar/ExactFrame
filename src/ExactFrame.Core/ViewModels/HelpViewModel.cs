using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Help;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;

namespace ExactFrame.Core.ViewModels;

public sealed class HelpTopicItem(HelpTopic topic, Action select) : SelectableItem(select)
{
    public HelpTopic Topic { get; } = topic;

    public string Title => Topic.Title;

    public string Glyph => Topic.Glyph;
}

public sealed record AboutRow(string Label, string Value);

/// <summary>The Help and About page: topics, search, and version details. Runs on the UI thread.</summary>
public sealed class HelpViewModel : ObservableObject
{
    private readonly IAppInfo _appInfo;
    private readonly IShellService _shell;
    private readonly IClipboardService _clipboard;
    private readonly IDisplayService _displays;
    private readonly IReadOnlyList<HelpTopicItem> _allTopics;
    private HelpTopic _selectedTopic;
    private string _searchText = string.Empty;
    private bool _isOpen;
    private bool _isInfoCopied;
    private int _copyVersion;

    public HelpViewModel(IAppInfo appInfo, IShellService shell, IClipboardService clipboard, IDisplayService displays)
    {
        _appInfo = appInfo;
        _shell = shell;
        _clipboard = clipboard;
        _displays = displays;
        _allTopics = [.. HelpContent.Topics.Select(t => new HelpTopicItem(t, () => SelectedTopic = t))];
        _selectedTopic = HelpContent.Topics[0];
        VisibleTopics = [.. _allTopics];
        SyncSelection();

        CloseCommand = new RelayCommand(() => IsOpen = false);
        RevealSettingsCommand = new RelayCommand(RevealSettings);
        CopySystemInfoCommand = new AsyncRelayCommand(CopySystemInfoAsync);
    }

    public IRelayCommand CloseCommand { get; }

    public IRelayCommand RevealSettingsCommand { get; }

    public IAsyncRelayCommand CopySystemInfoCommand { get; }

    public ObservableCollection<HelpTopicItem> VisibleTopics { get; }

    public IReadOnlyList<ShortcutRow> FixedShortcuts => HelpContent.FixedShortcuts;

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    public HelpTopic SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            if (!SetProperty(ref _selectedTopic, value)) return;
            SyncSelection();
            OnPropertyChanged(nameof(ShowShortcuts));
            OnPropertyChanged(nameof(ShowAbout));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty)) ApplySearch();
        }
    }

    public bool HasNoResults => VisibleTopics.Count == 0;

    public bool ShowShortcuts => _selectedTopic.Extra == HelpTopicExtra.Shortcuts;

    public bool ShowAbout => _selectedTopic.Extra == HelpTopicExtra.About;

    public string AppTitle => $"ExactFrame {_appInfo.Version}";

    public IReadOnlyList<AboutRow> AboutRows =>
    [
        new("Version", _appInfo.Version),
        new(".NET", _appInfo.RuntimeVersion),
        new("Windows App SDK", _appInfo.WindowsAppSdkVersion),
        new("Windows", _appInfo.OperatingSystem),
        new("App database", _appInfo.DatabasePath)
    ];

    public bool IsInfoCopied
    {
        get => _isInfoCopied;
        private set
        {
            if (!SetProperty(ref _isInfoCopied, value)) return;
            OnPropertyChanged(nameof(CopyInfoLabel));
        }
    }

    public string CopyInfoLabel => _isInfoCopied ? "Copied" : "Copy system info";

    /// <summary>The topic that explains what the user is looking at.</summary>
    public static string TopicFor(FrameMode mode, SettingsTab tab) => mode == FrameMode.Resize
        ? HelpContent.ResizeWindow
        : tab switch
        {
            SettingsTab.Style => HelpContent.StyleAndGuides,
            SettingsTab.Profiles => HelpContent.Profiles,
            _ => HelpContent.OutlineArea
        };

    /// <summary>Opens help at a topic, clearing any earlier search so the topic is listed.</summary>
    public void Open(string? topicId = null)
    {
        SearchText = string.Empty;
        if (topicId is not null) SelectedTopic = HelpContent.Find(topicId);
        IsOpen = true;
    }

    /// <summary>Plain-text details to paste into a bug report.</summary>
    public string SystemInfo()
    {
        var text = new StringBuilder()
            .AppendLine(AppTitle)
            .AppendLine($"Windows: {_appInfo.OperatingSystem}")
            .AppendLine($".NET: {_appInfo.RuntimeVersion}")
            .AppendLine($"Windows App SDK: {_appInfo.WindowsAppSdkVersion}")
            .AppendLine("Displays:");

        foreach (var display in _displays.GetDisplays())
        {
            var b = display.Bounds;
            var w = display.WorkArea;
            text.AppendLine($"  {display.Title}: {b.Width} × {b.Height} at ({b.X}, {b.Y}), {display.ScalePercent}%" +
                            $", work area {w.Width} × {w.Height} at ({w.X}, {w.Y})" + (display.IsPrimary ? ", primary" : string.Empty));
        }

        return text.AppendLine($"Database: {_appInfo.DatabasePath}").ToString();
    }

    private void ApplySearch()
    {
        VisibleTopics.Clear();
        foreach (var item in _allTopics.Where(t => t.Topic.Matches(_searchText))) VisibleTopics.Add(item);
        if (VisibleTopics.Count > 0 && VisibleTopics.All(t => t.Topic != _selectedTopic))
            SelectedTopic = VisibleTopics[0].Topic;
        OnPropertyChanged(nameof(HasNoResults));
    }

    private void SyncSelection()
    {
        foreach (var item in _allTopics) item.IsSelected = item.Topic == _selectedTopic;
    }

    private void RevealSettings() => _shell.RevealInExplorer(_appInfo.DatabasePath);

    private async Task CopySystemInfoAsync()
    {
        _clipboard.SetText(SystemInfo());
        IsInfoCopied = true;
        int version = ++_copyVersion;
        await Task.Delay(1600);
        if (version == _copyVersion) IsInfoCopied = false;
    }
}
