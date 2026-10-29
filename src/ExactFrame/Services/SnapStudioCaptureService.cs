using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using ExactFrame.Core.Services;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace ExactFrame.Services;

internal sealed class SnapStudioCaptureService(IDisplayService displays) : ISnapStudioCaptureService
{
    private nint _owner;
    public void Attach(Window window) => _owner = WinRT.Interop.WindowNative.GetWindowHandle(window);
    public async Task<string?> ChooseApplicationAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, CommitButtonText = "Use SnapStudio" };
        picker.FileTypeFilter.Add(".exe"); WinRT.Interop.InitializeWithWindow.Initialize(picker, _owner);
        var selected = await picker.PickSingleFileAsync();
        cancellation.ThrowIfCancellationRequested();
        if (selected is null) return null;
        SnapStudioCaptureConnection.ValidateApplication(selected.Path); return selected.Path;
    }
    public async Task CaptureAsync(string executable, Rectangle region, string name, CancellationToken cancellation)
    {
        SnapStudioCaptureConnection.ValidateApplication(executable); SnapStudioCaptureConnection.ValidateRegion(region);
        await using var pipe = new NamedPipeClientStream(".", SnapStudioCaptureConnection.DefaultPipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(500, cancellation); }
        catch (TimeoutException)
        {
            bool running = false;
            foreach (var process in Process.GetProcessesByName("SnapStudio.App"))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != Process.GetCurrentProcess().SessionId) continue;
                        string? path = process.MainModule?.FileName;
                        if (path is null || !File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "SnapStudio.integration.json")))
                            throw new InvalidOperationException("Close the older running SnapStudio copy, then capture again with the integration-enabled build.");
                        running = true;
                    }
                    catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("SnapStudio is running with different permissions. Run both apps with matching permissions."); }
                }
            }
            if (!running) Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) })?.Dispose();
            try { await pipe.ConnectAsync(20000, cancellation); }
            catch (TimeoutException error) { throw new InvalidOperationException("SnapStudio did not become ready. Open it, finish setup, and try again.", error); }
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        if (!displays.GetDisplays().Any(display => display.Bounds.Contains(region)))
            throw new InvalidOperationException("Display settings changed. Move the entire frame onto one connected display and try again.");
        if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverProcess)) AllowSetForegroundWindow(serverProcess);
        using var hidden = new HiddenWindows();
        await Task.Delay(400, deadline.Token);
        try { await SnapStudioCaptureConnection.RequestAsync(pipe, region, name, deadline.Token); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new InvalidOperationException("SnapStudio capture timed out. Check SnapStudio and try again."); }
        catch (System.Text.Json.JsonException error) { throw new InvalidOperationException("SnapStudio returned an invalid capture response.", error); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    private sealed class HiddenWindows : IDisposable
    {
        private readonly List<nint> _windows = [];
        public HiddenWindows()
        {
            uint ownProcess = (uint)Environment.ProcessId;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out uint process);
                if (process == ownProcess && IsWindowVisible(window) && !IsIconic(window)) _windows.Add(window);
                return true;
            }, 0);
            foreach (nint window in _windows) ShowWindow(window, 0);
        }
        public void Dispose()
        {
            // Showing with SW_SHOWNA raises a hidden window above SnapStudio. Preserve its current z-order instead.
            foreach (nint window in _windows)
                if (IsWindow(window)) SetWindowPos(window, 0, 0, 0, 0, 0, 0x0057); // SHOWWINDOW | NOACTIVATE | NOZORDER | NOMOVE | NOSIZE
        }
        private delegate bool EnumWindow(nint window, nint data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint data);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
        [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    }
}
