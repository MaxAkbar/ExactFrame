using System.Drawing;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class FrameEditingViewModelTests : IDisposable
{
    private readonly FakeDisplays _displays = new();
    private readonly FakeWindows _windows = new();
    private readonly FakePicker _windowPicker = new();
    private readonly FakeOverlay _overlay = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly MemoryStore _store = new();
    private readonly RegionPicker _regionPicker = new();
    private MainViewModel? _vm;

    private MainViewModel Create()
    {
        _vm = new MainViewModel(_displays, _windows, _windowPicker, _overlay, new FakeHotkeys(),
            new FakeClipboard(), _dialogs, _store, new FakeStartup(), _regionPicker);
        _vm.Initialize();
        return _vm;
    }

    public void Dispose() => _vm?.Dispose();

    [Fact]
    public async Task Interactive_size_and_position_are_atomic_and_saved_in_profiles_and_last_session()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        var expected = new Rectangle(300, 200, 1280, 720);
        int observations = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(vm.WidthValue) or nameof(vm.HeightValue) or nameof(vm.XValue) or nameof(vm.YValue))) return;
            observations++;
            Assert.Equal(expected, vm.CurrentFrame);
            Assert.Equal(expected.Width, vm.WidthValue);
            Assert.Equal(expected.Height, vm.HeightValue);
            Assert.Equal(expected.X, vm.XValue);
            Assert.Equal(expected.Y, vm.YValue);
        };

        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(expected);
        vm.FinishFrameInteraction();

        Assert.True(observations >= 4);
        Assert.Equal(expected, _overlay.Frame);
        Assert.Equal("Custom position", vm.AnchorName);
        Assert.True(vm.Presets.Single(p => p.Key == "720p").IsSelected);
        _dialogs.ProfileName = "Dragged recording area";
        await vm.SaveProfileCommand.ExecuteAsync(null);
        var profile = _store.Settings.Profiles.Single(p => p.Name == "Dragged recording area");
        Assert.Equal(expected, new Rectangle(profile.X, profile.Y, profile.Width, profile.Height));
        Assert.Null(profile.Anchor);

        vm.Shutdown();
        var last = Assert.IsType<FrameProfile>(_store.Settings.LastFrame);
        Assert.Equal(expected, new Rectangle(last.X, last.Y, last.Width, last.Height));
        Assert.Null(last.Anchor);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public void Native_overlay_start_move_and_cancel_restore_the_original_anchor_and_bounds()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        var original = vm.CurrentFrame;

        _overlay.BeginInteraction();
        _overlay.Drag(new Point(200, 150));
        Assert.Equal(new Rectangle(200, 150, original.Width, original.Height), vm.CurrentFrame);
        _overlay.EndInteraction(canceled: true);

        Assert.Equal(original, vm.CurrentFrame);
        Assert.Equal(original, _overlay.Frame);
        Assert.Equal("Centered", vm.AnchorName);
        Assert.Contains("canceled", vm.FrameEditorHint);
    }

    [Fact]
    public void Cancel_restores_dimensions_aspect_lock_ratio_anchor_and_preset()
    {
        var vm = Create();
        var original = vm.CurrentFrame;
        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(100, 100, 900, 500), freeform: true);
        Assert.False(vm.IsAspectLocked);
        Assert.True(vm.Presets.Single(p => p.Key == "custom").IsSelected);

        vm.FinishFrameInteraction(cancel: true);

        Assert.Equal(original, vm.CurrentFrame);
        Assert.True(vm.IsAspectLocked);
        Assert.True(vm.Presets.Single(p => p.Key == "1080p").IsSelected);
        Assert.Equal("Centered", vm.AnchorName);
        vm.WidthValue = 1280;
        Assert.Equal(720, vm.HeightValue);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("position")]
    [InlineData("preset")]
    [InlineData("custom")]
    [InlineData("anchor")]
    [InlineData("display")]
    public void External_frame_edits_clear_the_previous_gesture_announcement(string edit)
    {
        var vm = Create();
        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(120, 140, 800, 600), freeform: true);
        vm.FinishFrameInteraction();
        const string announcement = "Frame 800 × 600 px at 120, 140.";
        Assert.Contains(announcement, vm.FrameEditorHint);
        bool hintChanged = false;
        vm.PropertyChanged += (_, e) => hintChanged |= e.PropertyName == nameof(vm.FrameEditorHint);

        switch (edit)
        {
            case "width": vm.WidthValue = 900; break;
            case "height": vm.HeightValue = 700; break;
            case "position": vm.XValue = 150; break;
            case "preset": vm.Presets.Single(p => p.Key == "1080p").SelectCommand.Execute(null); break;
            case "custom": vm.Presets.Single(p => p.Key == "custom").SelectCommand.Execute(null); break;
            case "anchor": vm.Anchors.Single(a => a.Anchor == FrameAnchor.Center).SelectCommand.Execute(null); break;
            case "display": vm.Displays[1].SelectCommand.Execute(null); break;
        }

        Assert.DoesNotContain(announcement, vm.FrameEditorHint);
        Assert.True(hintChanged);
        Assert.Contains("Drag to move", vm.FrameEditorHint);
    }

    [Fact]
    public async Task Applying_a_profile_clears_the_previous_gesture_announcement()
    {
        var vm = Create();
        var saved = vm.CurrentFrame;
        _dialogs.ProfileName = "Original frame";
        await vm.SaveProfileCommand.ExecuteAsync(null);
        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(120, 140, 800, 600), freeform: true);
        vm.FinishFrameInteraction();
        Assert.Contains("Frame 800 × 600 px at 120, 140.", vm.FrameEditorHint);

        vm.Profiles.Single(p => p.Name == "Original frame").SelectCommand.Execute(null);

        Assert.Equal(saved, vm.CurrentFrame);
        Assert.DoesNotContain("Frame 800 × 600 px at 120, 140.", vm.FrameEditorHint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selecting_new_bounds_clears_the_previous_gesture_announcement(bool draw)
    {
        var vm = Create();
        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(120, 140, 800, 600), freeform: true);
        vm.FinishFrameInteraction();
        Assert.Contains("Frame 800 × 600 px at 120, 140.", vm.FrameEditorHint);
        _regionPicker.Result = new Rectangle(100, 100, 900, 500);
        _windowPicker.Result = FakeWindows.Terminal;

        if (draw) await vm.DrawFrameCommand.ExecuteAsync(null);
        else await vm.UseWindowBoundsCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Frame 800 × 600 px at 120, 140.", vm.FrameEditorHint);
        Assert.Equal(draw ? _regionPicker.Result.Value : FakeWindows.Terminal.Bounds, vm.CurrentFrame);
    }

    [Fact]
    public void New_gesture_replaces_the_previous_announcement_and_cancel_announces_restoration()
    {
        var vm = Create();
        var original = new Rectangle(120, 140, 800, 600);
        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(original, freeform: true);
        vm.FinishFrameInteraction();
        Assert.Contains("Frame 800 × 600 px at 120, 140.", vm.FrameEditorHint);

        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(150, 170, 900, 700), freeform: true);
        Assert.DoesNotContain("Frame 800 × 600 px at 120, 140.", vm.FrameEditorHint);
        vm.FinishFrameInteraction(cancel: true);

        Assert.Equal(original, vm.CurrentFrame);
        Assert.Contains("Frame edit canceled.", vm.FrameEditorHint);
        vm.XValue = 130;
        Assert.DoesNotContain("Frame edit canceled.", vm.FrameEditorHint);
    }

    [Fact]
    public void Keyboard_moves_and_resizes_one_pixel_even_when_pointer_snapping_is_enabled()
    {
        var vm = Create();
        vm.SetFrameFromInteraction(new Rectangle(0, 0, 1280, 720), freeform: true);
        Assert.True(vm.SnapToEdges);

        vm.NudgeFrame(1, 1, resize: false);
        Assert.Equal(new Rectangle(1, 1, 1280, 720), vm.CurrentFrame);
        vm.NudgeFrame(1, -1, resize: true);
        Assert.Equal(new Rectangle(1, 1, 1281, 719), vm.CurrentFrame);
        vm.NudgeFrame(10, 0, resize: false);
        Assert.Equal(11, vm.XValue);
    }

    [Fact]
    public void Locked_keyboard_resize_keeps_ratio_and_does_not_move_the_top_left()
    {
        var vm = Create();
        vm.SetFrameFromInteraction(new Rectangle(100, 100, 1280, 720));
        vm.NudgeFrame(1, 0, resize: true);

        Assert.Equal(new Point(100, 100), vm.CurrentFrame.Location);
        Assert.Equal(1281, vm.WidthValue);
        Assert.InRange(Math.Abs(vm.HeightValue - vm.WidthValue * 9 / 16), 0, .5);
    }

    [Fact]
    public void Repeated_preview_resizes_and_keyboard_adjustments_keep_the_original_locked_ratio()
    {
        var vm = Create();
        vm.SetFrameFromInteraction(new Rectangle(100, 100, 1280, 720));
        const double ratio = 16d / 9;

        for (int i = 0; i < 16; i++)
        {
            vm.BeginFrameInteraction();
            var frame = FrameManipulation.Drag(vm.CurrentFrame, Point.Empty, new Point(1, 0),
                FrameHandle.Right, vm.SelectedFrameArea, aspectRatio: vm.FrameEditAspectRatio);
            vm.SetFrameFromInteraction(frame);
            vm.FinishFrameInteraction();
            Assert.Equal(ratio, vm.FrameEditAspectRatio);
            Assert.Equal(ratio, _overlay.EditingOptions?.AspectRatio);
            Assert.Equal(Math.Round(vm.WidthValue / ratio), vm.HeightValue);

            vm.NudgeFrame(1, 0, resize: true);
            Assert.Equal(Math.Round(vm.WidthValue / ratio), vm.HeightValue);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shutdown_restores_an_uncommitted_gesture_before_saving_the_last_session(bool native)
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        var original = vm.CurrentFrame;
        if (native) _overlay.BeginInteraction();
        else vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(100, 100, 800, 600), freeform: true);

        vm.Shutdown();

        Assert.Equal(original, vm.CurrentFrame);
        Assert.False(_overlay.IsVisible);
        var saved = Assert.IsType<FrameProfile>(_store.Settings.LastFrame);
        Assert.Equal(original.Width, saved.Width);
        Assert.Equal(original.Height, saved.Height);
        Assert.Equal(FrameAnchor.Center, saved.Anchor);
        Assert.True(vm.IsAspectLocked);
    }

    [Fact]
    public void Click_through_and_resize_mode_guard_direct_pointer_and_keyboard_changes()
    {
        var vm = Create();
        var original = vm.CurrentFrame;
        vm.IsClickThrough = true;
        Assert.False(vm.CanEditFrame);
        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(100, 100, 800, 600));
        vm.NudgeFrame(10, 10, resize: false);
        Assert.Equal(original, vm.CurrentFrame);

        vm.IsClickThrough = false;
        vm.ShowResizeModeCommand.Execute(null);
        Assert.False(vm.CanEditFrame);
        Assert.False(vm.DrawFrameCommand.CanExecute(null));
        Assert.False(vm.UseWindowBoundsCommand.CanExecute(null));
        vm.SetFrameFromInteraction(new Rectangle(100, 100, 800, 600));
        vm.NudgeFrame(10, 10, resize: false);
        Assert.Equal(original, vm.CurrentFrame);
        Assert.Empty(_windows.Resizes);
    }

    [Theory]
    [InlineData(-1, 0, 800, 600)]
    [InlineData(0, 0, 99, 600)]
    [InlineData(0, 0, 800, 99)]
    [InlineData(0, 0, 16385, 600)]
    [InlineData(0, 0, 800, 2161)]
    public void Invalid_interactive_frames_leave_the_frame_unchanged(int x, int y, int width, int height)
    {
        var vm = Create();
        var original = vm.CurrentFrame;
        vm.SetFrameFromInteraction(new Rectangle(x, y, width, height));
        Assert.Equal(original, vm.CurrentFrame);
    }

    [Fact]
    public void Keyboard_bounds_and_step_limits_are_enforced()
    {
        var vm = Create();
        vm.SetFrameFromInteraction(new Rectangle(0, 0, 100, 100), freeform: true);
        vm.NudgeFrame(-1, -1, resize: false);
        Assert.Equal(new Rectangle(0, 0, 100, 100), vm.CurrentFrame);
        vm.NudgeFrame(-10, -10, resize: true);
        Assert.Equal(new Rectangle(0, 0, 100, 100), vm.CurrentFrame);
        vm.NudgeFrame(11, 0, resize: false);
        Assert.Equal(new Rectangle(0, 0, 100, 100), vm.CurrentFrame);

        vm.SetFrameFromInteraction(new Rectangle(3740, 2060, 100, 100), freeform: true);
        vm.NudgeFrame(10, 10, resize: false);
        vm.NudgeFrame(10, 10, resize: true);
        Assert.Equal(new Rectangle(3740, 2060, 100, 100), vm.CurrentFrame);
    }

    [Fact]
    public void Interactive_resize_recomputes_nested_frame_bounds_in_the_live_overlay()
    {
        var vm = Create();
        vm.AddShortsFrameCommand.Execute(null);
        vm.PrimaryCommand.Execute(null);
        var oldNested = Assert.Single(_overlay.NestedFrames).Bounds;
        var parent = new Rectangle(200, 100, 1280, 720);

        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(parent);
        vm.FinishFrameInteraction();

        var expected = NestedFrameGeometry.Place(parent, vm.NestedFrames[0].Frame);
        Assert.Equal(expected, Assert.Single(_overlay.NestedFrames).Bounds);
        Assert.Equal(expected, Assert.Single(vm.Preview.NestedFrames).Bounds);
        Assert.NotEqual(oldNested.Size, expected.Size);
        Assert.True(parent.Contains(expected));
    }

    [Fact]
    public void New_preset_survives_a_later_canceled_drag_callback()
    {
        var vm = Create();
        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(100, 100, 800, 600), freeform: true);
        vm.Presets.Single(p => p.Key == "720p").SelectCommand.Execute(null);
        var newPreset = vm.CurrentFrame;

        vm.FinishFrameInteraction(cancel: true);

        Assert.Equal(newPreset, vm.CurrentFrame);
        Assert.Equal(new Size(1280, 720), vm.CurrentFrame.Size);
        Assert.True(vm.Presets.Single(p => p.Key == "720p").IsSelected);
    }

    [Fact]
    public void New_preset_survives_cancel_even_when_it_has_the_same_rectangle_as_the_last_drag()
    {
        var vm = Create();
        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(1280, 720, 1280, 720));
        vm.Presets.Single(p => p.Key == "720p").SelectCommand.Execute(null);

        vm.FinishFrameInteraction(cancel: true);

        Assert.Equal(new Rectangle(1280, 720, 1280, 720), vm.CurrentFrame);
        Assert.Equal("Centered", vm.AnchorName);
        Assert.True(vm.Presets.Single(p => p.Key == "720p").IsSelected);
    }

    [Fact]
    public void Display_selection_survives_a_later_canceled_drag_callback()
    {
        var vm = Create();
        vm.BeginFrameInteraction();
        vm.SetFrameFromInteraction(new Rectangle(100, 100, 800, 600), freeform: true);
        vm.Displays[1].SelectCommand.Execute(null);
        var newFrame = vm.CurrentFrame;

        vm.FinishFrameInteraction(cancel: true);

        Assert.Equal(newFrame, vm.CurrentFrame);
        Assert.Contains("Display 2", vm.DisplayLabel);
        Assert.True(FakeDisplays.Secondary.Bounds.Contains(vm.CurrentFrame));
    }

    [Fact]
    public async Task Using_a_window_measures_visible_bounds_without_resizing_the_app()
    {
        var vm = Create();
        _windowPicker.Result = FakeWindows.Terminal;

        await vm.UseWindowBoundsCommand.ExecuteAsync(null);

        Assert.Equal(_windows.Measure(FakeWindows.Terminal, WindowArea.VisibleFrame), vm.CurrentFrame);
        Assert.Equal(FakeWindows.Terminal.Bounds, _overlay.Frame);
        Assert.True(_overlay.IsVisible);
        Assert.False(vm.IsAspectLocked);
        Assert.Empty(_windows.Resizes);
        Assert.Null(_windows.TrackedWindow);
    }

    [Fact]
    public async Task Canceled_window_picking_preserves_the_visible_outline()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        var original = vm.CurrentFrame;
        await vm.UseWindowBoundsCommand.ExecuteAsync(null);

        Assert.Equal(original, vm.CurrentFrame);
        Assert.Equal(original, _overlay.Frame);
        Assert.True(_overlay.IsVisible);
        Assert.Empty(_windows.Resizes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_frame_selection_and_following_edits_preserve_capture_exclusion_failure_status(bool draw)
    {
        var vm = Create();
        vm.HideFromRecorders = true;
        _overlay.CaptureExclusionApplied = false;
        _regionPicker.Result = new Rectangle(100, 100, 800, 600);
        _windowPicker.Result = FakeWindows.Terminal;

        if (draw) await vm.DrawFrameCommand.ExecuteAsync(null);
        else await vm.UseWindowBoundsCommand.ExecuteAsync(null);

        Assert.True(vm.CaptureExclusionFailed);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Contains("Windows rejected", vm.StatusDetail);
        vm.NudgeFrame(1, 0, resize: false);
        Assert.Contains("Windows rejected", vm.StatusDetail);
    }

    [Fact]
    public async Task Window_crossing_displays_is_rejected_without_adopting_or_resizing_it()
    {
        var vm = Create();
        var original = vm.CurrentFrame;
        _windowPicker.Result = FakeWindows.Terminal with { Bounds = new Rectangle(3700, 100, 800, 600) };

        await vm.UseWindowBoundsCommand.ExecuteAsync(null);

        Assert.Equal(original, vm.CurrentFrame);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public async Task Draw_adopts_a_negative_origin_display_free_aspect_and_shows_the_outline()
    {
        var negative = new DisplayInfo(@"\\.\DISPLAY3", 3,
            new Rectangle(-1920, 0, 1920, 1080), new Rectangle(-1920, 0, 1920, 1040), 96, false);
        _displays.Displays.Add(negative);
        var vm = Create();
        _regionPicker.Result = new Rectangle(-1800, 50, 800, 600);

        await vm.DrawFrameCommand.ExecuteAsync(null);

        Assert.Equal(_regionPicker.Result, vm.CurrentFrame);
        Assert.Equal(negative.DeviceName, _overlay.Display?.DeviceName);
        Assert.Equal(_regionPicker.Result, _overlay.Frame);
        Assert.True(_overlay.IsVisible);
        Assert.False(vm.IsAspectLocked);
        Assert.Equal("Custom position", vm.AnchorName);
        Assert.Contains("800 × 600 Windows logical px at 100%", vm.LogicalSizeNote);
        Assert.Equal(3, _regionPicker.DisplayCount);
        Assert.Empty(_windows.Resizes);
    }

    [Theory]
    [InlineData(99, 600)]
    [InlineData(800, 99)]
    public async Task Too_small_drawing_is_rejected_and_restores_the_previous_visible_outline(int width, int height)
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        var original = vm.CurrentFrame;
        _regionPicker.Result = new Rectangle(100, 100, width, height);

        await vm.DrawFrameCommand.ExecuteAsync(null);

        Assert.Equal(original, vm.CurrentFrame);
        Assert.Equal(original, _overlay.Frame);
        Assert.True(_overlay.IsVisible);
        Assert.True(vm.IsAspectLocked);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
    }

    [Fact]
    public async Task Canceled_drawing_restores_the_previous_visible_outline_and_forwards_taskbar_clearance()
    {
        var vm = Create();
        vm.KeepClearOfTaskbar = true;
        vm.PrimaryCommand.Execute(null);
        var original = vm.CurrentFrame;

        await vm.DrawFrameCommand.ExecuteAsync(null);

        Assert.Equal(original, vm.CurrentFrame);
        Assert.Equal(original, _overlay.Frame);
        Assert.True(_overlay.IsVisible);
        Assert.True(_regionPicker.KeepClear);
        Assert.True(vm.IsAspectLocked);
        Assert.Contains("canceled", vm.StatusDetail);
    }

    [Fact]
    public async Task Display_change_cancels_a_pending_region_selection_and_preserves_the_previous_frame()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        var original = vm.CurrentFrame;
        _regionPicker.Pending = new TaskCompletionSource<Rectangle?>();
        Task drawing = vm.DrawFrameCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(_overlay.IsVisible);
        vm.IsClickThrough = true;
        vm.IsClickThrough = false;
        Assert.True(_overlay.Locked);

        _displays.RaiseChanged();
        Assert.True(_regionPicker.Token.IsCancellationRequested);
        _regionPicker.Pending.SetResult(new Rectangle(100, 100, 800, 600));
        await drawing;

        Assert.Equal(original, vm.CurrentFrame);
        Assert.Equal(original, _overlay.Frame);
        Assert.True(_overlay.IsVisible);
        Assert.False(vm.IsBusy);
    }

    private sealed class RegionPicker : IFrameRegionPicker
    {
        public Rectangle? Result { get; set; }
        public TaskCompletionSource<Rectangle?>? Pending { get; set; }
        public CancellationToken Token { get; private set; }
        public bool KeepClear { get; private set; }
        public int DisplayCount { get; private set; }

        public Task<Rectangle?> PickAsync(IReadOnlyList<DisplayInfo> displays, bool keepClearOfTaskbar, CancellationToken cancellation)
        {
            Token = cancellation;
            KeepClear = keepClearOfTaskbar;
            DisplayCount = displays.Count;
            return Pending?.Task ?? Task.FromResult(Result);
        }
    }
}
