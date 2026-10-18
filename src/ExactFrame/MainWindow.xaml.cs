using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ExactFrame.Core.ViewModels;
using ExactFrame.Core.Models;
using ExactFrame.Helpers;
using ExactFrame.Interop;
using ExactFrame.Views;
using Windows.Graphics;

namespace ExactFrame;

/// <summary>The main window: custom title bar, preview stage, settings panels and action footer.</summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 860;
    private const int MinimumWidth = 640;
    private const int MinimumHeight = 480;

    private readonly HelpView _helpView;
    private readonly ObsViewModel _obs;
    private readonly Func<RecordingSetupViewModel> _recordingSetup;
    private bool _obsOpen;
    private bool _setupOpen;
    private bool _previewExpanded = true;
    private bool _compact;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private readonly Microsoft.UI.System.ThemeSettings _themeSettings;

    public MainWindow(MainViewModel viewModel, HelpViewModel help, ObsViewModel obs, Func<RecordingSetupViewModel> recordingSetup)
    {
        ViewModel = viewModel;
        Help = help;
        _obs = obs;
        _recordingSetup = recordingSetup;
        InitializeComponent();

        PanelHost.Children.Add(new FramePanel(viewModel));
        PanelHost.Children.Add(new StylePanel(viewModel));
        PanelHost.Children.Add(new ProfilesPanel(viewModel));
        PanelHost.Children.Add(new ResizePanel(viewModel));
        _helpView = new HelpView(help, viewModel);
        HelpHost.Children.Add(_helpView);

        ConfigureWindow();
        _themeSettings = Microsoft.UI.System.ThemeSettings.CreateForWindowId(AppWindow.Id);
        Root.SizeChanged += (_, _) => UpdateLayout();
        PreviewScroller.SizeChanged += (_, _) => ApplyPreviewLayout();
        Root.ActualThemeChanged += (_, _) => RefreshTheme();
        Root.Loaded += (_, _) => { ApplyThemePreference(); UpdateLayout(); };
        _uiSettings.ColorValuesChanged += OnSystemColorsChanged;
        _uiSettings.TextScaleFactorChanged += OnTextScaleChanged;
        _themeSettings.Changed += OnHighContrastChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarRegions();
        ModeSwitch.SizeChanged += (_, _) => UpdateTitleBarRegions();
        ProfileButton.SizeChanged += (_, _) => UpdateTitleBarRegions();
        HelpButton.SizeChanged += (_, _) => UpdateTitleBarRegions();
        CheckSetupButton.SizeChanged += (_, _) => UpdateTitleBarRegions();
        ApplyThemePreference();
        Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _uiSettings.ColorValuesChanged -= OnSystemColorsChanged;
            _uiSettings.TextScaleFactorChanged -= OnTextScaleChanged;
            _themeSettings.Changed -= OnHighContrastChanged;
        };
    }

    public MainViewModel ViewModel { get; }

    public HelpViewModel Help { get; }

    private async void OnCheckSetupClick(object sender, RoutedEventArgs e)
    {
        if (_setupOpen || Content?.XamlRoot is not { } root) return;
        _setupOpen = true;
        using var setup = _recordingSetup();
        var dialog = new RecordingSetupDialog(setup) { XamlRoot = root };
        AppThemeHelper.MatchOwner(dialog);
        try { await dialog.ShowAsync(); }
        catch (System.Runtime.InteropServices.COMException) { /* Another dialog is already open. */ }
        finally { _setupOpen = false; }
    }

    private async void OnSendToObsClick(object sender, RoutedEventArgs e)
    {
        if (_obsOpen || Content?.XamlRoot is not { } root) return;
        _obsOpen = true;
        var dialog = new ObsDialog(_obs) { XamlRoot = root };
        AppThemeHelper.MatchOwner(dialog);
        try { await dialog.ShowAsync(); }
        catch (System.Runtime.InteropServices.COMException) { /* Another dialog is already open. */ }
        finally { _obsOpen = false; }
    }

    /// <summary>Shows the window if it's in the notification area or minimized, and brings it to the front.</summary>
    public void BringToFront()
    {
        if (!AppWindow.IsVisible) AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        // Caption colors are refreshed with the app theme, including Windows contrast colors.

        string icon = Path.Combine(AppContext.BaseDirectory, "ExactFrame.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        // Size in physical pixels for the monitor the window opens on, then center it in the work area.
        nint hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        double scale = Math.Max(1, NativeMethods.GetDpiForWindow(hwnd)) / 96d;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)(DefaultWidth * scale), workArea.Width);
        int height = Math.Min((int)(DefaultHeight * scale), workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min((int)(MinimumWidth * scale), workArea.Width);
            presenter.PreferredMinimumHeight = Math.Min((int)(MinimumHeight * scale), workArea.Height);
        }
    }

    /// <summary>Keeps the system caption-button inset clear when the custom title bar changes size.</summary>
    private void UpdateTitleBarRegions()
    {
        if (Content?.XamlRoot is not { } root) return;
        double scale = root.RasterizationScale;
        CaptionButtonsColumn.Width = new GridLength(Math.Max(0, AppWindow.TitleBar.RightInset / scale));

        // All interactive controls now live below the draggable title bar.
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, []);
    }

    private void ApplyThemePreference()
    {
        Root.RequestedTheme = AppThemeHelper.ForPreference(ViewModel.ThemePreference);
        RefreshTheme();
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string theme } && Enum.TryParse<AppThemePreference>(theme, out var preference))
            ViewModel.ThemePreference = preference;
    }

    private void RefreshTheme()
    {
        Format.UpdateTheme(Root.ActualTheme, _themeSettings.HighContrast);
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = Format.ThemeBrush("InkSoftBrush").Color;
        titleBar.ButtonInactiveForegroundColor = Format.ThemeBrush("MutedBrush").Color;
        titleBar.ButtonHoverBackgroundColor = Format.ThemeBrush("HoverBrush").Color;
        titleBar.ButtonHoverForegroundColor = Format.ThemeBrush("InkBrush").Color;
        titleBar.ButtonPressedBackgroundColor = Format.ThemeBrush("PressedBrush").Color;
        titleBar.ButtonPressedForegroundColor = Format.ThemeBrush("InkBrush").Color;
    }

    private void OnSystemColorsChanged(Windows.UI.ViewManagement.UISettings sender, object args) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => RefreshTheme());

    private void OnHighContrastChanged(Microsoft.UI.System.ThemeSettings sender, object args) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => RefreshTheme());

    private void OnTextScaleChanged(Windows.UI.ViewManagement.UISettings sender, object args) =>
        DispatcherQueue.TryEnqueue(UpdateLayout);

    private void OnPreviewToggleClick(object sender, RoutedEventArgs e)
    {
        _previewExpanded = !_previewExpanded;
        ApplyPreviewLayout();
    }

    private void UpdateLayout()
    {
        double textScale = Math.Max(1, _uiSettings.TextScaleFactor);
        bool compact = Root.ActualWidth < 1100 * Math.Min(textScale, 1.5);
        if (compact != _compact)
        {
            _compact = compact;
            _previewExpanded = !compact;
        }
        ApplyPreviewLayout();
        _helpView.UpdateLayoutForTextScale(textScale);
        bool stacked = Root.ActualWidth < 1200 * textScale;
        Grid.SetRow(FooterActions, stacked ? 1 : 0);
        Grid.SetColumn(FooterActions, stacked ? 0 : 1);
        FooterStatus.Margin = new Thickness(0, 0, 0, stacked ? 10 : 0);
    }

    private void ApplyPreviewLayout()
    {
        if (!_previewExpanded && PreviewMap.IsInteracting) PreviewMap.CancelInteraction();
        PreviewScroller.Visibility = _previewExpanded ? Visibility.Visible : Visibility.Collapsed;
        double textScale = Math.Max(1, _uiSettings.TextScaleFactor);
        PreviewStage.Height = Math.Max(520 * textScale, PreviewScroller.ActualHeight);
        PreviewBounds.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        SettingsCard.Visibility = _compact && _previewExpanded ? Visibility.Collapsed : Visibility.Visible;
        PreviewColumn.Width = new GridLength(_previewExpanded ? 1 : 0, GridUnitType.Star);
        SettingsColumn.Width = _compact || !_previewExpanded ? new GridLength(1, GridUnitType.Star) : new GridLength(420 * textScale);
        Grid.SetColumnSpan(PreviewScroller, _compact ? 2 : 1);
        PreviewToggle.Content = _previewExpanded ? (_compact ? "Show controls" : "Hide preview") : "Show preview";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(PreviewToggle,
            _compact ? "Switch between the preview and controls in this compact window." : "Collapse the preview to give controls more space.");
    }

    private void OnTabSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectedTab = sender.SelectedItem == StyleTab ? SettingsTab.Style
            : sender.SelectedItem == ProfilesTab ? SettingsTab.Profiles
            : SettingsTab.Frame;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ThemePreference)) ApplyThemePreference();
        if (e.PropertyName is not (nameof(MainViewModel.SelectedTab) or "" or null)) return;
        var item = ViewModel.SelectedTab switch
        {
            SettingsTab.Style => StyleTab,
            SettingsTab.Profiles => ProfilesTab,
            _ => FrameTab
        };
        if (Tabs.SelectedItem != item) Tabs.SelectedItem = item;
    }

    /// <summary>Opens help at the topic for the current mode and tab.</summary>
    private void OpenHelp()
    {
        Help.Open(HelpViewModel.TopicFor(ViewModel.Mode, ViewModel.SelectedTab));
        // The help view only becomes focusable after the next layout pass.
        DispatcherQueue.TryEnqueue(_helpView.FocusSearch);
    }

    private void OnHelpButtonClick(object sender, RoutedEventArgs e)
    {
        if (Help.IsOpen) Help.IsOpen = false;
        else OpenHelp();
    }

    private void OnHelpInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenHelp();
        args.Handled = true;
    }

    /// <summary>The mode switch and profile button take you back to the main view.</summary>
    private void OnTitleBarNavigate(object sender, RoutedEventArgs e) => Help.IsOpen = false;

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Content?.XamlRoot is { } root && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is ExactFrame.Controls.NestedPositionMap crop && crop.IsInteracting)
        {
            crop.CancelInteraction();
            args.Handled = true;
            return;
        }
        if (PreviewMap.IsInteracting)
        {
            PreviewMap.CancelInteraction();
            args.Handled = true;
            return;
        }
        if (Help.IsOpen)
        {
            Help.IsOpen = false;
            args.Handled = true;
            return;
        }

        if (!ViewModel.IsOutlineVisible || ViewModel.IsBusy) return;
        ViewModel.HideOutlineCommand.Execute(null);
        args.Handled = true;
    }
}
