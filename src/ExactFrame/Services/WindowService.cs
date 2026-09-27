using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Interop;
using Rectangle = System.Drawing.Rectangle;

namespace ExactFrame.Services;

/// <summary>
/// Window discovery, measured resize, verification and restore. Ported from the WinForms version:
/// the resize loop measures real physical insets on the destination monitor and verifies the result.
/// </summary>
internal sealed class WindowService : IWindowService
{
    private const uint PositionFlags = NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate |
                                       NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpAsyncWindowPos;

    private sealed record SavedWindow(WindowInfo Window, NativeMethods.WindowPlacement Placement);

    private readonly Dictionary<nint, SavedWindow> _saved = [];

    public IReadOnlyList<WindowInfo> GetWindows()
    {
        var windows = new List<WindowInfo>();
        var processNames = new Dictionary<uint, string>();
        NativeMethods.Check(NativeMethods.EnumWindows((hwnd, _) =>
        {
            var window = Describe(hwnd, processNames);
            if (window is not null) windows.Add(window);
            return true;
        }, 0), "list open windows");
        return windows.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Describes a top-level window of another process, or returns <c>null</c> when it isn't a candidate.</summary>
    internal static WindowInfo? Describe(nint hwnd, Dictionary<uint, string>? processNames = null)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return null;
        uint thread = NativeMethods.GetWindowThreadProcessId(hwnd, out uint process);
        if (process == 0 || process == (uint)Environment.ProcessId) return null;
        if (NativeMethods.DwmGetWindowAttributeInt(hwnd, NativeMethods.DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return null;

        var title = new StringBuilder(2048);
        NativeMethods.GetWindowText(hwnd, title, title.Capacity);
        if (string.IsNullOrWhiteSpace(title.ToString())) return null;

        string processName;
        if (processNames is null || !processNames.TryGetValue(process, out processName!))
        {
            processName = ProcessName(process);
            processNames?.TryAdd(process, processName);
        }

        bool minimized = NativeMethods.IsIconic(hwnd);
        var bounds = minimized ? NormalBounds(hwnd) : VisibleBounds(hwnd);
        return new WindowInfo(hwnd, process, thread, title.ToString(), processName, bounds, minimized);
    }

    public Rectangle Measure(WindowInfo window, WindowArea area)
    {
        Validate(window);
        if (NativeMethods.IsIconic(window.Handle))
            throw new InvalidOperationException("That window is minimized, so it has no position on screen. Choose Center instead.");
        return CaptureBounds(window.Handle, area);
    }

    public bool CanRestore(WindowInfo? window) =>
        window is not null && _saved.TryGetValue(window.Handle, out var saved) && saved.Window.IsSameWindow(window);

    public async Task<ResizeResult> ResizeAsync(WindowInfo window, Rectangle desired, WindowArea area, CancellationToken cancellation)
    {
        Validate(window);
        if (!CanRestore(window))
        {
            var placement = NativeMethods.WindowPlacement.Create();
            NativeMethods.Check(NativeMethods.GetWindowPlacement(window.Handle, ref placement), "save the original window position");
            _saved[window.Handle] = new SavedWindow(window, placement);
        }

        bool restored = false;
        if (NativeMethods.IsIconic(window.Handle) || NativeMethods.IsZoomed(window.Handle))
        {
            await ShowNormalAsync(window, cancellation);
            restored = true;
        }

        // Bring the app in front of ExactFrame and everything else, so it's visible in the outline.
        BringToFront(window.Handle);

        // Move first, then let the destination monitor's DPI change take effect before measuring.
        NativeMethods.Check(NativeMethods.SetWindowPos(window.Handle, 0, desired.X, desired.Y, 0, 0,
            PositionFlags | NativeMethods.SwpNoSize), "move the selected window");
        await WaitForLayoutAsync(window, cancellation);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Validate(window);
            var outer = OuterBounds(window.Handle);
            var capture = CaptureBounds(window.Handle, area);
            if (capture == desired) return new ResizeResult(capture, true, restored);

            var target = FrameGeometry.OuterForCapture(outer, capture, desired);
            NativeMethods.Check(NativeMethods.SetWindowPos(window.Handle, 0, target.X, target.Y,
                target.Width, target.Height, PositionFlags), "resize the selected window");
            await WaitForLayoutAsync(window, cancellation);
        }

        var actual = CaptureBounds(window.Handle, area);
        return new ResizeResult(actual, actual == desired, restored);
    }

    /// <summary>
    /// Brings a minimized or maximized window back to a normal, sizable window. Uses the window's placement
    /// so it always lands in the normal state: SW_RESTORE would bring back a window that was maximized before
    /// it was minimized as maximized again, and a maximized window can't be resized.
    /// </summary>
    private static async Task ShowNormalAsync(WindowInfo window, CancellationToken cancellation)
    {
        var placement = NativeMethods.WindowPlacement.Create();
        NativeMethods.Check(NativeMethods.GetWindowPlacement(window.Handle, ref placement), "read the window’s state");
        placement.Flags = (placement.Flags & ~NativeMethods.PlacementRestoreToMaximized) | NativeMethods.PlacementAsync;
        placement.ShowCmd = NativeMethods.SwShowNormal;
        NativeMethods.Check(NativeMethods.SetWindowPlacement(window.Handle, in placement), "restore the window before resizing");
        if (await WaitForNormalAsync(window, 20, cancellation)) return;

        // A few apps ignore placement changes from another process; ask once more the ordinary way.
        NativeMethods.ShowWindowAsync(window.Handle, NativeMethods.SwShowNormal);
        if (await WaitForNormalAsync(window, 15, cancellation)) return;

        throw new InvalidOperationException(
            "The app stayed minimized or maximized. If it runs as administrator, run ExactFrame as administrator too, " +
            "or restore the app from the taskbar and try again.");
    }

    private static async Task<bool> WaitForNormalAsync(WindowInfo window, int attempts, CancellationToken cancellation)
    {
        for (int i = 0; i < attempts; i++)
        {
            await Task.Delay(100, cancellation);
            Validate(window);
            if (!NativeMethods.IsIconic(window.Handle) && !NativeMethods.IsZoomed(window.Handle)) return true;
        }
        return false;
    }

    /// <summary>
    /// Activates the window. ExactFrame is the foreground app when the user clicks Resize window, so Windows
    /// lets it hand the foreground over; if it refuses, the window is at least raised to the top.
    /// </summary>
    private static void BringToFront(nint handle)
    {
        if (NativeMethods.SetForegroundWindow(handle)) return;
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTop, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpAsyncWindowPos);
    }

    public async Task RestoreAsync(WindowInfo window, CancellationToken cancellation)
    {
        Validate(window);
        if (!CanRestore(window)) throw new InvalidOperationException("There’s no saved position for this window in this session.");

        var saved = _saved[window.Handle].Placement;
        var placement = saved;
        placement.Flags |= NativeMethods.PlacementAsync;
        NativeMethods.Check(NativeMethods.SetWindowPlacement(window.Handle, in placement), "restore the original window position");

        bool wasMinimized = saved.ShowCmd is 2 or 6 or 7 or 11;
        bool wasMaximized = saved.ShowCmd == 3;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(100, cancellation);
            Validate(window);
            var actual = NativeMethods.WindowPlacement.Create();
            NativeMethods.Check(NativeMethods.GetWindowPlacement(window.Handle, ref actual), "verify the restored window");
            if (actual.NormalPosition.ToRectangle() == saved.NormalPosition.ToRectangle() &&
                NativeMethods.IsIconic(window.Handle) == wasMinimized &&
                (wasMinimized || NativeMethods.IsZoomed(window.Handle) == wasMaximized))
            {
                _saved.Remove(window.Handle);
                return;
            }
        }
        throw new InvalidOperationException("The app didn’t return to the saved position. The saved position is kept; try Restore again.");
    }

    private static void Validate(WindowInfo window)
    {
        uint thread = NativeMethods.GetWindowThreadProcessId(window.Handle, out uint process);
        if (!NativeMethods.IsWindow(window.Handle) || process != window.ProcessId || thread != window.ThreadId)
            throw new InvalidOperationException("That window has closed or changed. Refresh the window list and select it again.");
        if (NativeMethods.IsHungAppWindow(window.Handle))
            throw new InvalidOperationException("The selected app isn’t responding. Try again when it responds.");
    }

    private static Rectangle OuterBounds(nint handle)
    {
        NativeMethods.Check(NativeMethods.GetWindowRect(handle, out var rect), "measure the window");
        return rect.ToRectangle();
    }

    /// <summary>The size a minimized window restores to. The position is in workspace coordinates, so only the size is reliable.</summary>
    private static Rectangle NormalBounds(nint handle)
    {
        var placement = NativeMethods.WindowPlacement.Create();
        return NativeMethods.GetWindowPlacement(handle, ref placement) ? placement.NormalPosition.ToRectangle() : Rectangle.Empty;
    }

    private static Rectangle VisibleBounds(nint handle)
    {
        int result = NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaExtendedFrameBounds, out var visible,
            Marshal.SizeOf<NativeMethods.Rect>());
        if (result == 0 && visible.Right > visible.Left && visible.Bottom > visible.Top) return visible.ToRectangle();
        return NativeMethods.GetWindowRect(handle, out var rect) ? rect.ToRectangle() : Rectangle.Empty;
    }

    private static Rectangle CaptureBounds(nint handle, WindowArea area)
    {
        if (area == WindowArea.VisibleFrame)
        {
            int result = NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaExtendedFrameBounds, out var visible,
                Marshal.SizeOf<NativeMethods.Rect>());
            if (result != 0 || visible.Right <= visible.Left || visible.Bottom <= visible.Top)
                throw new InvalidOperationException("This window’s visible frame couldn’t be measured. Try Client area.");
            return visible.ToRectangle();
        }

        NativeMethods.Check(NativeMethods.GetClientRect(handle, out var client), "measure the content area");
        // Convert both corners. A foreign window may use a different DPI-awareness mode; subtracting
        // converted corners keeps the result in screen pixels.
        var start = new NativeMethods.Point(client.Left, client.Top);
        var end = new NativeMethods.Point(client.Right, client.Bottom);
        NativeMethods.Check(NativeMethods.ClientToScreen(handle, ref start), "locate the content area");
        NativeMethods.Check(NativeMethods.ClientToScreen(handle, ref end), "locate the content area");
        return Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
            Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
    }

    private static async Task WaitForLayoutAsync(WindowInfo window, CancellationToken cancellation)
    {
        // ASYNCWINDOWPOS avoids hanging this UI on another process. Wait for the measured outer
        // rectangle to settle; the capture area is always verified afterwards.
        await Task.Delay(200, cancellation);
        Validate(window);
        var previous = OuterBounds(window.Handle);
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(80, cancellation);
            Validate(window);
            var current = OuterBounds(window.Handle);
            if (current == previous) return;
            previous = current;
        }
    }

    private static string ProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "app";
        }
    }
}
