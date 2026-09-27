using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ExactFrame.Core.Services;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;
using ExactFrame.Hud;
using ExactFrame.Native;
using ExactFrame.Services;

namespace ExactFrame;

/// <summary>Composition root: wires services, the view model and both windows together.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _mainWindow;
    private HudWindow? _hud;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _services = ConfigureServices();

        _mainWindow = _services.GetRequiredService<MainWindow>();
        _services.GetRequiredService<DialogService>().Attach(() => _mainWindow.Content?.XamlRoot);
        _hud = _services.GetRequiredService<HudWindow>();
        _hud.OpenMainRequested += (_, _) => _mainWindow.BringToFront();
        _mainWindow.Closed += OnMainWindowClosed;

        _services.GetRequiredService<MainViewModel>().Initialize();
        _mainWindow.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Platform services (Win32). All are created on the UI thread.
        services.AddSingleton<MessageWindow>();
        services.AddSingleton<WindowService>();
        services.AddSingleton<IWindowService>(sp => sp.GetRequiredService<WindowService>());
        services.AddSingleton<IDisplayService, DisplayService>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IOutlineOverlay, OutlineOverlay>();
        services.AddSingleton<IWindowPicker, WindowPicker>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        string settingsPath = JsonSettingsStore.DefaultPath();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(settingsPath));
        services.AddSingleton<IAppInfo>(_ => new AppInfo(settingsPath));
        services.AddSingleton<IShellService, ShellService>();

        // Presentation
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HelpViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<HudWindow>();

        return services.BuildServiceProvider();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_services is null) return;
        _services.GetRequiredService<MainViewModel>().Shutdown();
        _hud?.Close();
        _services.Dispose();
        _services = null;
        Exit();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        Debug.WriteLine($"Unhandled: {e.Exception}");
}
