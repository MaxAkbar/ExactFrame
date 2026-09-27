using System.Drawing;

namespace ExactFrame.Core.Geometry;

public enum FrameEdge
{
    Left,
    Top,
    Right,
    Bottom
}

/// <summary>Positions the edge magnifier beside the pointer and finds the nearest frame boundary.</summary>
public static class EdgeMagnifierGeometry
{
    public static bool TryNearestEdge(Rectangle frame, Point pointer, int tolerance, out FrameEdge edge)
    {
        edge = default;
        if (frame.Width <= 0 || frame.Height <= 0 || tolerance < 0) return false;

        int nearest = tolerance + 1;
        FrameEdge bestEdge = default;
        void Consider(FrameEdge candidate, int distance)
        {
            if (distance >= nearest) return;
            nearest = distance;
            bestEdge = candidate;
        }

        if (pointer.Y >= frame.Top - tolerance && pointer.Y < frame.Bottom + tolerance)
        {
            Consider(FrameEdge.Left, Math.Abs(pointer.X - frame.Left));
            Consider(FrameEdge.Right, Math.Abs(pointer.X - (frame.Right - 1)));
        }
        if (pointer.X >= frame.Left - tolerance && pointer.X < frame.Right + tolerance)
        {
            Consider(FrameEdge.Top, Math.Abs(pointer.Y - frame.Top));
            Consider(FrameEdge.Bottom, Math.Abs(pointer.Y - (frame.Bottom - 1)));
        }
        edge = bestEdge;
        return nearest <= tolerance;
    }

    public static Rectangle PlaceLens(Point pointer, Size lens, Rectangle display, int gap)
    {
        int x = pointer.X + gap;
        int y = pointer.Y + gap;
        if (x + lens.Width > display.Right) x = pointer.X - gap - lens.Width;
        if (y + lens.Height > display.Bottom) y = pointer.Y - gap - lens.Height;
        x = Math.Clamp(x, display.Left, Math.Max(display.Left, display.Right - lens.Width));
        y = Math.Clamp(y, display.Top, Math.Max(display.Top, display.Bottom - lens.Height));
        return new Rectangle(new Point(x, y), lens);
    }
}
