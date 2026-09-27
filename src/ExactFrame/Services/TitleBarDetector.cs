using System.Diagnostics;
using ExactFrame.Core.Geometry;
using ExactFrame.Interop;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace ExactFrame.Services;

/// <summary>
/// Measures title bars that apps draw inside their own client area, such as the tab strip in Chrome and Edge.
/// Windows asks a window which part is under the pointer (WM_NCHITTEST) so it can drag, resize and snap it;
/// asking the same question down the top of the window shows where the title bar ends. Results are cached per
/// window, size and DPI, because the outline measures the client area every 50 ms while it follows a window.
/// </summary>
internal sealed class TitleBarDetector
{
    // Inside the caption buttons (Close is about 46 DIPs wide) and clear of the corner resize handle.
    private const int ProbeInsetDips = 20;

    // Deeper than any title bar or tab strip.
    private const int MaxDepthDips = 128;

    private const uint ReplyTimeoutMs = 50;
    private const long CacheLifetimeMs = 2000;
    private const long RetryAfterMs = 250;
    private static readonly TimeSpan ScanBudget = TimeSpan.FromMilliseconds(250);

    private sealed record Measurement(Size ClientSize, uint Dpi, int Height, long ExpiresAt);

    private readonly Dictionary<nint, Measurement> _measurements = [];

    /// <summary>
    /// Height of the title bar the app draws at the top of <paramref name="client"/>, or 0 when it has none. If the
    /// app doesn't answer, the last height for this size is kept. Both rectangles are in physical screen pixels.
    /// </summary>
    public int HeightInside(nint handle, Rectangle client, Rectangle visibleFrame)
    {
        uint dpi = NativeMethods.GetDpiForWindow(handle);
        long now = Environment.TickCount64;
        if (_measurements.TryGetValue(handle, out var cached) && cached.ClientSize == client.Size &&
            cached.Dpi == dpi && now < cached.ExpiresAt)
            return cached.Height;

        if (Measure(handle, client, visibleFrame, dpi) is { } height)
        {
            _measurements[handle] = new Measurement(client.Size, dpi, height, now + CacheLifetimeMs);
            return height;
        }

        // The app didn't answer in time. Keep the last answer for this size so the outline doesn't jump, and ask again soon.
        int kept = cached is not null && cached.ClientSize == client.Size && cached.Dpi == dpi ? cached.Height : 0;
        _measurements[handle] = new Measurement(client.Size, dpi, kept, now + RetryAfterMs);
        return kept;
    }

    public void Forget(nint handle) => _measurements.Remove(handle);

    /// <returns>The title bar height, 0 for none, or <c>null</c> when the app didn't answer.</returns>
    private static int? Measure(nint handle, Rectangle client, Rectangle visibleFrame, uint dpi)
    {
        // The probe points are physical screen pixels; only a per-monitor aware app reads them that way.
        if (!NativeMethods.IsPerMonitorDpiAware(handle)) return 0;

        double scale = Math.Max(96u, dpi) / 96d;
        int inset = (int)Math.Round(ProbeInsetDips * scale);
        int left = Math.Max(client.Left, visibleFrame.Left);
        int right = Math.Min(client.Right, visibleFrame.Right);
        if (right - left <= inset * 2) return 0;

        // Caption buttons are on the right, or on the left in right-to-left layouts.
        int[] columns = [right - 1 - inset, left + inset];
        int maxDepth = Math.Min(client.Height / 2, (int)Math.Round(MaxDepthDips * scale));
        var clock = Stopwatch.StartNew();
        if (!TitleBarScan.TryFindContentTop(client, columns, maxDepth,
                (x, y) => clock.Elapsed > ScanBudget ? WindowPart.Unknown : PartAt(handle, x, y), out int? contentTop))
            return null;
        return contentTop is { } top ? top - client.Top : 0;
    }

    private static WindowPart PartAt(nint handle, int x, int y)
    {
        if (NativeMethods.SendMessageTimeout(handle, NativeMethods.WmNcHitTest, 0, NativeMethods.PointParam(x, y),
                NativeMethods.SmtoAbortIfHung | NativeMethods.SmtoErrorOnExit, ReplyTimeoutMs, out nint result) == 0)
            return WindowPart.Unknown;

        return (int)result switch
        {
            NativeMethods.HtClient => WindowPart.Content,
            NativeMethods.HtCaption or NativeMethods.HtSysMenu or NativeMethods.HtMinButton or NativeMethods.HtMaxButton or
                NativeMethods.HtClose or NativeMethods.HtHelp or NativeMethods.HtTop or NativeMethods.HtTopLeft or
                NativeMethods.HtTopRight => WindowPart.TitleBar,
            _ => WindowPart.Other
        };
    }
}
