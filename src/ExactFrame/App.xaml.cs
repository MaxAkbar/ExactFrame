using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ExactFrame.Core.Services;
using ExactFrame.Core.Obs;
using ExactFrame.Core.Integration;
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
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly CancellationTokenSource _launchLifetime = new();

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
        _services.GetRequiredService<ProfileTransferService>().Attach(
            WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow), () => _mainWindow.Content?.XamlRoot);
        _services.GetRequiredService<OutputPreviewService>().Attach(_mainWindow);
        _services.GetRequiredService<SnapStudioCaptureService>().Attach(_mainWindow);
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
        if (arguments.Any(argument => !string.Equals(argument, StartupService.BackgroundArgument, StringComparison.OrdinalIgnoreCase)))
            _mainWindow.DispatcherQueue.TryEnqueue(async () => await HandleLaunchAsync(arguments));
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
        services.AddSingleton<ProfileTransferService>();
        services.AddSingleton<IProfileTransferService>(sp => sp.GetRequiredService<ProfileTransferService>());
        services.AddSingleton<OutputPreviewService>();
        services.AddSingleton<IOutputPreviewService>(sp => sp.GetRequiredService<OutputPreviewService>());
        services.AddSingleton<SnapStudioCaptureService>();
        services.AddSingleton<ISnapStudioCaptureService>(sp => sp.GetRequiredService<SnapStudioCaptureService>());
        string? dataRoot = Environment.GetEnvironmentVariable("EXACTFRAME_DATA_ROOT");
        string databasePath = string.IsNullOrWhiteSpace(dataRoot) ? CSharpDbAppStore.DefaultPath() : Path.Combine(dataRoot, "exactframe.db");
        string settingsPath = string.IsNullOrWhiteSpace(dataRoot) ? JsonSettingsStore.DefaultPath() : Path.Combine(dataRoot, "settings.json");
        string catalogPath = string.IsNullOrWhiteSpace(dataRoot) ? CommandCatalog.DefaultPath : Path.Combine(dataRoot, "command-palette.json");
        services.AddSingleton<ICommandCatalogPublisher>(_ => new CommandCatalogPublisher(catalogPath,
            Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ExactFrame.exe")));
        services.AddSingleton<IPowerToysService>(_ => new PowerToysService());
        services.AddSingleton<Func<IPowerToysService>>(sp => () => sp.GetRequiredService<IPowerToysService>());
        services.AddSingleton(_ => new CSharpDbAppStore(databasePath, settingsPath));
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
        services.AddSingleton<Func<PowerToysViewModel>>(sp => () => new PowerToysViewModel(
            sp.GetRequiredService<MainViewModel>(), sp.GetRequiredService<IPowerToysService>(), sp.GetRequiredService<IClipboardService>()));
        services.AddSingleton<HudWindow>();
        services.AddSingleton<TrayIcon>();

        return services.BuildServiceProvider();
    }

    private static bool IsBackgroundLaunch(IEnumerable<string> arguments) =>
        arguments.Contains(StartupService.BackgroundArgument, StringComparer.OrdinalIgnoreCase);

    /// <summary>Someone started ExactFrame again: show this copy, unless Windows started it at sign-in.</summary>
    private void OnLaunchForwarded(object? sender, IReadOnlyList<string> arguments)
    {
        // Return to WM_COPYDATA immediately; execute asynchronous commands on the UI dispatcher.
        _mainWindow?.DispatcherQueue.TryEnqueue(async () => await HandleLaunchAsync(arguments));
    }

    private async Task HandleLaunchAsync(IReadOnlyList<string> arguments)
    {
        try
        {
            await _launchGate.WaitAsync(_launchLifetime.Token);
            try
            {
                if (_services is null || _mainWindow is null) return;
                var main = _services.GetRequiredService<MainViewModel>();
                var command = LaunchCommand.Parse(arguments);
                if (!command.Background) _mainWindow.BringToFront();
                if (command.DelayMilliseconds > 0) await Task.Delay(command.DelayMilliseconds, _launchLifetime.Token);
                if (!await main.ExecuteLaunchAsync(command)) { _mainWindow.BringToFront(); return; }
                foreach (var action in command.Actions)
                {
                    if (action is not (FrameAction.OpenObs or FrameAction.OpenPowerToys)) continue;
                    _mainWindow.BringToFront();
                    // A background cold launch needs one layout pass before displaying a dialog.
                    for (int i = 0; i < 50 && _mainWindow.Content?.XamlRoot is null; i++)
                        await Task.Delay(50, _launchLifetime.Token);
                    if (action == FrameAction.OpenObs) await _mainWindow.ShowObsAsync();
                    else await _mainWindow.ShowPowerToysAsync();
                }
            }
            finally { _launchGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            _services?.GetRequiredService<MainViewModel>().ReportLaunchError(error.Message);
            _mainWindow?.BringToFront();
        }
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
        _launchLifetime.Cancel();
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
