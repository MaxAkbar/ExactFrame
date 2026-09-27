using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Microsoft.UI.Dispatching;
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
internal sealed class WindowService : IWindowService, IDisposable
{
    private const uint PositionFlags = NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate |
                                       NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpAsyncWindowPos;

    private sealed record SavedWindow(WindowInfo Window, NativeMethods.WindowPlacement Placement);

    private readonly Dictionary<nint, SavedWindow> _saved = [];
    private readonly Dictionary<nint, AutomationElement> _accessiblePages = [];
    private readonly Dictionary<nint, AutomationElement> _accessibleDocuments = [];
    private readonly Dictionary<nint, DateTime> _nextDocumentSearch = [];
    private readonly TitleBarDetector _titleBars;
    private readonly DispatcherQueueTimer _trackingTimer;
    private WindowInfo? _trackedWindow;
    private WindowArea _trackedArea;
    private WindowObservation? _lastObservation;

    public WindowService(TitleBarDetector titleBars)
    {
        _titleBars = titleBars;
        var dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Window tracking requires the UI thread.");
        _trackingTimer = dispatcher.CreateTimer();
        _trackingTimer.Interval = TimeSpan.FromMilliseconds(50);
        _trackingTimer.IsRepeating = true;
        _trackingTimer.Tick += OnTrackingTick;
    }

    public event EventHandler<WindowObservation>? TrackedWindowChanged;

    public void StartTracking(WindowInfo window, WindowArea area)
    {
        StopTracking();
        Validate(window);
        _trackedWindow = window;
        _trackedArea = area;
        _trackingTimer.Start();
        PollTrackedWindow();
    }

    public void StopTracking()
    {
        _trackingTimer.Stop();
        _trackedWindow = null;
        _lastObservation = null;
    }

    public void Dispose()
    {
        StopTracking();
        _accessiblePages.Clear();
        _accessibleDocuments.Clear();
        _nextDocumentSearch.Clear();
        _trackingTimer.Tick -= OnTrackingTick;
    }

    private void OnTrackingTick(DispatcherQueueTimer sender, object args) => PollTrackedWindow();

    private void PollTrackedWindow()
    {
        if (_trackedWindow is not { } window) return;
        var observation = Observe(window, _trackedArea);
        if (observation == _lastObservation) return;
        _lastObservation = observation;
        if (observation.Kind == WindowObservationKind.Closed) StopTracking();
        TrackedWindowChanged?.Invoke(this, observation);
    }

    private WindowObservation Observe(WindowInfo window, WindowArea area)
    {
        if (!NativeMethods.IsWindow(window.Handle))
        {
            _accessiblePages.Remove(window.Handle);
            _accessibleDocuments.Remove(window.Handle);
            _nextDocumentSearch.Remove(window.Handle);
            _titleBars.Forget(window.Handle);
            return new(WindowObservationKind.Closed, Rectangle.Empty);
        }
        uint thread = NativeMethods.GetWindowThreadProcessId(window.Handle, out uint process);
        if (process != window.ProcessId || thread != window.ThreadId)
        {
            _accessiblePages.Remove(window.Handle);
            _accessibleDocuments.Remove(window.Handle);
            _nextDocumentSearch.Remove(window.Handle);
            _titleBars.Forget(window.Handle);
            return new(WindowObservationKind.Closed, Rectangle.Empty);
        }

        if (!NativeMethods.IsWindowVisible(window.Handle) || NativeMethods.IsIconic(window.Handle) ||
            NativeMethods.IsHungAppWindow(window.Handle))
            return new(WindowObservationKind.Unavailable, Rectangle.Empty);
        if (NativeMethods.DwmGetWindowAttributeInt(window.Handle, NativeMethods.DwmwaCloaked,
                out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return new(WindowObservationKind.Unavailable, Rectangle.Empty);

        try
        {
            var bounds = CaptureBounds(window.Handle, area);
            return bounds.Width > 0 && bounds.Height > 0
                ? new(WindowObservationKind.Visible, bounds)
                : new(WindowObservationKind.Unavailable, Rectangle.Empty);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new(WindowObservationKind.Unavailable, Rectangle.Empty);
        }
    }

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
        // Page mode is based on a Chromium implementation detail. Reject unsupported windows before
        // saving, restoring, activating or moving them. A minimized page has no reliable viewport bounds.
        if (area == WindowArea.PageContent)
        {
            if (NativeMethods.IsIconic(window.Handle))
                throw new InvalidOperationException("Restore the browser window before using Web page mode.");
            _ = CaptureBounds(window.Handle, area);
        }
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

    private static Rectangle VisibleBounds(nint handle) =>
        VisibleFrameBounds(handle) ?? (NativeMethods.GetWindowRect(handle, out var rect) ? rect.ToRectangle() : Rectangle.Empty);

    private Rectangle CaptureBounds(nint handle, WindowArea area) => area switch
    {
        WindowArea.PageContent => PageContentBounds(handle),
        WindowArea.VisibleFrame => VisibleFrameBounds(handle)
            ?? throw new InvalidOperationException("This window’s visible frame couldn’t be measured. Try Client area."),
        _ => ClientBounds(handle)
    };

    private static Rectangle? VisibleFrameBounds(nint handle)
    {
        int result = NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaExtendedFrameBounds, out var visible,
            Marshal.SizeOf<NativeMethods.Rect>());
        return result == 0 && visible.Right > visible.Left && visible.Bottom > visible.Top ? visible.ToRectangle() : null;
    }

    /// <summary>
    /// The app's content without its title bar. Chrome, Edge, VS Code and similar apps draw their title bar or
    /// tab strip inside the native client rectangle, so that part is measured and left out too.
    /// </summary>
    private Rectangle ClientBounds(nint handle)
    {
        var client = NativeClientBounds(handle);
        int titleBar = _titleBars.HeightInside(handle, client, VisibleFrameBounds(handle) ?? client);
        return titleBar > 0 ? Rectangle.FromLTRB(client.Left, client.Top + titleBar, client.Right, client.Bottom) : client;
    }

    /// <summary>The native client rectangle (GetClientRect) in screen pixels.</summary>
    private static Rectangle NativeClientBounds(nint handle)
    {
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

    private Rectangle PageContentBounds(nint handle)
    {
        // Edge still exposes Chromium's legacy content HWND; current Chrome builds may not.
        // The render host's bounds can include a one-pixel edge. Prefer the accessible document,
        // which describes the actual rendered viewport, when Chromium exposes it.
        var client = NativeClientBounds(handle);
        if (AccessibleDocumentBounds(handle, client) is { } document) return document;
        var candidates = new List<Rectangle>();
        NativeMethods.EnumChildWindows(handle, (child, _) =>
        {
            if (!NativeMethods.IsWindowVisible(child)) return true;
            var name = new StringBuilder(128);
            if (NativeMethods.GetClassName(child, name, name.Capacity) == 0 ||
                !string.Equals(name.ToString(), "Chrome_RenderWidgetHostHWND", StringComparison.Ordinal)) return true;
            Rectangle bounds;
            try { bounds = NativeClientBounds(child); }
            catch (Win32Exception) { return true; } // Chromium can remove a child during enumeration.
            if (!IsPlausiblePage(bounds, client)) return true;
            candidates.Add(bounds);
            return true;
        }, 0);

        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count == 0) candidates.AddRange(AccessiblePageBounds(handle, client));
        if (candidates.Count == 1) return candidates[0];
        throw new InvalidOperationException(candidates.Count == 0
            ? "Web page content could not be measured for this app. Try Client area, or use a visible Chrome or Edge page."
            : "Several web page areas are visible in this app, so ExactFrame cannot choose one. Try Client area.");
    }

    private Rectangle? AccessibleDocumentBounds(nint handle, Rectangle client)
    {
        if (_accessibleDocuments.TryGetValue(handle, out var cached))
        {
            if (TryAccessibleDocumentBounds(cached, client, out var bounds)) return bounds;
            _accessibleDocuments.Remove(handle);
        }

        // A browser may not expose its document until accessibility is enabled. Avoid scanning
        // its full tree on every 50 ms tracking tick when that is the case.
        if (_nextDocumentSearch.TryGetValue(handle, out var next) && DateTime.UtcNow < next) return null;
        _nextDocumentSearch[handle] = DateTime.UtcNow.AddSeconds(2);
        try
        {
            var root = AutomationElement.FromHandle(handle);
            if (root is null) return null;
            var documents = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "RootWebArea"));
            var pages = new List<(AutomationElement Element, Rectangle Bounds)>();
            foreach (AutomationElement document in documents)
                if (TryAccessibleDocumentBounds(document, client, out var bounds))
                    pages.Add((document, bounds));

            if (pages.Count != 1) return null;
            _accessibleDocuments[handle] = pages[0].Element;
            return pages[0].Bounds;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryAccessibleDocumentBounds(AutomationElement element, Rectangle client, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            if (element.Current.IsOffscreen || element.Current.ControlType != ControlType.Document ||
                element.Current.AutomationId != "RootWebArea") return false;
            return TryAccessibleRectangle(element.Current.BoundingRectangle, client, out bounds);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return false;
        }
    }

    private IReadOnlyList<Rectangle> AccessiblePageBounds(nint handle, Rectangle client)
    {
        if (_accessiblePages.TryGetValue(handle, out var cached))
        {
            if (TryAccessibleBounds(cached, client, out var bounds)) return [bounds];
            _accessiblePages.Remove(handle);
        }

        try
        {
            var root = AutomationElement.FromHandle(handle);
            if (root is null) return [];
            var containers = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "MultiContentsView"));
            var pages = new List<(AutomationElement Element, Rectangle Bounds)>();
            foreach (AutomationElement container in containers)
            {
                if (container.Current.IsOffscreen) continue;
                var views = container.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ClassNameProperty, "Chrome_WidgetWin_1"));
                foreach (AutomationElement view in views)
                    if (TryAccessibleBounds(view, client, out var bounds)) pages.Add((view, bounds));
            }

            if (pages.Count == 1) _accessiblePages[handle] = pages[0].Element;
            return pages.Select(page => page.Bounds).ToList();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return [];
        }
    }

    private static bool TryAccessibleBounds(AutomationElement element, Rectangle client, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            if (element.Current.IsOffscreen || element.Current.ClassName != "Chrome_WidgetWin_1") return false;
            return TryAccessibleRectangle(element.Current.BoundingRectangle, client, out bounds);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryAccessibleRectangle(System.Windows.Rect rect, Rectangle client, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (rect.IsEmpty || !double.IsFinite(rect.Left) || !double.IsFinite(rect.Top) ||
            !double.IsFinite(rect.Right) || !double.IsFinite(rect.Bottom)) return false;
        if (rect.Left < int.MinValue || rect.Top < int.MinValue ||
            rect.Right > int.MaxValue || rect.Bottom > int.MaxValue) return false;
        bounds = Rectangle.FromLTRB((int)Math.Round(rect.Left), (int)Math.Round(rect.Top),
            (int)Math.Round(rect.Right), (int)Math.Round(rect.Bottom));
        return IsPlausiblePage(bounds, client);
    }

    private static bool IsPlausiblePage(Rectangle bounds, Rectangle client)
    {
        if (bounds.Width < 100 || bounds.Height < 100) return false;
        var overlap = Rectangle.Intersect(bounds, client);
        return (long)overlap.Width * overlap.Height >= (long)bounds.Width * bounds.Height * 95 / 100;
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
