using System.Drawing;

namespace ExactFrame.Core.Geometry;

/// <summary>Pixel-coordinate calculations. All values are physical screen pixels.</summary>
public static class FrameGeometry
{
    /// <summary>Centers <paramref name="size"/> in <paramref name="area"/>. Oversized frames are rejected, never shrunk.</summary>
    public static Rectangle Center(Size size, Rectangle area)
    {
        EnsureFits(size, area);
        return Place(size, area, FrameAnchor.Center);
    }

    /// <summary>
    /// Positions <paramref name="size"/> at an anchor inside <paramref name="area"/>. The result can extend past
    /// the area when the frame is larger than it; use <see cref="CheckFit"/> before showing or applying it.
    /// </summary>
    public static Rectangle Place(Size size, Rectangle area, FrameAnchor anchor)
    {
        int x = anchor.Column() switch
        {
            0 => area.Left,
            1 => area.Left + (area.Width - size.Width) / 2,
            _ => area.Right - size.Width
        };
        int y = anchor.Row() switch
        {
            0 => area.Top,
            1 => area.Top + (area.Height - size.Height) / 2,
            _ => area.Bottom - size.Height
        };
        return new Rectangle(x, y, size.Width, size.Height);
    }

    public static void EnsureFits(Size size, Rectangle area)
    {
        if (size.Width <= 0 || size.Height <= 0 || size.Width > area.Width || size.Height > area.Height)
            throw new InvalidOperationException(
                $"{size.Width} × {size.Height} does not fit in {area.Width} × {area.Height}. " +
                "Choose a smaller size or a larger display.");
    }

    /// <summary>Keeps a dragged frame inside <paramref name="area"/> without changing its size.</summary>
    public static Rectangle ClampPosition(Rectangle frame, Rectangle area)
    {
        EnsureFits(frame.Size, area);
        return new Rectangle(
            Math.Clamp(frame.X, area.Left, area.Right - frame.Width),
            Math.Clamp(frame.Y, area.Top, area.Bottom - frame.Height),
            frame.Width,
            frame.Height);
    }

    /// <summary>
    /// Computes the outer window rectangle that puts the measured capture area at <paramref name="desired"/>.
    /// Uses measured physical insets, so custom title bars and asymmetric invisible borders are handled.
    /// </summary>
    public static Rectangle OuterForCapture(Rectangle outer, Rectangle capture, Rectangle desired) => new(
        desired.X - (capture.X - outer.X),
        desired.Y - (capture.Y - outer.Y),
        desired.Width + outer.Width - capture.Width,
        desired.Height + outer.Height - capture.Height);

    /// <summary>Checks whether a frame fits on a display, optionally keeping clear of the taskbar.</summary>
    public static FitResult CheckFit(Rectangle frame, Rectangle bounds, Rectangle workArea, bool keepClearOfTaskbar)
    {
        var area = keepClearOfTaskbar ? workArea : bounds;
        if (frame.Width > 0 && frame.Height > 0 && area.Contains(frame))
            return FitResult.Fits;

        if (frame.Width <= 0 || frame.Height <= 0)
            return new FitResult(false, FitProblem.Empty, "Enter a width and height");

        if (frame.Width > bounds.Width || frame.Height > bounds.Height)
            return new FitResult(false, FitProblem.TooLarge, "Larger than this display");

        if (keepClearOfTaskbar && (frame.Width > workArea.Width || frame.Height > workArea.Height))
        {
            int excess = Math.Max(frame.Width - workArea.Width, frame.Height - workArea.Height);
            return new FitResult(false, FitProblem.OverlapsTaskbar, $"Overlaps the taskbar by {excess} px");
        }

        if (keepClearOfTaskbar && bounds.Contains(frame))
        {
            int overlap = Math.Max(
                Math.Max(workArea.Top - frame.Top, frame.Bottom - workArea.Bottom),
                Math.Max(workArea.Left - frame.Left, frame.Right - workArea.Right));
            return new FitResult(false, FitProblem.OverlapsTaskbar, $"Overlaps the taskbar by {overlap} px");
        }

        return new FitResult(false, FitProblem.OutsideDisplay, "Runs past the display edge");
    }
}

public enum FitProblem
{
    None,
    Empty,
    TooLarge,
    OverlapsTaskbar,
    OutsideDisplay
}

public sealed record FitResult(bool IsFit, FitProblem Problem, string Message)
{
    public static FitResult Fits { get; } = new(true, FitProblem.None, "Fits on this display");
}
