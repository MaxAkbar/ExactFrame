using System.Drawing;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.Geometry;

public static class NestedFrameGeometry
{
    private const int Inset = 8;

    /// <summary>Fits an exact integer aspect ratio inside the parent, with space between the outlines.</summary>
    public static Rectangle Place(Rectangle parent, NestedFrame frame)
    {
        int ratioWidth = Math.Clamp(frame.AspectWidth, 1, 32);
        int ratioHeight = Math.Clamp(frame.AspectHeight, 1, 32);
        int divisor = Gcd(ratioWidth, ratioHeight);
        ratioWidth /= divisor;
        ratioHeight /= divisor;

        var area = Rectangle.Inflate(parent, -Inset, -Inset);
        if (area.Width <= 0 || area.Height <= 0) return Rectangle.Empty;

        int units = Math.Min(area.Width / ratioWidth, area.Height / ratioHeight);
        if (units < 1) return Rectangle.Empty;
        int scaledUnits = Math.Max(1, (int)Math.Floor(units * Math.Clamp(frame.ScalePercent, 10, 100) / 100d));
        var size = new Size(ratioWidth * scaledUnits, ratioHeight * scaledUnits);
        var anchor = Enum.IsDefined(frame.Anchor) ? frame.Anchor : FrameAnchor.Center;
        return FrameGeometry.Place(size, area, anchor);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
