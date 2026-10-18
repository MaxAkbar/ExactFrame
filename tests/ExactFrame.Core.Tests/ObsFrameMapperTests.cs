using System.Drawing;
using System.Text.Json;
using ExactFrame.Core.Obs;

namespace ExactFrame.Core.Tests;

public sealed class ObsFrameMapperTests
{
    internal static ObsSnapshot Snapshot(Rectangle? display = null, ObsVideoSize? video = null) => new(
        "Collection", new("Tutorial", "scene-1"), new(7, "Desktop", "source-1"), "monitor-1",
        display ?? new Rectangle(-3840, -240, 3840, 2160), "Display 1",
        video ?? new(1920, 1080, 1280, 720),
        JsonSerializer.SerializeToElement(new { sourceWidth = 3840, sourceHeight = 2160 }));

    [Fact]
    public void Negative_desktop_coordinates_become_source_pixel_crops()
    {
        var plan = ObsFrameMapper.Create(Snapshot(), new("main", "Main", new(-3520, 0, 1920, 1080)), ObsPlacement.FitToCanvas);
        Assert.Equal((320, 240, 1600, 840), (plan.CropLeft, plan.CropTop, plan.CropRight, plan.CropBottom));
        Assert.Equal(1, plan.Scale);
        Assert.Equal((0d, 0d), (plan.X, plan.Y));
    }

    [Fact]
    public void Portrait_region_is_letterboxed_without_stretching()
    {
        var plan = ObsFrameMapper.Create(Snapshot(), new("portrait", "Portrait", new(-3000, 0, 540, 960)), ObsPlacement.FitToCanvas);
        Assert.Equal(1.125, plan.Scale);
        Assert.Equal(656.5, plan.X);
        Assert.Equal(0, plan.Y);
        Assert.Equal(607.5, plan.Width);
        Assert.Equal(1080, plan.Height);
    }

    [Fact]
    public void Actual_pixels_centers_with_no_display_or_output_scaling()
    {
        var plan = ObsFrameMapper.Create(Snapshot(), new("main", "Main", new(-3000, 100, 1280, 720)), ObsPlacement.ActualPixels);
        Assert.Equal(1, plan.Scale);
        Assert.Equal((320d, 180d), (plan.X, plan.Y));
        Assert.Equal(840, plan.CropLeft);
    }

    [Fact]
    public void Full_monitor_can_be_downscaled_to_the_canvas()
    {
        var snapshot = Snapshot();
        var plan = ObsFrameMapper.Create(snapshot, new("main", "Main", snapshot.DisplayBounds), ObsPlacement.FitToCanvas);
        Assert.Equal((0, 0, 0, 0), (plan.CropLeft, plan.CropTop, plan.CropRight, plan.CropBottom));
        Assert.Equal(0.5, plan.Scale);
    }

    [Theory]
    [InlineData(-3841, -240, 100, 100)]
    [InlineData(-3840, -241, 100, 100)]
    [InlineData(-20, 0, 100, 100)]
    [InlineData(-3000, 1900, 100, 100)]
    [InlineData(-3000, 0, 0, 100)]
    public void Out_of_source_regions_are_rejected_instead_of_clamped(int x, int y, int width, int height) =>
        Assert.Throws<ObsException>(() => ObsFrameMapper.Create(Snapshot(), new("main", "Main", new(x, y, width, height)), ObsPlacement.FitToCanvas));

    [Fact]
    public void Actual_pixels_rejects_a_region_larger_than_the_canvas() =>
        Assert.Throws<ObsException>(() => ObsFrameMapper.Create(Snapshot(), new("main", "Main", new(-3800, 0, 2560, 1440)), ObsPlacement.ActualPixels));

    [Fact]
    public void Filtered_or_stale_source_dimensions_are_rejected()
    {
        var snapshot = Snapshot() with { Transform = JsonSerializer.SerializeToElement(new { sourceWidth = 1920, sourceHeight = 1080 }) };
        Assert.Throws<ObsException>(() => ObsFrameMapper.Create(snapshot, new("main", "Main", new(-3000, 0, 1280, 720)), ObsPlacement.FitToCanvas));
    }

    [Fact]
    public void Invalid_video_dimensions_are_rejected() =>
        Assert.Throws<ObsException>(() => ObsFrameMapper.Create(Snapshot(video: new(0, 1080, 1920, 1080)),
            new("main", "Main", new(-3000, 0, 1280, 720)), ObsPlacement.FitToCanvas));
}
