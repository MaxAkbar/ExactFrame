using System.Drawing;
using ExactFrame.Core.Geometry;

namespace ExactFrame.Core.Tests;

// Includes every check from the original GeometryChecks console app.
public sealed class FrameGeometryTests
{
    [Fact]
    public void Center_keeps_negative_monitor_origin()
    {
        var leftMonitor = new Rectangle(-2560, -240, 2560, 1440);
        Assert.Equal(new Rectangle(-2240, -60, 1920, 1080), FrameGeometry.Center(new Size(1920, 1080), leftMonitor));
    }

    [Fact]
    public void Center_full_screen_frame_is_exact()
    {
        var fullHd = new Rectangle(0, 0, 1920, 1080);
        Assert.Equal(fullHd, FrameGeometry.Center(fullHd.Size, fullHd));
    }

    [Fact]
    public void Center_rejects_oversized_frames_instead_of_shrinking()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FrameGeometry.Center(new Size(2560, 1440), new Rectangle(0, 0, 1920, 1080)));
    }

    [Fact]
    public void OuterForCapture_compensates_asymmetric_client_chrome()
    {
        var outer = new Rectangle(-8, 20, 1296, 759);
        var client = new Rectangle(0, 51, 1280, 720);
        var desired = new Rectangle(200, 120, 1920, 1080);
        Assert.Equal(new Rectangle(192, 89, 1936, 1119), FrameGeometry.OuterForCapture(outer, client, desired));
    }

    [Fact]
    public void OuterForCapture_ignores_invisible_resize_borders()
    {
        var physicalOuter = new Rectangle(88, 100, 1944, 1092);
        var physicalVisible = new Rectangle(100, 100, 1920, 1080);
        Assert.Equal(new Rectangle(288, 200, 2584, 1452),
            FrameGeometry.OuterForCapture(physicalOuter, physicalVisible, new Rectangle(300, 200, 2560, 1440)));
    }

    [Fact]
    public void ClampPosition_never_changes_size_or_escapes_monitor()
    {
        var random = new Random(42);
        for (int i = 0; i < 1000; i++)
        {
            var monitor = new Rectangle(random.Next(-8000, 8000), random.Next(-4000, 4000),
                random.Next(500, 5000), random.Next(500, 4000));
            var frame = new Rectangle(random.Next(-12000, 12000), random.Next(-10000, 10000),
                random.Next(100, monitor.Width + 1), random.Next(100, monitor.Height + 1));
            var clamped = FrameGeometry.ClampPosition(frame, monitor);
            Assert.Equal(frame.Size, clamped.Size);
            Assert.True(monitor.Contains(clamped));
        }
    }

    [Theory]
    [InlineData(FrameAnchor.TopLeft, 0, 0)]
    [InlineData(FrameAnchor.Top, 960, 0)]
    [InlineData(FrameAnchor.TopRight, 1920, 0)]
    [InlineData(FrameAnchor.Center, 960, 540)]
    [InlineData(FrameAnchor.BottomRight, 1920, 1080)]
    [InlineData(FrameAnchor.Left, 0, 540)]
    public void Place_puts_frame_at_anchor(FrameAnchor anchor, int x, int y)
    {
        var area = new Rectangle(0, 0, 3840, 2160);
        Assert.Equal(new Rectangle(x, y, 1920, 1080), FrameGeometry.Place(new Size(1920, 1080), area, anchor));
    }

    [Fact]
    public void Place_respects_area_origin()
    {
        var area = new Rectangle(-2560, 100, 2560, 1340);
        Assert.Equal(new Rectangle(-1280, 720, 1280, 720),
            FrameGeometry.Place(new Size(1280, 720), area, FrameAnchor.BottomRight));
    }

    [Fact]
    public void CheckFit_reports_fit_and_each_problem()
    {
        var bounds = new Rectangle(0, 0, 3840, 2160);
        var work = new Rectangle(0, 0, 3840, 2088);

        Assert.True(FrameGeometry.CheckFit(new Rectangle(960, 540, 1920, 1080), bounds, work, true).IsFit);

        var tooLarge = FrameGeometry.CheckFit(new Rectangle(0, 0, 5120, 2880), bounds, work, false);
        Assert.Equal(FitProblem.TooLarge, tooLarge.Problem);

        var full = FrameGeometry.CheckFit(new Rectangle(0, -36, 3840, 2160), bounds, work, true);
        Assert.Equal(FitProblem.OverlapsTaskbar, full.Problem);
        Assert.Equal("Overlaps the taskbar by 72 px", full.Message);

        var low = FrameGeometry.CheckFit(new Rectangle(0, 1080, 1920, 1080), bounds, work, true);
        Assert.Equal("Overlaps the taskbar by 72 px", low.Message);

        var outside = FrameGeometry.CheckFit(new Rectangle(3000, 0, 1920, 1080), bounds, work, false);
        Assert.Equal(FitProblem.OutsideDisplay, outside.Problem);
    }

    [Theory]
    [InlineData(1920, 1080, "16:9")]
    [InlineData(1080, 1920, "9:16")]
    [InlineData(1080, 1080, "1:1")]
    [InlineData(1024, 768, "4:3")]
    [InlineData(2560, 1080, "21:9")]
    [InlineData(1000, 700, "10:7")]
    public void AspectRatio_describes_common_ratios(int width, int height, string expected) =>
        Assert.Equal(expected, AspectRatio.Describe(width, height));
}
