using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ExactFrame.Core.ViewModels;
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
    private const int MinimumWidth = 1040;
    private const int MinimumHeight = 700;

    private readonly HelpView _helpView;

    public MainWindow(MainViewModel viewModel, HelpViewModel help)
    {
        ViewModel = viewModel;
        Help = help;
        InitializeComponent();

        PanelHost.Children.Add(new FramePanel(viewModel));
        PanelHost.Children.Add(new StylePanel(viewModel));
        PanelHost.Children.Add(new ProfilesPanel(viewModel));
        PanelHost.Children.Add(new ResizePanel(viewModel));
        _helpView = new HelpView(help, viewModel);
        HelpHost.Children.Add(_helpView);

        ConfigureWindow();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarRegions();
        ModeSwitch.SizeChanged += (_, _) => UpdateTitleBarRegions();
        ProfileButton.SizeChanged += (_, _) => UpdateTitleBarRegions();
        HelpButton.SizeChanged += (_, _) => UpdateTitleBarRegions();
        Closed += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public MainViewModel ViewModel { get; }

    public HelpViewModel Help { get; }

    /// <summary>Restores the window if minimized and brings it to the front.</summary>
    public void BringToFront()
    {
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
        titleBar.ButtonForegroundColor = Format.ColorFromHex("#3C4F4D");
        titleBar.ButtonInactiveForegroundColor = Format.ColorFromHex("#8FA19F");
        titleBar.ButtonHoverBackgroundColor = Format.ColorFromHex("#14122422");
        titleBar.ButtonHoverForegroundColor = Format.ColorFromHex("#122422");
        titleBar.ButtonPressedBackgroundColor = Format.ColorFromHex("#24122422");

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

    /// <summary>Lets clicks reach the mode switch and profile button, which sit inside the draggable title bar.</summary>
    private void UpdateTitleBarRegions()
    {
        if (Content?.XamlRoot is not { } root) return;
        double scale = root.RasterizationScale;
        CaptionButtonsColumn.Width = new GridLength(Math.Max(0, AppWindow.TitleBar.RightInset / scale));

        var regions = new[] { RegionFor(ModeSwitch, scale), RegionFor(HelpButton, scale), RegionFor(ProfileButton, scale) };
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, regions);
    }

    private static RectInt32 RegionFor(FrameworkElement element, double scale)
    {
        var bounds = element.TransformToVisual(null).TransformBounds(
            new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
    }

    private void OnTabSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectedTab = sender.SelectedItem == StyleTab ? SettingsTab.Style
            : sender.SelectedItem == ProfilesTab ? SettingsTab.Profiles
            : SettingsTab.Frame;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
