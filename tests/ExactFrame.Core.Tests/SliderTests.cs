using System.Drawing;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class SliderTests : IDisposable
{
    private readonly FakeDisplays _displays = new();
    private readonly FakeOverlay _overlay = new();
    private readonly MemoryStore _store = new();
    private MainViewModel? _vm;

    // Primary display: 3840 × 2160, work area 3840 × 2088. Starts at 1920 × 1080, aspect locked (16:9).
    private MainViewModel Create()
    {
        _vm = new MainViewModel(_displays, new FakeWindows(), new FakePicker(), _overlay, new FakeHotkeys(),
            new FakeClipboard(), new FakeDialogs(), _store);
        _vm.Initialize();
        return _vm;
    }

    public void Dispose() => _vm?.Dispose();

    [Fact]
    public void Range_is_the_largest_size_that_fits_the_display()
    {
        var vm = Create();

        Assert.Equal(3840, vm.WidthSliderMaximum);
        Assert.Equal(2160, vm.HeightSliderMaximum);
    }

    [Fact]
    public void Range_keeps_the_aspect_ratio_inside_the_work_area()
    {
        var vm = Create();

        vm.KeepClearOfTaskbar = true;

        Assert.Equal(3712, vm.WidthSliderMaximum); // 2088 × 16 / 9
        Assert.Equal(2088, vm.HeightSliderMaximum);
    }

    [Fact]
    public void Range_uses_each_side_when_the_aspect_ratio_is_unlocked()
    {
        var vm = Create();
        vm.Presets.Single(p => p.Key == "vertical").SelectCommand.Execute(null);
        Assert.Equal(1215, vm.WidthSliderMaximum); // 2160 × 9 / 16

        vm.IsAspectLocked = false;

        Assert.Equal(3840, vm.WidthSliderMaximum);
        Assert.Equal(2160, vm.HeightSliderMaximum);
    }

    [Fact]
    public void Range_never_falls_below_a_typed_size()
    {
        var vm = Create();
        vm.IsAspectLocked = false;

        vm.WidthValue = 5000;

        Assert.Equal(5000, vm.WidthSliderMaximum);
    }

    [Fact]
    public void Range_follows_the_selected_display()
    {
        var vm = Create();

        vm.Displays[1].SelectCommand.Execute(null);

        Assert.Equal(2560, vm.WidthSliderMaximum);
        Assert.Equal(1440, vm.HeightSliderMaximum);
    }

    [Fact]
    public void Dragging_near_a_standard_size_snaps_to_it_and_keeps_the_ratio()
    {
        var vm = Create();

        vm.SetWidthFromSlider(1266);

        Assert.Equal(1280, vm.WidthValue);
        Assert.Equal(720, vm.HeightValue);
        Assert.True(vm.Presets.Single(p => p.Key == "720p").IsSelected);
    }

    [Fact]
    public void Dragging_the_height_snaps_too()
    {
        var vm = Create();

        vm.SetHeightFromSlider(1452);

        Assert.Equal(2560, vm.WidthValue);
        Assert.Equal(1440, vm.HeightValue);
    }

    [Fact]
    public void Dragging_to_the_far_end_fills_the_usable_area()
    {
        var vm = Create();
        vm.KeepClearOfTaskbar = true;

        vm.SetWidthFromSlider(vm.WidthSliderMaximum);

        Assert.Equal(3712, vm.WidthValue);
        Assert.Equal(2088, vm.HeightValue);
        Assert.True(vm.IsFit);
    }

    [Fact]
    public void Other_sizes_become_even_on_both_sides()
    {
        var vm = Create();

        vm.SetWidthFromSlider(1001);

        Assert.Equal(1000, vm.WidthValue);
        Assert.Equal(562, vm.HeightValue); // 562.5 rounded to even
    }

    [Fact]
    public void Arrow_key_steps_move_off_a_standard_size()
    {
        var vm = Create();

        vm.SetWidthFromSlider(1920 + MainViewModel.SliderSmallChange);
        Assert.Equal(1922, vm.WidthValue);

        vm.SetWidthFromSlider(1922 - MainViewModel.SliderSmallChange);
        Assert.Equal(1920, vm.WidthValue);
    }

    [Fact]
    public void Unlocked_sliders_change_one_side()
    {
        var vm = Create();
        vm.IsAspectLocked = false;

        vm.SetHeightFromSlider(800);

        Assert.Equal(1920, vm.WidthValue);
        Assert.Equal(800, vm.HeightValue);
    }

    [Fact]
    public void Slider_moves_keep_a_visible_outline_in_sync()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);

        vm.SetWidthFromSlider(2566);

        Assert.True(_overlay.IsVisible);
        Assert.Equal(new Rectangle(640, 360, 2560, 1440), _overlay.Frame);
    }

    [Fact]
    public void Custom_frame_resizes_around_its_center_until_it_reaches_an_edge()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        vm.XValue = 1000;
        vm.YValue = 500;

        vm.SetWidthFromSlider(2560);

        Assert.Equal(new Rectangle(680, 320, 2560, 1440), vm.CurrentFrame);
        Assert.Equal(vm.CurrentFrame, _overlay.Frame);
        Assert.True(_overlay.IsVisible);
    }

    [Fact]
    public void Slider_keeps_a_custom_frame_inside_the_right_and_bottom_edges()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        _overlay.Drag(new Point(1920, 1080));

        vm.SetWidthFromSlider(2560);

        Assert.Equal(new Rectangle(1280, 720, 2560, 1440), vm.CurrentFrame);
        Assert.Equal(vm.CurrentFrame, _overlay.Frame);
        Assert.True(_overlay.IsVisible);
        Assert.True(vm.IsFit);
    }

    [Fact]
    public void Range_changes_are_announced()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.KeepClearOfTaskbar = true;
        vm.IsAspectLocked = false;

        Assert.Equal(2, changed.Count(n => n == nameof(MainViewModel.WidthSliderMaximum)));
        Assert.Equal(2, changed.Count(n => n == nameof(MainViewModel.HeightSliderMaximum)));
    }
}
