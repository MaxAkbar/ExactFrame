using System.Drawing;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.Geometry;

public static class NestedFrameGeometry
{
    /// <summary>Fits an exact integer aspect ratio inside the parent, with space between the outlines.</summary>
    public static Rectangle Place(Rectangle parent, NestedFrame frame)
    {
        int ratioWidth = Math.Clamp(frame.AspectWidth, 1, 32);
        int ratioHeight = Math.Clamp(frame.AspectHeight, 1, 32);
        int divisor = Gcd(ratioWidth, ratioHeight);
        ratioWidth /= divisor;
        ratioHeight /= divisor;

        var area = AvailableArea(parent, frame);
        if (area.Width <= 0 || area.Height <= 0) return Rectangle.Empty;

        int units = Math.Min(area.Width / ratioWidth, area.Height / ratioHeight);
        if (units < 1) return Rectangle.Empty;
        int scaledUnits = Math.Max(1, (int)Math.Floor(units * Math.Clamp(frame.ScalePercent, 10, 100) / 100d));
        var size = new Size(ratioWidth * scaledUnits, ratioHeight * scaledUnits);
        var anchor = Enum.IsDefined(frame.Anchor) ? frame.Anchor : FrameAnchor.Center;
        if (!frame.UseCustomPosition) return FrameGeometry.Place(size, area, anchor);
        return new Rectangle(area.X + Offset(area.Width - size.Width, frame.PositionXPercent),
            area.Y + Offset(area.Height - size.Height, frame.PositionYPercent), size.Width, size.Height);
    }

    public static Rectangle AvailableArea(Rectangle parent, NestedFrame frame) =>
        Rectangle.Inflate(parent, -Math.Clamp(frame.InsetPixels, 0, 1000), -Math.Clamp(frame.InsetPixels, 0, 1000));

    /// <summary>Moves a crop to an arbitrary parent-relative pixel position without changing its size.</summary>
    public static NestedFrame PositionAt(Rectangle parent, NestedFrame frame, double x, double y)
    {
        var crop = Place(parent, frame);
        var area = AvailableArea(parent, frame);
        if (crop.IsEmpty || !double.IsFinite(x) || !double.IsFinite(y)) return frame;
        static double Percent(double value, int travel) => travel <= 0 ? 50 : Math.Clamp(value / travel * 100, 0, 100);
        return frame with
        {
            UseCustomPosition = true,
            PositionXPercent = Percent(x - (area.X - parent.X), area.Width - crop.Width),
            PositionYPercent = Percent(y - (area.Y - parent.Y), area.Height - crop.Height)
        };
    }

    public static Rectangle SafeArea(Rectangle crop, int marginPercent)
    {
        if (crop.IsEmpty || marginPercent <= 0) return Rectangle.Empty;
        int margin = Math.Clamp(marginPercent, 0, 40);
        int dx = (int)Math.Ceiling(crop.Width * margin / 100d);
        int dy = (int)Math.Ceiling(crop.Height * margin / 100d);
        return crop.Width <= dx * 2 || crop.Height <= dy * 2 ? Rectangle.Empty : Rectangle.Inflate(crop, -dx, -dy);
    }

    private static int Offset(int travel, double percent) =>
        (int)Math.Round(travel * (double.IsFinite(percent) ? Math.Clamp(percent, 0, 100) : 50) / 100);

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
