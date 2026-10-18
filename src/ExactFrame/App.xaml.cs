using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ExactFrame.Core.Services;
using ExactFrame.Core.Obs;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;
using ExactFrame.Hud;
using ExactFrame.Native;
using ExactFrame.Services;

namespace ExactFrame;

/// <summary>
/// Composition root: wires services, the view model and both windows together. One copy runs per session; it lives
/// in the notification area, and closing the main window only hides it unless the user turned that off.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _mainWindow;
    private HudWindow? _hud;
    private TrayIcon? _tray;
    private Mutex? _instance;
    private bool _quitting;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] arguments = Environment.GetCommandLineArgs()[1..];
        _instance = SingleInstance.TryClaim();
        if (_instance is null)
        {
            SingleInstance.Forward(arguments);
            Exit();
            return;
        }

        _services = ConfigureServices();

        _mainWindow = _services.GetRequiredService<MainWindow>();
        _services.GetRequiredService<DialogService>().Attach(() => _mainWindow.Content?.XamlRoot);
        _hud = _services.GetRequiredService<HudWindow>();
        _hud.OpenMainRequested += (_, _) => _mainWindow.BringToFront();
        _mainWindow.AppWindow.Closing += OnMainWindowClosing;
        _mainWindow.Closed += OnMainWindowClosed;

        _tray = _services.GetRequiredService<TrayIcon>();
        _tray.OpenRequested += (_, _) => _mainWindow.BringToFront();
        // Quit after the menu has closed, not from inside the tray window's own message.
        _tray.QuitRequested += (_, _) => _mainWindow.DispatcherQueue.TryEnqueue(Quit);
        _services.GetRequiredService<MessageWindow>().LaunchForwarded += OnLaunchForwarded;

        _services.GetRequiredService<MainViewModel>().Initialize();
        if (!IsBackgroundLaunch(arguments)) _mainWindow.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Platform services (Win32). All are created on the UI thread.
        services.AddSingleton<MessageWindow>();
        services.AddSingleton<TitleBarDetector>();
        services.AddSingleton<WindowService>();
        services.AddSingleton<IWindowService>(sp => sp.GetRequiredService<WindowService>());
        services.AddSingleton<IDisplayService, DisplayService>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IOutlineOverlay, OutlineOverlay>();
        services.AddSingleton<IWindowPicker, WindowPicker>();
        services.AddSingleton<IFrameRegionPicker, FrameRegionPicker>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        string databasePath = CSharpDbAppStore.DefaultPath();
        services.AddSingleton(_ => new CSharpDbAppStore(databasePath, JsonSettingsStore.DefaultPath()));
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<CSharpDbAppStore>());
        services.AddSingleton<IAppDataStore>(sp => sp.GetRequiredService<CSharpDbAppStore>());
        services.AddSingleton<IAppInfo>(_ => new AppInfo(databasePath));
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IObsClient>(_ => new ObsWebSocketClient());
        services.AddSingleton<IObsService, ObsService>();

        // Presentation
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HelpViewModel>();
        services.AddSingleton<ObsViewModel>();
        services.AddSingleton<Func<RecordingSetupViewModel>>(sp => () => new RecordingSetupViewModel(
            sp.GetRequiredService<MainViewModel>(), sp.GetRequiredService<IAppInfo>(),
            sp.GetRequiredService<IDisplayService>(), sp.GetRequiredService<IClipboardService>()));
        services.AddSingleton<MainWindow>();
        services.AddSingleton<HudWindow>();
        services.AddSingleton<TrayIcon>();

        return services.BuildServiceProvider();
    }

    private static bool IsBackgroundLaunch(IEnumerable<string> arguments) =>
        arguments.Contains(StartupService.BackgroundArgument, StringComparer.OrdinalIgnoreCase);

    /// <summary>Someone started ExactFrame again: show this copy, unless Windows started it at sign-in.</summary>
    private void OnLaunchForwarded(object? sender, IReadOnlyList<string> arguments)
    {
        if (!IsBackgroundLaunch(arguments)) _mainWindow?.BringToFront();
    }

    private void OnMainWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_quitting || _services is null) return;
        var viewModel = _services.GetRequiredService<MainViewModel>();
        if (!viewModel.KeepRunningInBackground) return;

        args.Cancel = true;
        sender.Hide();
        if (viewModel.MoveToBackground())
        {
            _tray?.ShowNotice("ExactFrame is still running",
                "Shortcuts and the outline keep working. Select this icon to open ExactFrame, or right-click it to quit.");
        }
    }

    private void Quit()
    {
        _quitting = true;
        _mainWindow?.Close();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_services is null) return;
        _services.GetRequiredService<MainViewModel>().Shutdown();
        _hud?.Close();
        _services.Dispose();
        _services = null;
        _instance?.ReleaseMutex();
        _instance?.Dispose();
        Exit();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        Debug.WriteLine($"Unhandled: {e.Exception}");
}
