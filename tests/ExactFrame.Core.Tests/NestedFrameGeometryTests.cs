using System.Drawing;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.Tests;

public sealed class NestedFrameGeometryTests
{
    [Fact]
    public void Shorts_frame_has_an_exact_nine_by_sixteen_ratio_inside_landscape_frame()
    {
        var parent = new Rectangle(400, 300, 1920, 1080);
        var nested = NestedFrameGeometry.Place(parent, new NestedFrame());

        Assert.Equal(new Rectangle(1063, 312, 594, 1056), nested);
        Assert.Equal(nested.Width * 16, nested.Height * 9);
        Assert.True(parent.Contains(nested));
    }

    [Fact]
    public void Scale_and_anchor_keep_the_frame_inside_its_parent()
    {
        var parent = new Rectangle(-1200, 100, 1280, 720);
        var frame = new NestedFrame { AspectWidth = 9, AspectHeight = 16, ScalePercent = 50, Anchor = FrameAnchor.BottomRight };

        var nested = NestedFrameGeometry.Place(parent, frame);

        Assert.Equal(new Size(198, 352), nested.Size);
        Assert.Equal(parent.Right - 8, nested.Right);
        Assert.Equal(parent.Bottom - 8, nested.Bottom);
        Assert.True(parent.Contains(nested));
    }

    [Fact]
    public void Moving_the_parent_moves_the_nested_frame_without_changing_its_size()
    {
        var frame = new NestedFrame { AspectWidth = 4, AspectHeight = 5 };
        var first = NestedFrameGeometry.Place(new Rectangle(0, 0, 1920, 1080), frame);
        var moved = NestedFrameGeometry.Place(new Rectangle(500, -200, 1920, 1080), frame);

        Assert.Equal(first.Size, moved.Size);
        Assert.Equal(new Point(first.X + 500, first.Y - 200), moved.Location);
    }
}
