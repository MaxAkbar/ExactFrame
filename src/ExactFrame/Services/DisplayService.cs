using System.Runtime.InteropServices;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Interop;
using ExactFrame.Native;

namespace ExactFrame.Services;

/// <summary>Enumerates monitors in physical pixels. Displays are numbered left to right, then top to bottom.</summary>
internal sealed class DisplayService : IDisplayService, IDisposable
{
    private readonly MessageWindow _messages;

    public DisplayService(MessageWindow messages)
    {
        _messages = messages;
        _messages.DisplaysChanged += OnDisplaysChanged;
    }

    public event EventHandler? DisplaysChanged;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var found = new List<(string Device, System.Drawing.Rectangle Bounds, System.Drawing.Rectangle Work, int Dpi, bool Primary)>();
        NativeMethods.EnumDisplayMonitors(0, 0, (nint monitor, nint hdc, ref NativeMethods.Rect rect, nint data) =>
        {
            var info = new NativeMethods.MonitorInfoEx { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>(), Device = string.Empty };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return true;
            int dpi = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out uint dpiX, out _) == 0
                ? (int)dpiX
                : 96;
            found.Add((info.Device, info.Monitor.ToRectangle(), info.Work.ToRectangle(), dpi,
                (info.Flags & NativeMethods.MonitorInfoPrimary) != 0));
            return true;
        }, 0);

        return found
            .OrderBy(d => d.Bounds.X)
            .ThenBy(d => d.Bounds.Y)
            .Select((d, index) => new DisplayInfo(d.Device, index + 1, d.Bounds, d.Work, d.Dpi, d.Primary))
            .ToList();
    }

    public void Dispose() => _messages.DisplaysChanged -= OnDisplaysChanged;

    private void OnDisplaysChanged(object? sender, EventArgs e) => DisplaysChanged?.Invoke(this, EventArgs.Empty);
}
