using System.Drawing;
using System.Text.Json.Nodes;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;
using ExactFrame.Core.Services;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class CompositionTests
{
    private static readonly Rectangle Parent = new(-1920, 100, 1920, 1080);

    [Fact]
    public void Free_position_uses_parent_relative_physical_pixels_and_clamps_inside_inset()
    {
        var original = new NestedFrame { ScalePercent = 50 };
        var free = NestedFrameGeometry.PositionAt(Parent, original, 123, 240);
        var crop = NestedFrameGeometry.Place(Parent, free);
        Assert.True(free.UseCustomPosition);
        Assert.Equal(new Point(Parent.X + 123, Parent.Y + 240), crop.Location);
        Assert.Equal(NestedFrameGeometry.Place(Parent, original).Size, crop.Size);
        var beyond = NestedFrameGeometry.PositionAt(Parent, free, -500, 100000);
        var clamped = NestedFrameGeometry.Place(Parent, beyond);
        Assert.Equal(Parent.Left + 8, clamped.Left);
        Assert.Equal(Parent.Bottom - 8, clamped.Bottom);
    }

    [Fact]
    public void Normalized_free_position_survives_parent_moves_resizes_and_serialization()
    {
        var frame = new NestedFrame { UseCustomPosition = true, PositionXPercent = 25, PositionYPercent = 75, ScalePercent = 50 };
        var first = NestedFrameGeometry.Place(Parent, frame);
        var movedParent = new Rectangle(1000, -600, Parent.Width, Parent.Height);
        var moved = NestedFrameGeometry.Place(movedParent, frame);
        Assert.Equal(new Point(first.X - Parent.X, first.Y - Parent.Y), new Point(moved.X - movedParent.X, moved.Y - movedParent.Y));
        var resizedParent = new Rectangle(0, 0, 1280, 720);
        var resized = NestedFrameGeometry.Place(resizedParent, frame);
        var area = NestedFrameGeometry.AvailableArea(resizedParent, frame);
        Assert.InRange(Math.Abs(resized.X - area.X - (area.Width - resized.Width) * .25), 0, .5);
        Assert.InRange(Math.Abs(resized.Y - area.Y - (area.Height - resized.Height) * .75), 0, .5);
        Assert.True(resizedParent.Contains(resized));
    }

    [Fact]
    public void Insets_and_safe_margins_are_independent_and_do_not_change_the_requested_output()
    {
        var frame = new NestedFrame { AspectWidth = 1, AspectHeight = 1, InsetPixels = 0, Anchor = FrameAnchor.TopLeft, SafeMarginPercent = 5, OutputWidth = 1080, OutputHeight = 1080 };
        var layout = CompositionLayout.Create(Parent, frame);
        Assert.Equal(new Rectangle(Parent.X, Parent.Y, 1080, 1080), layout.Crop);
        Assert.Equal(Rectangle.Inflate(layout.Crop, -54, -54), layout.SafeArea);
        Assert.Equal(new Size(1080, 1080), layout.OutputSize);
        Assert.False(layout.NeedsEnlargement);
        Assert.Equal(1, layout.Scale);
        Assert.True(CompositionLayout.Create(Parent, frame with { InsetPixels = 1000 }).Crop.IsEmpty);
    }

    [Fact]
    public void Vertical_1080_output_from_landscape_source_reports_enlargement_without_inventing_source_pixels()
    {
        var layout = CompositionLayout.Create(Parent, new NestedFrame { OutputWidth = 1080, OutputHeight = 1920, SafeMarginPercent = 5 });
        Assert.Equal(new Size(594, 1056), layout.Crop.Size);
        Assert.Equal(new Size(1080, 1920), layout.OutputSize);
        Assert.Equal(1080d / 594, layout.Scale, 10);
        Assert.True(layout.NeedsEnlargement);
        Assert.False(layout.HasBars);
        Assert.Contains("1.82×", layout.ScaleText);
        Assert.Contains("softer", layout.ScaleText);
    }

    [Fact]
    public void Different_output_ratio_uses_uniform_fit_with_bars_instead_of_stretching()
    {
        var layout = CompositionLayout.Create(Parent, new NestedFrame { AspectWidth = 1, AspectHeight = 1, OutputWidth = 1920, OutputHeight = 1080 });
        Assert.Equal(new RectangleF(420, 0, 1080, 1080), layout.ContentBounds);
        Assert.True(layout.HasBars);
        Assert.Contains("bars", layout.ScaleText);
        Assert.Equal(layout.ContentBounds.Width / layout.Crop.Width, layout.ContentBounds.Height / layout.Crop.Height, 5);
    }

    [Fact]
    public void Legacy_guide_has_no_assumed_output_or_safe_margin()
    {
        var layout = CompositionLayout.Create(Parent, new NestedFrame());
        Assert.False(layout.HasOutput);
        Assert.Equal(layout.Crop.Size, layout.OutputSize);
        Assert.Equal(1, layout.Scale);
        Assert.False(layout.NeedsEnlargement);
        Assert.True(layout.SafeArea.IsEmpty);
    }

    [Fact]
    public void Tiny_exact_ratio_crops_omit_a_safe_guide_that_has_no_interior_pixels()
    {
        var layout = CompositionLayout.Create(new Rectangle(0, 0, 100, 100),
            new NestedFrame { AspectWidth = 1, AspectHeight = 32, ScalePercent = 10, SafeMarginPercent = 40 });
        Assert.Equal(new Size(1, 32), layout.Crop.Size);
        Assert.True(layout.SafeArea.IsEmpty);
    }

    [Fact]
    public void Presets_update_output_and_crop_ratio_custom_outputs_keep_the_chosen_crop()
    {
        var item = new NestedFrameItem(new NestedFrame(), _ => { }, () => { });
        item.UpdateBounds(NestedFrameGeometry.Place(Parent, item.Frame), Parent);
        item.OutputPresetIndex = 3;
        Assert.Equal(1080, item.OutputWidth);
        Assert.Equal(1350, item.OutputHeight);
        Assert.Equal(4, item.AspectWidth); Assert.Equal(5, item.AspectHeight);
        item.OutputWidth = 1000;
        Assert.Equal(CompositionPreset.All.Count, item.OutputPresetIndex);
        Assert.Equal(4, item.AspectWidth); Assert.Equal(5, item.AspectHeight);
        item.OutputPresetIndex = 0;
        Assert.False(item.HasOutput);
        item.OutputPresetIndex = CompositionPreset.All.Count;
        Assert.True(item.HasOutput);
    }

    [Fact]
    public void Binding_the_current_pixel_position_does_not_silently_change_an_anchor_to_free_position()
    {
        var item = new NestedFrameItem(new NestedFrame(), _ => { }, () => { });
        item.UpdateBounds(NestedFrameGeometry.Place(Parent, item.Frame), Parent);
        item.XPosition = item.XPosition; item.YPosition = item.YPosition;
        Assert.False(item.HasCustomPosition);
        Assert.Equal((int)FrameAnchor.Center, item.AnchorIndex);
        item.AnchorIndex = 9;
        Assert.True(item.HasCustomPosition);
    }

    [Fact]
    public void Version_two_round_trip_preserves_compositions_and_version_one_missing_fields_preserves_old_guides()
    {
        var original = Composition();
        var profile = new FrameProfile { NestedFrames = [original] };
        string json = ProfilePackageSerializer.Export([profile], [FakeDisplays.Primary]);
        var parsed = ProfilePackageSerializer.Parse(json);
        Assert.Equal(2, parsed.Version);
        Assert.Equal(original, parsed.Profiles[0].Profile.NestedFrames.Single());
        var legacy = JsonNode.Parse(json)!;
        legacy["version"] = 1;
        var guide = legacy["profiles"]![0]!["profile"]!["nestedFrames"]![0]!.AsObject();
        foreach (string key in new[] { "useCustomPosition", "positionXPercent", "positionYPercent", "insetPixels", "safeMarginPercent", "outputWidth", "outputHeight" }) guide.Remove(key);
        var imported = ProfilePackageSerializer.Parse(legacy.ToJsonString()).Profiles[0].Profile.NestedFrames.Single();
        Assert.False(imported.UseCustomPosition);
        Assert.Equal(8, imported.InsetPixels);
        Assert.Equal(0, imported.SafeMarginPercent);
        Assert.Equal(0, imported.OutputWidth);
        Assert.Equal(NestedFrameGeometry.Place(Parent, original with { UseCustomPosition = false, InsetPixels = 8 }), NestedFrameGeometry.Place(Parent, imported));
    }

    [Theory]
    [InlineData("positionXPercent", -1)]
    [InlineData("positionYPercent", 101)]
    [InlineData("insetPixels", 1001)]
    [InlineData("safeMarginPercent", 41)]
    [InlineData("outputWidth", 0)]
    [InlineData("outputHeight", 16385)]
    public void Malformed_composition_fields_reject_the_whole_package(string property, int value)
    {
        var json = JsonNode.Parse(ProfilePackageSerializer.Export([new FrameProfile { NestedFrames = [Composition()] }], [FakeDisplays.Primary]))!;
        json["profiles"]![0]!["profile"]!["nestedFrames"]![0]![property] = value;
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Parse(json.ToJsonString()));
    }

    [Fact]
    public async Task Preview_freezes_the_setup_blocks_global_profile_changes_and_never_resizes_an_app()
    {
        var preview = new PendingPreview();
        var windows = new FakeWindows(); var hotkeys = new FakeHotkeys(); var store = new MemoryStore();
        using var vm = new MainViewModel(new FakeDisplays(), windows, new FakePicker(), new FakeOverlay(), hotkeys,
            new FakeClipboard(), new FakeDialogs(), store, new FakeStartup(), outputPreview: preview);
        vm.Initialize(); Assert.False(vm.PreviewOutputsCommand.CanExecute(null));
        vm.AddShortsFrameCommand.Execute(null); vm.NestedFrames[0].XPosition = 25;
        var frame = vm.CurrentFrame; var guide = vm.NestedFrames[0].Frame;
        var task = vm.PreviewOutputsCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.Equal(frame, preview.Source);
        Assert.Equal(guide, Assert.Single(preview.Frames));
        hotkeys.Press(101);
        Assert.Equal(frame, vm.CurrentFrame);
        preview.Complete.SetResult(); await task;
        Assert.False(vm.IsBusy); Assert.Empty(windows.Resizes);
        vm.ShowResizeModeCommand.Execute(null);
        Assert.False(vm.PreviewOutputsCommand.CanExecute(null));
    }

    private static NestedFrame Composition() => new()
    {
        Name = "Social crop", UseCustomPosition = true, PositionXPercent = 23.5, PositionYPercent = 87,
        InsetPixels = 16, SafeMarginPercent = 7, OutputWidth = 1080, OutputHeight = 1920, ScalePercent = 65
    };
    private sealed class PendingPreview : IOutputPreviewService
    {
        public TaskCompletionSource Complete { get; } = new();
        public Rectangle Source { get; private set; }
        public IReadOnlyList<NestedFrame> Frames { get; private set; } = [];
        public Task ShowAsync(Rectangle source, IReadOnlyList<NestedFrame> frames, CancellationToken cancellation = default)
        { Source = source; Frames = frames; return Complete.Task; }
    }
}
