using System.Drawing;

namespace ExactFrame.Core.Models;

/// <summary>A monitor in physical pixels. <see cref="Number"/> is a stable, left-to-right label.</summary>
public sealed record DisplayInfo(string DeviceName, int Number, Rectangle Bounds, Rectangle WorkArea, int Dpi, bool IsPrimary)
{
    /// <summary>Windows monitor interface path, also used by OBS Display Capture's monitor_id.</summary>
    public string? MonitorDevicePath { get; init; }

    public double Scale => Dpi / 96d;

    public int ScalePercent => (int)Math.Round(Dpi * 100 / 96d);

    public string Title => $"Display {Number}";

    public Rectangle UsableArea(bool keepClearOfTaskbar) => keepClearOfTaskbar ? WorkArea : Bounds;
}
