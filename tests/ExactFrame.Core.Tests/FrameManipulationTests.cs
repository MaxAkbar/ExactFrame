using System.Drawing;
using ExactFrame.Core.Geometry;

namespace ExactFrame.Core.Tests;

public sealed class FrameManipulationTests
{
    private static readonly Rectangle Area = new(-1920, -1080, 3840, 2160);
    private static readonly Rectangle Original = new(-600, -400, 800, 600);

    [Theory]
    [InlineData(-600, -400, FrameHandle.TopLeft)]
    [InlineData(200, -400, FrameHandle.TopRight)]
    [InlineData(-600, 200, FrameHandle.BottomLeft)]
    [InlineData(200, 200, FrameHandle.BottomRight)]
    [InlineData(-200, -400, FrameHandle.Top)]
    [InlineData(-200, 200, FrameHandle.Bottom)]
    [InlineData(-600, -100, FrameHandle.Left)]
    [InlineData(200, -100, FrameHandle.Right)]
    [InlineData(-606, -394, FrameHandle.TopLeft)]
    [InlineData(-500, -400, FrameHandle.Move)]
    [InlineData(-500, -100, FrameHandle.None)]
    [InlineData(-700, -400, FrameHandle.None)]
    public void Hit_testing_distinguishes_handles_move_ring_and_transparent_interior(int x, int y, FrameHandle expected)
    {
        Assert.Equal(expected, FrameManipulation.HitTest(Original, new Point(x, y), 6, includeInterior: false));
    }

    [Fact]
    public void Preview_interior_can_move_without_intercepting_outside_pixels()
    {
        Assert.Equal(FrameHandle.Move, FrameManipulation.HitTest(Original, new Point(-500, -100), 6, true));
        Assert.Equal(FrameHandle.None, FrameManipulation.HitTest(Original, new Point(-700, -100), 6, true));
    }

    [Fact]
    public void Large_preview_hit_radius_keeps_a_small_frames_center_available_for_moving()
    {
        var small = new Rectangle(100, 100, 100, 100);
        Assert.Equal(FrameHandle.Move, FrameManipulation.HitTest(small, new Point(150, 150), 54, true));
        Assert.Equal(FrameHandle.None, FrameManipulation.HitTest(small, new Point(150, 150), 54, false));
        Assert.Equal(FrameHandle.TopLeft, FrameManipulation.HitTest(small, new Point(105, 105), 54, true));
        Assert.Equal(FrameHandle.Top, FrameManipulation.HitTest(small, new Point(150, 100), 54, true));
    }

    [Fact]
    public void Move_preserves_size_and_clamps_at_negative_display_origin()
    {
        var result = FrameManipulation.Drag(Original, Point.Empty, new Point(-5000, 5000), FrameHandle.Move, Area);
        Assert.Equal(new Rectangle(-1920, 480, 800, 600), result);
    }

    [Theory]
    [InlineData(FrameHandle.Left, -100, 0, -700, -400, 900, 600)]
    [InlineData(FrameHandle.Right, 100, 0, -600, -400, 900, 600)]
    [InlineData(FrameHandle.Top, 0, -100, -600, -500, 800, 700)]
    [InlineData(FrameHandle.Bottom, 0, 100, -600, -400, 800, 700)]
    [InlineData(FrameHandle.TopLeft, -100, -100, -700, -500, 900, 700)]
    [InlineData(FrameHandle.TopRight, 100, -100, -600, -500, 900, 700)]
    [InlineData(FrameHandle.BottomLeft, -100, 100, -700, -400, 900, 700)]
    [InlineData(FrameHandle.BottomRight, 100, 100, -600, -400, 900, 700)]
    public void Resizing_keeps_the_opposite_edges_fixed(FrameHandle handle, int dx, int dy, int x, int y, int width, int height)
    {
        Assert.Equal(new Rectangle(x, y, width, height),
            FrameManipulation.Drag(Original, new Point(100, 100), new Point(100 + dx, 100 + dy), handle, Area));
    }

    [Fact]
    public void Crossing_the_opposite_corner_stops_at_minimum_without_flipping()
    {
        var result = FrameManipulation.Drag(Original, Point.Empty, new Point(5000, 5000), FrameHandle.TopLeft, Area);
        Assert.Equal(new Rectangle(100, 100, 100, 100), result);
    }

    [Fact]
    public void Resizing_stops_at_display_boundary_and_maximum_size()
    {
        Assert.Equal(Rectangle.FromLTRB(-1920, -1080, 200, 200),
            FrameManipulation.Drag(Original, Point.Empty, new Point(-5000, -5000), FrameHandle.TopLeft, Area));
        Assert.Equal(new Size(16384, 16384), FrameManipulation.Drag(new Rectangle(0, 0, 800, 600),
            Point.Empty, new Point(30000, 30000), FrameHandle.BottomRight, new Rectangle(0, 0, 40000, 40000)).Size);
    }

    [Fact]
    public void Every_update_is_relative_to_gesture_start_so_snap_does_not_accumulate()
    {
        var targets = new[] { new Rectangle(250, -400, 200, 600) };
        var snapped = FrameManipulation.Drag(Original, Point.Empty, new Point(46, 0), FrameHandle.Move, Area,
            snapTargets: targets, snapDistance: 6);
        var released = FrameManipulation.Drag(Original, Point.Empty, new Point(60, 0), FrameHandle.Move, Area,
            snapTargets: targets, snapDistance: 6);
        Assert.Equal(-550, snapped.Left);
        Assert.Equal(-540, released.Left);
    }

    [Fact]
    public void Move_snaps_edges_and_centers_and_can_disable_snapping()
    {
        var frame = new Rectangle(100, 100, 400, 200);
        var area = new Rectangle(0, 0, 1920, 1080);
        var snapped = FrameManipulation.Drag(frame, Point.Empty, new Point(657, 338), FrameHandle.Move, area, snapDistance: 6);
        Assert.Equal(new Point(760, 440), snapped.Location);
        var disabled = FrameManipulation.Drag(frame, Point.Empty, new Point(657, 338), FrameHandle.Move, area, snapDistance: 0);
        Assert.Equal(new Point(757, 438), disabled.Location);
    }

    [Fact]
    public void Free_corner_can_snap_each_axis_to_a_different_window_line()
    {
        var result = FrameManipulation.Drag(Original, Point.Empty, new Point(46, 46), FrameHandle.BottomRight, Area,
            snapTargets: [new Rectangle(250, 250, 400, 200)], snapDistance: 6);
        Assert.Equal(new Rectangle(-600, -400, 850, 650), result);
    }

    [Fact]
    public void Snapping_does_not_override_the_minimum_size_or_display_boundary()
    {
        var frame = new Rectangle(100, 100, 200, 200);
        var result = FrameManipulation.Drag(frame, Point.Empty, new Point(-95, -95), FrameHandle.BottomRight,
            new Rectangle(0, 0, 1000, 1000), snapTargets: [new Rectangle(195, 195, 5, 5)], snapDistance: 20);
        Assert.True(result.Width >= 100 && result.Height >= 100);
        Assert.Equal(new Point(100, 100), result.Location);
    }

    [Theory]
    [InlineData(FrameHandle.TopLeft, -100, -20)]
    [InlineData(FrameHandle.TopRight, 100, -20)]
    [InlineData(FrameHandle.BottomLeft, -100, 20)]
    [InlineData(FrameHandle.BottomRight, 100, 20)]
    [InlineData(FrameHandle.Left, -100, 0)]
    [InlineData(FrameHandle.Right, 100, 0)]
    [InlineData(FrameHandle.Top, 0, -100)]
    [InlineData(FrameHandle.Bottom, 0, 100)]
    public void Aspect_locked_resizing_keeps_ratio_and_the_opposite_anchor(FrameHandle handle, int dx, int dy)
    {
        var frame = FrameManipulation.Drag(Original, Point.Empty, new Point(dx, dy), handle, Area, aspectRatio: 4d / 3);
        Assert.InRange(Math.Abs(frame.Height - frame.Width * .75), 0, .5);
        Assert.True(Area.Contains(frame));
        if (handle.HasFlag(FrameHandle.Left)) Assert.Equal(Original.Right, frame.Right);
        if (handle.HasFlag(FrameHandle.Right)) Assert.Equal(Original.Left, frame.Left);
        if (handle.HasFlag(FrameHandle.Top)) Assert.Equal(Original.Bottom, frame.Bottom);
        if (handle.HasFlag(FrameHandle.Bottom)) Assert.Equal(Original.Top, frame.Top);
    }

    [Fact]
    public void Aspect_locked_edge_keeps_perpendicular_center_and_constrains_both_dimensions()
    {
        var original = new Rectangle(300, 200, 400, 200);
        var result = FrameManipulation.Drag(original, Point.Empty, new Point(2000, 0), FrameHandle.Right,
            new Rectangle(0, 0, 1000, 600), aspectRatio: 2);
        Assert.Equal(new Rectangle(300, 125, 700, 350), result);
        var shrunk = FrameManipulation.Drag(original, Point.Empty, new Point(-2000, 0), FrameHandle.Right,
            new Rectangle(0, 0, 1000, 600), aspectRatio: 2);
        Assert.Equal(new Size(200, 100), shrunk.Size);
    }

    [Fact]
    public void Aspect_locked_snapping_uses_one_constraint_without_distorting_the_other_axis()
    {
        var frame = FrameManipulation.Drag(Original, Point.Empty, new Point(47, 20), FrameHandle.BottomRight, Area,
            aspectRatio: 4d / 3, snapTargets: [new Rectangle(250, 260, 200, 200)], snapDistance: 6);
        Assert.Equal(250, frame.Right);
        Assert.Equal(850, frame.Width);
        Assert.Equal(638, frame.Height);
        Assert.Equal(Original.Location, frame.Location);
    }

    [Fact]
    public void Draw_supports_reverse_drag_negative_origins_and_rejectable_small_boxes()
    {
        Assert.Equal(new Rectangle(-1920, -1080, 1920, 1080),
            FrameManipulation.Draw(Point.Empty, new Point(-5000, -5000), Area));
        Assert.Equal(new Rectangle(10, 10, 15, 20), FrameManipulation.Draw(new Point(25, 30), new Point(10, 10), Area));
        Assert.Equal(new Rectangle(10, 10, 0, 0), FrameManipulation.Draw(new Point(10, 10), new Point(10, 10), Area));
    }

    [Fact]
    public void Invalid_resize_handles_and_unrepresentable_aspects_leave_the_frame_unchanged()
    {
        Assert.Equal(Original, FrameManipulation.Drag(Original, Point.Empty, new Point(50, 50), FrameHandle.Left | FrameHandle.Right, Area));
        Assert.Equal(Original, FrameManipulation.Drag(Original, Point.Empty, new Point(50, 50), FrameHandle.BottomRight, Area, aspectRatio: 500));
    }
}
