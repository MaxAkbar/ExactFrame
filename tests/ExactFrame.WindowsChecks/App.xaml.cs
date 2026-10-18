using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using ExactFrame.Core.Models;
using ExactFrame.Core.Obs;
using ExactFrame.Core.Tests;
using ExactFrame.Core.ViewModels;
using ExactFrame.Hud;
using ExactFrame.Interop;
using ExactFrame.Native;
using ExactFrame.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using DRect = System.Drawing.Rectangle;

namespace ExactFrame.WindowsChecks;

/// <summary>Explicit, interactive-desktop checks. Never runs as part of the portable application.</summary>
public sealed partial class App : Application
{
    private readonly List<object> _checks = [];
    private readonly List<object> _clips = [];
    private string _output = Path.GetFullPath("artifacts/recording-checks/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
    private Window? _fixture;
    private IReadOnlyList<DisplayInfo> _monitors = [];
    private bool _nativePassed = true;
    private bool _responsivenessOnly;
    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        string? Value(string name) { int index = Array.IndexOf(arguments, name); return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null; }
        _output = Path.GetFullPath(Value("--output") ?? _output);
        _responsivenessOnly = arguments.Contains("--responsiveness-only");
        if (_responsivenessOnly && Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any())
        {
            Debug.WriteLine("Responsiveness output must be a new, empty directory. Existing evidence was preserved.");
            Environment.ExitCode = 2;
            Exit();
            return;
        }
        Directory.CreateDirectory(_output);
        try
        {
            if (arguments.Contains("--stop-isolated-recording"))
            {
                await StopIsolatedRecording(int.Parse(Value("--obs-port") ?? "4456"), Value("--obs-config")!);
                return;
            }
            DesktopState.RequireInteractiveDesktop();
            using var messages = new MessageWindow();
            using var displays = new DisplayService(messages);
            var monitors = displays.GetDisplays();
            _monitors = monitors;
            if (monitors.Count == 0) throw new InvalidOperationException("An unlocked interactive Windows desktop is required.");
            using var windows = new WindowService(new TitleBarDetector());
            if (arguments.Contains("--targeting-metadata-only"))
            {
                _nativePassed = TargetingMetadataChecks.Run(windows, _checks);
                WriteReport(monitors, null);
                if (!_nativePassed) Environment.ExitCode = 1;
                return;
            }
            _fixture = PatternWindow();
            var hwnd = Win32Interop.GetWindowFromWindowId(_fixture.AppWindow.Id);
            _fixture.Activate();
            await Task.Delay(800);
            uint thread = NativeMethods.GetWindowThreadProcessId(hwnd, out uint process);
            var target = new WindowInfo(hwnd, process, thread, "ExactFrame recording fixture", "ExactFrame.WindowsChecks", DRect.Empty);
            if (_responsivenessOnly)
            {
                await ResponsivenessChecks.Run(windows, target, monitors.First(m => m.IsPrimary), _output, _checks, Value("--performance-browser-title"));
                WriteReport(monitors, null);
                return;
            }
            foreach (var monitor in monitors)
            {
                if (monitor.WorkArea.Width < 1400 || monitor.WorkArea.Height < 900)
                { _checks.Add(new { kind = "nativeResize", monitor = monitor.Number, status = "not-tested", reason = "Needs 1400x900 work area" }); continue; }
                foreach (var area in new[] { WindowArea.VisibleFrame, WindowArea.Client })
                {
                    var desired = new DRect(monitor.WorkArea.X + 60, monitor.WorkArea.Y + 60, 1280, 720);
                    var result = await windows.ResizeAsync(target, desired, area, CancellationToken.None);
                    var independentlyRead = area == WindowArea.Client ? ClientBounds(hwnd) : VisibleBounds(hwnd);
                    _nativePassed &= result.Exact && independentlyRead == desired;
                    _checks.Add(new { kind = "nativeResize", monitor = monitor.Number, monitor.ScalePercent, area = area.ToString(), desired,
                        result.Actual, result.Exact, independentBounds = independentlyRead,
                        dpi = NativeMethods.GetDpiForWindow(hwnd), pass = result.Exact && independentlyRead == desired });
                }
            }
            await windows.RestoreAsync(target, CancellationToken.None);
            _checks.Add(new { kind = "monitorTransitions", status = monitors.Select(m => m.Dpi).Distinct().Count() > 1 ? "native-measurements-only" : "mixed-DPI-not-available",
                note = "Native fixture moved through the listed monitors; recorded transitions and browser behavior are separate checks." });
            if (Value("--browser-pid") is { } browserPid)
                _nativePassed &= await BrowserChecks.Run(windows, monitors, int.Parse(browserPid), _checks, Value("--browser-metrics"));

            if (Value("--obs-config") is { } obsConfig)
                await RecordChecks(displays, monitors, windows, target, int.Parse(Value("--obs-port") ?? "4456"), obsConfig);
            WriteReport(monitors, null);
            if (!_nativePassed) Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            if (arguments.Contains("--stop-isolated-recording"))
                File.WriteAllText(Path.Combine(_output, "recorder-cleanup.json"), JsonSerializer.Serialize(new { error = ex.ToString() }));
            else WriteReport(_monitors, ex.ToString());
            Environment.ExitCode = 1;
        }
        finally { _fixture?.Close(); Exit(); }
    }

    private async Task RecordChecks(DisplayService displays, IReadOnlyList<DisplayInfo> monitors, WindowService windows, WindowInfo target, int port, string configPath)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        using var client = new ObsWebSocketClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        await client.ConnectAsync(port, config.RootElement.GetProperty("server_password").GetString()!, token);
        var profile = await client.RequestAsync("GetProfileList", cancellation: token);
        if (profile.GetProperty("currentProfileName").GetString() != "ExactFrame QA")
            throw new InvalidOperationException("Refusing to change OBS: use an isolated portable profile named ExactFrame QA.");
        foreach (string request in new[] { "GetRecordStatus", "GetStreamStatus" })
            if ((await client.RequestAsync(request, cancellation: token)).GetProperty("outputActive").GetBoolean())
                throw new InvalidOperationException("Stop all outputs in the isolated OBS instance before checking.");
        var outputs = await client.RequestAsync("GetOutputList", cancellation: token);
        if (outputs.GetProperty("outputs").EnumerateArray().Any(o => o.GetProperty("outputActive").GetBoolean()))
            throw new InvalidOperationException("Stop all outputs in the isolated OBS instance before checking.");
        var version = await client.RequestAsync("GetVersion", cancellation: token);
        _checks.Add(new { kind = "recorder", version = version.GetProperty("obsVersion").GetString(), profile = "ExactFrame QA" });
        var inputs = await client.RequestAsync("GetInputList", cancellation: token);
        foreach (var audio in inputs.GetProperty("inputs").EnumerateArray().Where(i => i.GetProperty("inputKind").GetString()!.StartsWith("wasapi_", StringComparison.Ordinal)))
        {
            string name = audio.GetProperty("inputName").GetString()!;
            await client.RequestAsync("SetInputMute", new { inputName = name, inputMuted = true }, token);
            if (!(await client.RequestAsync("GetInputMute", new { inputName = name }, token)).GetProperty("inputMuted").GetBoolean())
                throw new InvalidOperationException("Could not mute the isolated recorder's audio input.");
        }
        await client.RequestAsync("SetProfileParameter", new { parameterCategory = "SimpleOutput", parameterName = "FilePath", parameterValue = _output }, token);
        await client.RequestAsync("SetVideoSettings", new { fpsNumerator = 30, fpsDenominator = 1, baseWidth = 1280, baseHeight = 800, outputWidth = 1280, outputHeight = 800 }, token);
        var monitor = monitors.OrderBy(m => m.Bounds.X).First(m => m.WorkArea.Width >= 1400 && m.WorkArea.Height >= 1000);
        var bounds = new DRect(monitor.WorkArea.X + 60, monitor.WorkArea.Y + 60, 1280, 800);
        var resized = await windows.ResizeAsync(target, bounds, WindowArea.Client, token);
        if (!resized.Exact) throw new InvalidOperationException("Pattern did not reach the required physical bounds.");
        using var overlay = new OutlineOverlay();
        using var main = new MainViewModel(displays, windows, new FakePicker(), overlay, new FakeHotkeys(), new FakeClipboard(), new FakeDialogs(), new MemoryStore(), new FakeStartup());
        main.Initialize();
        var hud = new HudWindow(main, overlay);
        try
        {
            string scene = "ExactFrame recording check " + DateTime.UtcNow.ToString("HHmmss");
            await client.RequestAsync("CreateScene", new { sceneName = scene }, token);
            await client.RequestAsync("SetCurrentProgramScene", new { sceneName = scene }, token);
            foreach (var method in new[] { (Id: 1, Name: "DXGI"), (Id: 2, Name: "WGC") })
            {
                string input = scene + " " + method.Name;
                var created = await client.RequestAsync("CreateInput", new { sceneName = scene, inputName = input, inputKind = "monitor_capture",
                    inputSettings = new { monitor_id = monitor.MonitorDevicePath, method = method.Id, capture_cursor = false }, sceneItemEnabled = true }, token);
                int item = created.GetProperty("sceneItemId").GetInt32();
                await Task.Delay(1200, token);
                await client.RequestAsync("SetSceneItemTransform", new { sceneName = scene, sceneItemId = item, sceneItemTransform = new {
                    cropLeft = bounds.Left - monitor.Bounds.Left, cropTop = bounds.Top - monitor.Bounds.Top,
                    cropRight = monitor.Bounds.Right - bounds.Right, cropBottom = monitor.Bounds.Bottom - bounds.Bottom,
                    scaleX = 1, scaleY = 1, positionX = 0, positionY = 0, alignment = 5 } }, token);
                foreach (string phase in new[] { "baseline", "visible", "excluded" })
                {
                    DesktopState.RequireInteractiveDesktop();
                    if (phase == "baseline") overlay.Hide();
                    else
                    {
                        main.HideFromRecorders = phase == "excluded";
                        overlay.ApplyStyle(new OutlineStyle { Color = OutlineColor.Red, Thickness = 6, ShowThirds = true, ShowCenterMark = true, ShowSafeArea = true, DimOutside = true });
                        var frame = new DRect(bounds.X + 140, bounds.Y + 100, 960, 540);
                        overlay.Show(frame, monitor, false, [new(new(frame.X + 200, frame.Y + 120, 240, 240), "Square", OutlineColor.Amber)]);
                    }
                    await Task.Delay(1000, token);
                    bool recording = false;
                    JsonElement stopped;
                    try
                    {
                        File.WriteAllText(Path.Combine(_output, "recording-owned.json"), "{\"mayBeActive\":true}");
                        await client.RequestAsync("StartRecord", cancellation: token); recording = true;
                        await Task.Delay(2200, token);
                        stopped = await client.RequestAsync("StopRecord", cancellation: token); recording = false;
                    }
                    finally
                    {
                        if (recording) await client.RequestAsync("StopRecord");
                    }
                    string source = stopped.GetProperty("outputPath").GetString()!;
                    string clip = Path.Combine(_output, method.Name + "-" + phase + Path.GetExtension(source));
                    // StopRecord can reply before the muxer closes its file. Wait for finalization, not a new recording.
                    await WaitForFileClosed(source, token);
                    File.WriteAllText(Path.Combine(_output, "recording-owned.json"), "{\"mayBeActive\":false}");
                    File.Move(source, clip);
                    _clips.Add(new { method = method.Name, phase, clip = Path.GetFileName(clip), monitor = monitor.Number, monitor.ScalePercent,
                        desktopBounds = bounds, overlayAccepted = overlay.CaptureExclusionApplied, controlBarStatus = main.CaptureExclusionStatusText,
                        hudBounds = new { X = hud.AppWindow.Position.X, Y = hud.AppWindow.Position.Y, Width = hud.AppWindow.Size.Width, Height = hud.AppWindow.Size.Height }, outputWidth = 1280, outputHeight = 800 });
                }
                overlay.Hide();
                await client.RequestAsync("RemoveSceneItem", new { sceneName = scene, sceneItemId = item }, token);
            }
        }
        finally { overlay.Hide(); hud.Close(); }
    }

    private async Task StopIsolatedRecording(int port, string configPath)
    {
        string marker = Path.Combine(_output, "recording-owned.json");
        if (!File.Exists(marker)) return;
        using var ownership = JsonDocument.Parse(File.ReadAllText(marker));
        if (!ownership.RootElement.GetProperty("mayBeActive").GetBoolean()) return;
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        using var client = new ObsWebSocketClient();
        await client.ConnectAsync(port, config.RootElement.GetProperty("server_password").GetString()!);
        var profile = await client.RequestAsync("GetProfileList");
        if (profile.GetProperty("currentProfileName").GetString() != "ExactFrame QA")
            throw new InvalidOperationException("Refusing cleanup outside the ExactFrame QA profile.");
        bool active = (await client.RequestAsync("GetRecordStatus")).GetProperty("outputActive").GetBoolean();
        if (active) await client.RequestAsync("StopRecord");
        File.WriteAllText(marker, "{\"mayBeActive\":false}");
        File.WriteAllText(Path.Combine(_output, "recorder-cleanup.json"), JsonSerializer.Serialize(new { stopRequested = active, utc = DateTimeOffset.UtcNow }));
    }

    internal static DRect ClientBounds(nint hwnd)
    {
        NativeMethods.Check(NativeMethods.GetClientRect(hwnd, out var client), "read the fixture client");
        var origin = new NativeMethods.Point(0, 0);
        NativeMethods.Check(NativeMethods.ClientToScreen(hwnd, ref origin), "read the fixture origin");
        return new(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
    }
    private static async Task WaitForFileClosed(string path, CancellationToken token)
    {
        for (int attempt = 0; attempt < 80; attempt++)
        {
            try { using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None); return; }
            catch (IOException) when (attempt < 79) { await Task.Delay(100, token); }
        }
        throw new IOException("OBS did not finalize its recording within eight seconds.");
    }
    private static DRect VisibleBounds(nint hwnd)
    {
        int result = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaExtendedFrameBounds, out var bounds, Marshal.SizeOf<NativeMethods.Rect>());
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        return bounds.ToRectangle();
    }
    private static Window PatternWindow()
    {
        var canvas = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 52, 86)) };
        foreach (var (horizontal, vertical, color) in new[] {
            (HorizontalAlignment.Left, VerticalAlignment.Top, Colors.Red), (HorizontalAlignment.Right, VerticalAlignment.Top, Colors.Lime),
            (HorizontalAlignment.Left, VerticalAlignment.Bottom, Colors.Yellow), (HorizontalAlignment.Right, VerticalAlignment.Bottom, Colors.Cyan) })
            canvas.Children.Add(new Border { Width = 32, Height = 32, HorizontalAlignment = horizontal, VerticalAlignment = vertical, Background = new SolidColorBrush(color) });
        var window = new Window { Title = "ExactFrame recording fixture", Content = canvas };
        if (window.AppWindow.Presenter is OverlappedPresenter presenter) { presenter.SetBorderAndTitleBar(false, false); presenter.IsAlwaysOnTop = true; }
        window.AppWindow.MoveAndResize(new RectInt32(80, 80, 1280, 800));
        return window;
    }
    private void WriteReport(IReadOnlyList<DisplayInfo> monitors, string? error) =>
        File.WriteAllText(Path.Combine(_output, "windows-checks.json"), JsonSerializer.Serialize(new {
            utc = DateTimeOffset.UtcNow, windows = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), monitors = monitors.Select(m => new { m.Number, m.Bounds, m.Dpi, m.ScalePercent }),
            mode = _responsivenessOnly ? "responsiveness" : "correctness",
            checks = _checks, clips = _clips, error, nativeChecksPassed = _responsivenessOnly ? (bool?)null : error is null && _nativePassed,
            recordedResult = "Analyze the saved clips separately; API acceptance is not a recording pass."
        }, new JsonSerializerOptions { WriteIndented = true }));
}
