using System.Drawing;

namespace ExactFrame.Core.Geometry;

[Flags]
public enum FrameHandle
{
    None = 0,
    Move = 1,
    Left = 2,
    Top = 4,
    Right = 8,
    Bottom = 16,
    TopLeft = Left | Top,
    TopRight = Right | Top,
    BottomLeft = Left | Bottom,
    BottomRight = Right | Bottom
}

/// <summary>Direct frame editing in physical screen pixels. Each update uses the gesture's original rectangle.</summary>
public static class FrameManipulation
{
    private const int MaximumSize = 16384;

    /// <summary>
    /// Corners and edge midpoints resize; the rest of the perimeter moves. Interior pixels move only when requested.
    /// The radius also extends the perimeter outside the rectangle to provide a usable grab area.
    /// It is capped at a quarter of the shortest side so a small preview retains an interior move zone.
    /// </summary>
    public static FrameHandle HitTest(Rectangle frame, Point point, int radius, bool includeInterior)
    {
        if (frame.Width <= 0 || frame.Height <= 0) return FrameHandle.None;
        radius = Math.Clamp(radius, 0, Math.Min(frame.Width, frame.Height) / 4);
        if (point.X < (long)frame.Left - radius || point.X > (long)frame.Right + radius ||
            point.Y < (long)frame.Top - radius || point.Y > (long)frame.Bottom + radius)
            return FrameHandle.None;

        (int X, int Y, FrameHandle Handle)[] handles =
        [
            (frame.Left, frame.Top, FrameHandle.TopLeft), (frame.Right, frame.Top, FrameHandle.TopRight),
            (frame.Left, frame.Bottom, FrameHandle.BottomLeft), (frame.Right, frame.Bottom, FrameHandle.BottomRight),
            (frame.Left + frame.Width / 2, frame.Top, FrameHandle.Top),
            (frame.Left + frame.Width / 2, frame.Bottom, FrameHandle.Bottom),
            (frame.Left, frame.Top + frame.Height / 2, FrameHandle.Left),
            (frame.Right, frame.Top + frame.Height / 2, FrameHandle.Right)
        ];
        FrameHandle nearest = FrameHandle.None;
        long distance = long.MaxValue;
        foreach (var handle in handles)
        {
            long dx = (long)point.X - handle.X, dy = (long)point.Y - handle.Y;
            if (Math.Abs(dx) > radius || Math.Abs(dy) > radius) continue;
            long candidate = dx * dx + dy * dy;
            if (candidate >= distance) continue;
            nearest = handle.Handle;
            distance = candidate;
        }
        if (nearest != FrameHandle.None) return nearest;

        if (Math.Abs((long)point.X - frame.Left) <= radius || Math.Abs((long)point.X - frame.Right) <= radius ||
            Math.Abs((long)point.Y - frame.Top) <= radius || Math.Abs((long)point.Y - frame.Bottom) <= radius)
            return FrameHandle.Move;
        return includeInterior && frame.Contains(point) ? FrameHandle.Move : FrameHandle.None;
    }

    /// <summary>
    /// Moves or resizes inside the usable area. Opposite resize edges stay fixed. An aspect-locked single edge
    /// keeps the perpendicular center fixed (within half a pixel). Unrepresentable aspect ratios or invalid
    /// resize handles leave the original rectangle unchanged. Snapping never overrides containment or size limits.
    /// </summary>
    public static Rectangle Drag(Rectangle original, Point start, Point current, FrameHandle handle,
        Rectangle area, int minSize = 100, double? aspectRatio = null,
        IReadOnlyList<Rectangle>? snapTargets = null, int snapDistance = 0)
    {
        if (original.Width <= 0 || original.Height <= 0 || area.Width <= 0 || area.Height <= 0)
            return original;
        minSize = Math.Clamp(minSize, 1, MaximumSize);
        double dx = (long)current.X - start.X, dy = (long)current.Y - start.Y;
        if (handle == FrameHandle.Move)
        {
            if (original.Width > area.Width || original.Height > area.Height) return original;
            var moved = new Rectangle(
                (int)Math.Clamp(original.X + dx, area.Left, area.Right - original.Width),
                (int)Math.Clamp(original.Y + dy, area.Top, area.Bottom - original.Height),
                original.Width, original.Height);
            if (snapDistance <= 0) return moved;
            int x = SnapMoveAxis(moved.X, moved.Width, area.Left, area.Right, Lines(area, snapTargets, true), snapDistance);
            int y = SnapMoveAxis(moved.Y, moved.Height, area.Top, area.Bottom, Lines(area, snapTargets, false), snapDistance);
            return new Rectangle(x, y, moved.Width, moved.Height);
        }

        bool left = handle.HasFlag(FrameHandle.Left), right = handle.HasFlag(FrameHandle.Right);
        bool top = handle.HasFlag(FrameHandle.Top), bottom = handle.HasFlag(FrameHandle.Bottom);
        const FrameHandle allEdges = FrameHandle.Left | FrameHandle.Top | FrameHandle.Right | FrameHandle.Bottom;
        if ((handle & ~allEdges) != 0 || (left && right) || (top && bottom) || !(left || right || top || bottom))
            return original;

        double width = original.Width + (left ? -dx : right ? dx : 0);
        double height = original.Height + (top ? -dy : bottom ? dy : 0);
        bool locked = aspectRatio is > 0 && double.IsFinite(aspectRatio.Value);
        double? ratio = locked ? aspectRatio : null;
        if (ratio is { } r)
        {
            if (!(left || right) || ((top || bottom) && Math.Abs(height - original.Height) * r > Math.Abs(width - original.Width)))
                width = height * r;
        }

        var frame = Resize(original, width, height, handle, area, minSize, ratio);
        if (snapDistance <= 0) return frame;

        if (ratio is null)
        {
            if (left || right)
            {
                double distance = double.MaxValue;
                foreach (int line in Lines(area, snapTargets, true))
                {
                    double gap = Math.Abs((left ? frame.Left : frame.Right) - (double)line);
                    if (gap > snapDistance || gap >= distance) continue;
                    var candidate = Resize(original, left ? original.Right - (double)line : line - (double)original.Left,
                        frame.Height, handle, area, minSize, null);
                    if ((left ? candidate.Left : candidate.Right) != line) continue;
                    distance = gap;
                    width = candidate.Width;
                }
                frame = Resize(original, width, frame.Height, handle, area, minSize, null);
            }
            if (top || bottom)
            {
                double distance = double.MaxValue;
                foreach (int line in Lines(area, snapTargets, false))
                {
                    double gap = Math.Abs((top ? frame.Top : frame.Bottom) - (double)line);
                    if (gap > snapDistance || gap >= distance) continue;
                    var candidate = Resize(original, frame.Width,
                        top ? original.Bottom - (double)line : line - (double)original.Top,
                        handle, area, minSize, null);
                    if ((top ? candidate.Top : candidate.Bottom) != line) continue;
                    distance = gap;
                    height = candidate.Height;
                }
                frame = Resize(original, frame.Width, height, handle, area, minSize, null);
            }
            return frame;
        }

        // One constraint drives both dimensions while the aspect ratio is locked.
        Rectangle best = frame;
        double bestDistance = double.MaxValue;
        foreach (bool horizontal in new[] { true, false })
        {
            if (horizontal ? !(left || right) : !(top || bottom)) continue;
            foreach (int line in Lines(area, snapTargets, horizontal))
            {
                int edge = horizontal ? (left ? frame.Left : frame.Right) : (top ? frame.Top : frame.Bottom);
                double gap = Math.Abs(edge - (double)line);
                if (gap > snapDistance || gap >= bestDistance) continue;
                double candidateWidth = horizontal
                    ? left ? original.Right - (double)line : line - (double)original.Left
                    : (top ? original.Bottom - (double)line : line - (double)original.Top) * ratio.Value;
                var candidate = Resize(original, candidateWidth, height, handle, area, minSize, ratio);
                int candidateEdge = horizontal ? (left ? candidate.Left : candidate.Right) : (top ? candidate.Top : candidate.Bottom);
                if (candidateEdge != line) continue;
                best = candidate;
                bestDistance = gap;
            }
        }
        return best;
    }

    /// <summary>Returns the unclipped-size drawing rectangle, with both pointer positions clamped to the area.</summary>
    public static Rectangle Draw(Point start, Point current, Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0) return Rectangle.Empty;
        int x1 = Math.Clamp(start.X, area.Left, area.Right), y1 = Math.Clamp(start.Y, area.Top, area.Bottom);
        int x2 = Math.Clamp(current.X, area.Left, area.Right), y2 = Math.Clamp(current.Y, area.Top, area.Bottom);
        return Rectangle.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    private static Rectangle Resize(Rectangle original, double width, double height, FrameHandle handle,
        Rectangle area, int minimum, double? ratio)
    {
        bool left = handle.HasFlag(FrameHandle.Left), right = handle.HasFlag(FrameHandle.Right);
        bool top = handle.HasFlag(FrameHandle.Top), bottom = handle.HasFlag(FrameHandle.Bottom);
        double centerX = original.Left + original.Width / 2d, centerY = original.Top + original.Height / 2d;
        double maxWidth = Math.Min(MaximumSize, left ? original.Right - (double)area.Left : right
            ? area.Right - (double)original.Left : 2 * Math.Min(centerX - area.Left, area.Right - centerX));
        double maxHeight = Math.Min(MaximumSize, top ? original.Bottom - (double)area.Top : bottom
            ? area.Bottom - (double)original.Top : 2 * Math.Min(centerY - area.Top, area.Bottom - centerY));
        if (maxWidth < minimum || maxHeight < minimum) return original;

        int w, h;
        if (ratio is { } r)
        {
            double lower = Math.Ceiling(Math.Max(minimum, minimum * r));
            double upper = Math.Floor(Math.Min(maxWidth, maxHeight * r));
            if (lower > upper) return original;
            w = (int)Math.Clamp(Math.Round(width), lower, upper);
            h = (int)Math.Round(w / r);
        }
        else
        {
            w = left || right ? (int)Math.Clamp(Math.Round(width), minimum, maxWidth) : original.Width;
            h = top || bottom ? (int)Math.Clamp(Math.Round(height), minimum, maxHeight) : original.Height;
        }
        int x = left ? original.Right - w : right ? original.Left : (int)Math.Floor(centerX - w / 2d);
        int y = top ? original.Bottom - h : bottom ? original.Top : (int)Math.Floor(centerY - h / 2d);
        var result = new Rectangle(x, y, w, h);
        return area.Contains(result) ? result : original;
    }

    private static int SnapMoveAxis(int position, int length, int minimum, int maximum, IEnumerable<int> lines, int tolerance)
    {
        int best = position;
        double bestDistance = double.MaxValue;
        foreach (int line in lines)
        {
            foreach (double offset in new[] { 0d, length / 2d, (double)length })
            {
                double gap = Math.Abs(line - position - offset);
                if (gap > tolerance || gap >= bestDistance) continue;
                int candidate = (int)Math.Floor(line - offset);
                if (candidate < minimum || (long)candidate + length > maximum) continue;
                best = candidate;
                bestDistance = gap;
            }
        }
        return best;
    }

    private static IEnumerable<int> Lines(Rectangle area, IReadOnlyList<Rectangle>? targets, bool horizontal)
    {
        foreach (int line in RectangleLines(area, horizontal)) yield return line;
        foreach (var target in targets ?? [])
            if (target.Width > 0 && target.Height > 0)
                foreach (int line in RectangleLines(target, horizontal)) yield return line;
    }

    private static IEnumerable<int> RectangleLines(Rectangle rectangle, bool horizontal)
    {
        int start = horizontal ? rectangle.Left : rectangle.Top, length = horizontal ? rectangle.Width : rectangle.Height;
        yield return start;
        yield return start + length / 2;
        yield return start + length;
    }
}
