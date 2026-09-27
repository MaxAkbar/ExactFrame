namespace ExactFrame.Core.Geometry;

/// <summary>Where a frame sits inside the usable area of a display (a 3 × 3 grid).</summary>
public enum FrameAnchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight
}

public static class FrameAnchorExtensions
{
    public static int Column(this FrameAnchor anchor) => (int)anchor % 3;

    public static int Row(this FrameAnchor anchor) => (int)anchor / 3;

    public static string DisplayName(this FrameAnchor anchor) => anchor switch
    {
        FrameAnchor.TopLeft => "Top left",
        FrameAnchor.Top => "Top center",
        FrameAnchor.TopRight => "Top right",
        FrameAnchor.Left => "Middle left",
        FrameAnchor.Center => "Centered",
        FrameAnchor.Right => "Middle right",
        FrameAnchor.BottomLeft => "Bottom left",
        FrameAnchor.Bottom => "Bottom center",
        FrameAnchor.BottomRight => "Bottom right",
        _ => anchor.ToString()
    };
}
