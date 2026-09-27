using System.Drawing;
using ExactFrame.Core.Geometry;

namespace ExactFrame.Core.Tests;

public sealed class EdgeMagnifierGeometryTests
{
    [Fact]
    public void Detects_the_nearest_outline_edge_and_ignores_the_middle()
    {
        var frame = new Rectangle(100, 100, 800, 500);

        Assert.True(EdgeMagnifierGeometry.TryNearestEdge(frame, new Point(895, 350), 18, out var edge));
        Assert.Equal(FrameEdge.Right, edge);
        Assert.False(EdgeMagnifierGeometry.TryNearestEdge(frame, new Point(500, 350), 18, out _));
    }

    [Fact]
    public void Lens_flips_inside_a_negative_origin_display_at_its_right_and_bottom_edges()
    {
        var display = new Rectangle(-3840, 0, 3840, 2160);
        var pointer = new Point(-4, 2155);

        var lens = EdgeMagnifierGeometry.PlaceLens(pointer, new Size(272, 300), display, 32);

        Assert.Equal(new Rectangle(-308, 1823, 272, 300), lens);
        Assert.True(display.Contains(lens));
    }
}
