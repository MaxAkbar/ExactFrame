using System.Drawing;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Models;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly FakeDisplays _displays = new();
    private readonly FakeWindows _windows = new();
    private readonly FakePicker _picker = new();
    private readonly FakeOverlay _overlay = new();
    private readonly FakeHotkeys _hotkeys = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly MemoryStore _store = new();
    private MainViewModel? _vm;

    private MainViewModel Create(AppSettings? settings = null)
    {
        if (settings is not null) _store.Settings = settings;
        _vm = new MainViewModel(_displays, _windows, _picker, _overlay, _hotkeys, _clipboard, _dialogs, _store);
        _vm.Initialize();
        return _vm;
    }

    public void Dispose() => _vm?.Dispose();

    [Fact]
    public void Starts_centered_on_the_primary_display()
    {
        var vm = Create();

        Assert.Equal(new Rectangle(960, 540, 1920, 1080), vm.CurrentFrame);
        Assert.Equal("1920 × 1080", vm.SizeText);
        Assert.Equal("16:9 · Landscape · Display 1", vm.StageSubtitle);
        Assert.Equal("1280 × 720 logical px at 150% scaling", vm.LogicalSizeNote);
        Assert.True(vm.Presets.Single(p => p.Key == "1080p").IsSelected);
        Assert.True(vm.Anchors.Single(a => a.Anchor == Geometry.FrameAnchor.Center).IsSelected);
        Assert.True(vm.IsFit);
    }

    [Fact]
    public void Aspect_lock_keeps_the_ratio_while_typing_a_width()
    {
        var vm = Create();

        vm.WidthValue = 1280;

        Assert.Equal(720, vm.HeightValue);
        Assert.True(vm.Presets.Single(p => p.Key == "720p").IsSelected);
        Assert.Equal(new Rectangle(1280, 720, 1280, 720), vm.CurrentFrame);
    }

    [Fact]
    public void Typing_a_position_switches_to_custom_position()
    {
        var vm = Create();

        vm.XValue = 100;

        Assert.Equal("Custom position", vm.AnchorName);
        Assert.Equal(new Rectangle(100, 540, 1920, 1080), vm.CurrentFrame);
        Assert.All(vm.Anchors, a => Assert.False(a.IsSelected));
    }

    [Fact]
    public void Four_k_with_taskbar_clearance_does_not_fit_and_cannot_be_shown()
    {
        var vm = Create();
        vm.Presets.Single(p => p.Key == "4k").SelectCommand.Execute(null);
        vm.KeepClearOfTaskbar = true;

        Assert.False(vm.IsFit);
        Assert.Equal("Overlaps the taskbar by 72 px", vm.FitText);

        vm.PrimaryCommand.Execute(null);

        Assert.False(_overlay.IsVisible);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
    }

    [Fact]
    public void Primary_button_toggles_the_outline()
    {
        var vm = Create();

        vm.PrimaryCommand.Execute(null);
        Assert.True(_overlay.IsVisible);
        Assert.Equal(new Rectangle(960, 540, 1920, 1080), _overlay.Frame);
        Assert.Equal("Hide outline", vm.PrimaryLabel);
        Assert.Equal("Outline is live", vm.StatusTitle);

        vm.PrimaryCommand.Execute(null);
        Assert.False(_overlay.IsVisible);
        Assert.Equal("Show outline", vm.PrimaryLabel);
    }

    [Fact]
    public void Visible_outline_follows_anchor_changes()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);

        vm.Anchors.Single(a => a.Anchor == Geometry.FrameAnchor.TopLeft).SelectCommand.Execute(null);

        Assert.True(_overlay.IsVisible);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), _overlay.Frame);
    }

    [Fact]
    public void Dragging_the_outline_updates_the_position()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);

        _overlay.Drag(new Point(400, 300));

        Assert.Equal("400", vm.BoundsLeft);
        Assert.Equal(400, vm.XValue);
        Assert.Equal("Custom position", vm.AnchorName);
    }

    [Fact]
    public void Additional_frames_follow_the_main_frame_and_can_be_edited_or_removed()
    {
        var vm = Create();
        vm.AddShortsFrameCommand.Execute(null);
        vm.AddSquareFrameCommand.Execute(null);
        Assert.Equal(2, vm.NestedFrames.Count);
        Assert.Equal(new Rectangle(1623, 552, 594, 1056), vm.Preview.NestedFrames[0].Bounds);

        vm.PrimaryCommand.Execute(null);
        Assert.Equal(2, _overlay.NestedFrames.Count);
        _overlay.Drag(new Point(400, 300));
        Assert.Equal(new Rectangle(1063, 312, 594, 1056), _overlay.NestedFrames[0].Bounds);

        vm.WidthValue = 1280;
        var shorts = vm.NestedFrames[0];
        shorts.ScalePercent = 50;
        shorts.AnchorIndex = (int)Geometry.FrameAnchor.Right;
        Assert.Equal(new Size(198, 352), _overlay.NestedFrames[0].Bounds.Size);
        Assert.Equal(vm.CurrentFrame.Right - 8, _overlay.NestedFrames[0].Bounds.Right);

        shorts.RemoveCommand.Execute(null);
        Assert.Single(_overlay.NestedFrames);
        Assert.Single(vm.Preview.NestedFrames);
    }

    [Fact]
    public async Task Additional_frames_are_copied_and_restored_with_a_profile()
    {
        var vm = Create();
        vm.AddShortsFrameCommand.Execute(null);
        await vm.CopyBoundsCommand.ExecuteAsync(null);
        Assert.Contains("Main: X=960, Y=540, Width=1920, Height=1080", _clipboard.Text);
        Assert.Contains("Shorts 9:16: X=1623, Y=552, Width=594, Height=1056", _clipboard.Text);

        _dialogs.ProfileName = "Landscape and Shorts";
        await vm.SaveProfileCommand.ExecuteAsync(null);
        Assert.Equal("Profile", vm.ProfileEyebrow);
        Assert.Single(_store.Settings.Profiles[^1].NestedFrames);

        vm.NestedFrames[0].ScalePercent = 50;
        Assert.Equal("Profile · edited", vm.ProfileEyebrow);
        vm.Profiles.Single(p => p.Name == "Landscape and Shorts").SelectCommand.Execute(null);
        Assert.Equal("Profile", vm.ProfileEyebrow);
        Assert.Equal(100, vm.NestedFrames[0].ScalePercent);
    }

    [Fact]
    public void Style_changes_reach_the_overlay_and_are_saved()
    {
        var vm = Create();
        int saves = _store.SaveCount;

        vm.Swatches.Single(s => s.Color == OutlineColor.Amber).SelectCommand.Execute(null);
        vm.ShowThirds = true;

        Assert.Equal(OutlineColor.Amber, _overlay.Style!.Color);
        Assert.True(_overlay.Style.ShowThirds);
        Assert.True(vm.Preview.Style.ShowThirds);
        Assert.True(_store.SaveCount > saves);
        Assert.True(vm.Swatches.Single(s => s.Color == OutlineColor.Amber).IsSelected);
    }

    [Fact]
    public void Hotkeys_register_and_conflicts_are_reported()
    {
        _hotkeys.Taken.Add(HotkeyGesture.CtrlAlt(HotkeyGesture.VkF10));
        var vm = Create();

        Assert.False(vm.Hotkeys[0].HasConflict);
        Assert.True(vm.Hotkeys[2].HasConflict);
        Assert.True(vm.ShowPrimaryHotkey);
        Assert.Equal(["Ctrl", "Alt", "1"], vm.Profiles[0].Keys);
        Assert.True(vm.Profiles[0].HotkeyRegistered);
    }

    [Fact]
    public async Task Changing_a_conflicting_hotkey_registers_the_new_one()
    {
        _hotkeys.Taken.Add(HotkeyGesture.CtrlAlt(HotkeyGesture.VkF10));
        var vm = Create();
        var replacement = HotkeyGesture.CtrlAlt(0x50);
        _dialogs.RecordedHotkey = replacement;

        await vm.Hotkeys[2].ChangeCommand.ExecuteAsync(null);

        Assert.False(vm.Hotkeys[2].HasConflict);
        Assert.Equal(replacement, _store.Settings.NextProfileHotkey);
    }

    [Fact]
    public void Show_hide_hotkey_toggles_the_outline()
    {
        Create();

        _hotkeys.Press(1);
        Assert.True(_overlay.IsVisible);

        _hotkeys.Press(1);
        Assert.False(_overlay.IsVisible);
    }

    [Fact]
    public void Applying_a_profile_sets_size_and_anchor_and_edits_are_flagged()
    {
        var vm = Create();

        vm.Profiles.Single(p => p.Name == "Course 1440p").SelectCommand.Execute(null);

        Assert.Equal(new Rectangle(0, 0, 2560, 1440), vm.CurrentFrame);
        Assert.Equal("Course 1440p", vm.ActiveProfileName);
        Assert.Equal("Profile", vm.ProfileEyebrow);

        vm.WidthValue = 1280;
        Assert.Equal("Profile · edited", vm.ProfileEyebrow);
    }

    [Fact]
    public async Task Saving_a_profile_adds_it_with_a_hotkey()
    {
        var vm = Create();
        vm.WidthValue = 1280;
        _dialogs.ProfileName = "Tutorial 720p";

        await vm.SaveProfileCommand.ExecuteAsync(null);

        Assert.Equal(4, vm.Profiles.Count);
        var saved = vm.Profiles[^1];
        Assert.Equal("Tutorial 720p", saved.Name);
        Assert.True(saved.IsSelected);
        Assert.Equal(["Ctrl", "Alt", "4"], saved.Keys);
        Assert.Equal(1280, _store.Settings.Profiles[^1].Width);
        Assert.Equal("Profile", vm.ProfileEyebrow);
    }

    [Fact]
    public async Task Deleting_a_profile_removes_it()
    {
        var vm = Create();

        await vm.Profiles[1].DeleteCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Profiles.Count);
        Assert.DoesNotContain(_store.Settings.Profiles, p => p.Name == "Shorts, vertical");
    }

    [Fact]
    public async Task Resizing_shows_the_measured_window_and_allows_restore()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        Assert.False(vm.PrimaryCommand.CanExecute(null));

        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        Assert.True(vm.PrimaryCommand.CanExecute(null));

        await vm.PrimaryCommand.ExecuteAsync(null);

        var (window, desired, area) = Assert.Single(_windows.Resizes);
        Assert.Equal(FakeWindows.Studio, window);
        Assert.Equal(new Rectangle(960, 540, 1920, 1080), desired);
        Assert.Equal(WindowArea.Client, area);
        Assert.True(_overlay.IsVisible);
        Assert.True(vm.CanRestore);
        Assert.Equal("Applied", vm.ChipText);

        await vm.RestoreCommand.ExecuteAsync(null);
        Assert.False(_overlay.IsVisible);
        Assert.False(vm.CanRestore);
    }

    [Fact]
    public async Task Manual_resize_remembers_the_accepted_size_and_measurement_area()
    {
        _windows.AcceptedSize = new Rectangle(0, 0, 1600, 900);
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        vm.MeasureOptions.Single(o => o.Key == nameof(WindowArea.VisibleFrame)).SelectCommand.Execute(null);

        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(new RememberedAppSize("devenv", 1600, 900, WindowArea.VisibleFrame),
            Assert.Single(_store.Settings.RememberedAppSizes));
        Assert.True(vm.HasRememberedAppSize);
        Assert.Contains("1600 × 900", vm.RememberedAppSizeText);
        Assert.True(_store.SaveCount > 0);
    }

    [Fact]
    public async Task Web_page_mode_resizes_and_remembers_the_page_viewport()
    {
        _windows.Windows.Add(FakeWindows.Browser);
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Browser).SelectCommand.Execute(null);
        vm.MeasureOptions.Single(o => o.Key == nameof(WindowArea.PageContent)).SelectCommand.Execute(null);

        Assert.Contains("web page", vm.MeasureNote, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Page", vm.Preview.CaptureLabel);
        await vm.PrimaryCommand.ExecuteAsync(null);

        var resize = Assert.Single(_windows.Resizes);
        Assert.Equal(new Rectangle(960, 540, 1920, 1080), resize.Desired);
        Assert.Equal(WindowArea.PageContent, resize.Area);
        Assert.Equal(WindowArea.PageContent, _windows.TrackedArea);
        Assert.Equal(new RememberedAppSize("msedge", 1920, 1080, WindowArea.PageContent),
            Assert.Single(_store.Settings.RememberedAppSizes));
    }

    [Fact]
    public async Task Web_page_mode_rejects_an_app_without_a_measurable_viewport()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        vm.MeasureOptions.Single(o => o.Key == nameof(WindowArea.PageContent)).SelectCommand.Execute(null);

        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Empty(_windows.Resizes);
        Assert.False(vm.CanRestore);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Contains("could not be measured", vm.StatusDetail);
    }

    [Fact]
    public async Task Remembered_web_page_size_is_applied_with_the_page_mode()
    {
        _windows.Windows.Add(FakeWindows.Browser);
        var settings = new AppSettings
        {
            RememberedAppSizes = [new RememberedAppSize("msedge", 1280, 720, WindowArea.PageContent)]
        };
        var vm = Create(settings);
        vm.ShowResizeModeCommand.Execute(null);

        await ((IAsyncRelayCommand)vm.Windows.Single(w => w.Window == FakeWindows.Browser).SelectCommand).ExecuteAsync(null);

        var resize = Assert.Single(_windows.Resizes);
        Assert.Equal(new Rectangle(300, 320, 1280, 720), resize.Desired);
        Assert.Equal(WindowArea.PageContent, resize.Area);
        Assert.True(vm.MeasureOptions.Single(o => o.Key == nameof(WindowArea.PageContent)).IsSelected);
        Assert.Contains("web page", vm.RememberedAppSizeText);
    }

    [Fact]
    public async Task Explicit_web_page_choice_is_kept_when_selecting_an_app_with_a_saved_client_size()
    {
        _windows.Windows.Add(FakeWindows.Browser);
        var settings = new AppSettings
        {
            RememberedAppSizes = [new RememberedAppSize("msedge", 1280, 720, WindowArea.Client)]
        };
        var vm = Create(settings);
        vm.ShowResizeModeCommand.Execute(null);
        vm.MeasureOptions.Single(o => o.Key == nameof(WindowArea.PageContent)).SelectCommand.Execute(null);

        await ((IAsyncRelayCommand)vm.Windows.Single(w => w.Window == FakeWindows.Browser).SelectCommand).ExecuteAsync(null);

        Assert.Equal(WindowArea.PageContent, Assert.Single(_windows.Resizes).Area);
        Assert.True(vm.MeasureOptions.Single(o => o.Key == nameof(WindowArea.PageContent)).IsSelected);
        Assert.Equal("Applied", vm.ChipText);
        Assert.Contains("web page selected for this resize", vm.RememberedAppSizeText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Changing_measurement_area_after_resize_returns_the_stage_to_preview()
    {
        var settings = new AppSettings
        {
            RememberedAppSizes = [new RememberedAppSize("devenv", 1280, 720, WindowArea.Client)]
        };
        var vm = Create(settings);
        vm.ShowResizeModeCommand.Execute(null);
        await ((IAsyncRelayCommand)vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand).ExecuteAsync(null);

        vm.MeasureOptions.Single(o => o.Key == nameof(WindowArea.VisibleFrame)).SelectCommand.Execute(null);

        Assert.True(vm.CanRestore);
        Assert.False(_overlay.IsVisible);
        Assert.Equal("Preview", vm.ChipText);
        Assert.Equal("Ready to resize", vm.StatusTitle);
    }

    [Fact]
    public async Task Selecting_a_remembered_app_applies_its_saved_size_automatically()
    {
        var settings = new AppSettings
        {
            RememberedAppSizes = [new RememberedAppSize("DEVENV", 1280, 720, WindowArea.VisibleFrame)]
        };
        var vm = Create(settings);
        vm.ShowResizeModeCommand.Execute(null);

        await ((IAsyncRelayCommand)vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand).ExecuteAsync(null);

        var resize = Assert.Single(_windows.Resizes);
        Assert.Equal(new Rectangle(240, 180, 1280, 720), resize.Desired);
        Assert.Equal(WindowArea.VisibleFrame, resize.Area);
        Assert.True(_overlay.IsVisible);
        Assert.Equal(new Rectangle(240, 180, 1280, 720), _overlay.Frame);
        Assert.Single(_store.Settings.RememberedAppSizes);
    }

    [Fact]
    public async Task Manual_override_updates_only_the_selected_apps_remembered_size()
    {
        var settings = new AppSettings
        {
            RememberedAppSizes =
            [
                new RememberedAppSize("devenv", 1280, 720, WindowArea.Client),
                new RememberedAppSize("WindowsTerminal", 1024, 768, WindowArea.VisibleFrame)
            ]
        };
        var vm = Create(settings);
        vm.ShowResizeModeCommand.Execute(null);
        await ((IAsyncRelayCommand)vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand).ExecuteAsync(null);

        vm.WidthValue = 1600;
        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(2, _store.Settings.RememberedAppSizes.Count);
        Assert.Equal(new RememberedAppSize("devenv", 1600, 900, WindowArea.Client),
            _store.Settings.RememberedAppSizes.Single(s => s.ProcessName == "devenv"));
        Assert.Equal(new RememberedAppSize("WindowsTerminal", 1024, 768, WindowArea.VisibleFrame),
            _store.Settings.RememberedAppSizes.Single(s => s.ProcessName == "WindowsTerminal"));
    }

    [Fact]
    public async Task Picking_a_remembered_app_applies_its_saved_size()
    {
        var settings = new AppSettings
        {
            RememberedAppSizes = [new RememberedAppSize("devenv", 1280, 720, WindowArea.Client)]
        };
        _picker.Result = FakeWindows.Studio;
        var vm = Create(settings);
        vm.ShowResizeModeCommand.Execute(null);

        await vm.PickWindowCommand.ExecuteAsync(null);

        var resize = Assert.Single(_windows.Resizes);
        Assert.Equal(new Rectangle(240, 228, 1280, 720), resize.Desired);
        Assert.Equal(WindowArea.Client, resize.Area);
    }

    [Fact]
    public async Task Remembered_size_that_does_not_fit_is_loaded_but_not_applied()
    {
        var settings = new AppSettings
        {
            RememberedAppSizes = [new RememberedAppSize("devenv", 5000, 3000, WindowArea.Client)]
        };
        var vm = Create(settings);
        vm.ShowResizeModeCommand.Execute(null);

        await ((IAsyncRelayCommand)vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand).ExecuteAsync(null);

        Assert.Empty(_windows.Resizes);
        Assert.Equal(5000, vm.WidthValue);
        Assert.Equal(3000, vm.HeightValue);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Contains("does not fit", vm.StatusDetail);
    }

    [Fact]
    public async Task Forgetting_an_app_size_prevents_the_next_automatic_resize()
    {
        var settings = new AppSettings
        {
            RememberedAppSizes = [new RememberedAppSize("devenv", 1280, 720, WindowArea.Client)]
        };
        var vm = Create(settings);
        vm.ShowResizeModeCommand.Execute(null);
        await ((IAsyncRelayCommand)vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand).ExecuteAsync(null);
        Assert.Single(_windows.Resizes);

        vm.ForgetAppSizeCommand.Execute(null);
        Assert.False(vm.HasRememberedAppSize);
        Assert.Empty(_store.Settings.RememberedAppSizes);

        vm.Windows.Single(w => w.Window == FakeWindows.Terminal).SelectCommand.Execute(null);
        vm.Windows.Single(w => w.Window.IsSameWindow(FakeWindows.Studio)).SelectCommand.Execute(null);
        Assert.Single(_windows.Resizes);
    }

    [Fact]
    public async Task Resized_outline_follows_window_moves_and_external_size_changes()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.True(_windows.TrackedWindow!.IsSameWindow(FakeWindows.Studio));
        Assert.Equal(WindowArea.Client, _windows.TrackedArea);
        Assert.True(_overlay.Locked);
        _overlay.Drag(new Point(50, 50));
        Assert.Equal(new Rectangle(960, 540, 1920, 1080), _overlay.Frame);
        _windows.RaiseTracked(WindowObservationKind.Visible, new Rectangle(500, 600, 1920, 1080));

        Assert.Equal(new Rectangle(500, 600, 1920, 1080), _overlay.Frame);
        Assert.Equal("Custom position", vm.AnchorName);
        Assert.Equal("500, 600, 1920, 1080", vm.BoundsText);

        _windows.RaiseTracked(WindowObservationKind.Visible, new Rectangle(500, 600, 1280, 720));
        Assert.Equal(new Rectangle(500, 600, 1280, 720), _overlay.Frame);
        Assert.Equal("1280 × 720", vm.SizeText);
        Assert.Contains("currently 1280 × 720", vm.StageSubtitle);
    }

    [Fact]
    public async Task Tracking_hides_for_a_minimized_window_and_returns_on_restore()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        await vm.PrimaryCommand.ExecuteAsync(null);

        _windows.RaiseTracked(WindowObservationKind.Unavailable);
        Assert.False(_overlay.IsVisible);
        Assert.NotNull(_windows.TrackedWindow);

        _windows.RaiseTracked(WindowObservationKind.Visible, new Rectangle(400, 300, 1920, 1080));
        Assert.True(_overlay.IsVisible);
        Assert.Equal(new Rectangle(400, 300, 1920, 1080), _overlay.Frame);

        await vm.RestoreCommand.ExecuteAsync(null);
        Assert.False(_overlay.IsVisible);
        Assert.Null(_windows.TrackedWindow);
    }

    [Fact]
    public async Task Moving_to_another_display_updates_the_outline_display()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        await vm.PrimaryCommand.ExecuteAsync(null);

        _windows.RaiseTracked(WindowObservationKind.Visible, new Rectangle(4200, 100, 1920, 1080));

        Assert.Equal(FakeDisplays.Secondary.DeviceName, _overlay.Display?.DeviceName);
        Assert.Contains("Display 2", vm.DisplayLabel);
        Assert.Equal(4200, vm.XValue);
    }

    [Fact]
    public async Task Hiding_the_outline_stops_following_the_app()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        await vm.PrimaryCommand.ExecuteAsync(null);

        vm.HideOutlineCommand.Execute(null);
        _windows.RaiseTracked(WindowObservationKind.Visible, new Rectangle(400, 300, 1920, 1080));

        Assert.Null(_windows.TrackedWindow);
        Assert.False(_overlay.IsVisible);
        Assert.False(_overlay.Locked);
    }

    [Fact]
    public async Task Choosing_another_app_stops_following_the_previous_one()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        await vm.PrimaryCommand.ExecuteAsync(null);

        vm.Windows.Single(w => w.Window == FakeWindows.Terminal).SelectCommand.Execute(null);

        Assert.Null(_windows.TrackedWindow);
        Assert.False(_overlay.IsVisible);
        Assert.False(_overlay.Locked);
    }

    [Fact]
    public async Task Editing_the_requested_frame_while_the_app_is_hidden_stops_following()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        await vm.PrimaryCommand.ExecuteAsync(null);
        _windows.RaiseTracked(WindowObservationKind.Unavailable);

        vm.WidthValue = 1280;
        _windows.RaiseTracked(WindowObservationKind.Visible, new Rectangle(400, 300, 1280, 720));

        Assert.Null(_windows.TrackedWindow);
        Assert.False(_overlay.IsVisible);
    }

    [Fact]
    public async Task Closing_the_tracked_app_clears_its_outline_and_target()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Studio).SelectCommand.Execute(null);
        await vm.PrimaryCommand.ExecuteAsync(null);

        _windows.Windows.RemoveAll(w => w.IsSameWindow(FakeWindows.Studio));
        _windows.RaiseTracked(WindowObservationKind.Closed);

        Assert.Null(_windows.TrackedWindow);
        Assert.False(_overlay.IsVisible);
        Assert.False(vm.HasTargetWindow);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
    }

    [Fact]
    public async Task Resize_reports_when_the_app_rejects_the_size()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows[0].SelectCommand.Execute(null);
        _windows.AcceptedSize = new Rectangle(0, 0, 1600, 900);

        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Equal("1600 × 900", vm.SizeText);
    }

    [Fact]
    public void Keep_position_uses_the_window_capture_rectangle()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(w => w.Window == FakeWindows.Terminal).SelectCommand.Execute(null);

        vm.PlacementOptions.Single(o => o.Key == "keep").SelectCommand.Execute(null);

        Assert.Equal(1200, vm.XValue);
        Assert.Equal(688, vm.YValue);
        Assert.True(vm.PlacementOptions.Single(o => o.Key == "keep").IsSelected);
    }

    [Fact]
    public async Task Picking_a_window_selects_it()
    {
        var vm = Create();
        vm.ShowResizeModeCommand.Execute(null);
        _picker.Result = FakeWindows.Terminal;

        await vm.PickWindowCommand.ExecuteAsync(null);

        Assert.True(vm.Windows.Single(w => w.Window == FakeWindows.Terminal).IsSelected);
        Assert.True(vm.HasTargetWindow);
    }

    [Fact]
    public void Switching_display_recenters_on_it()
    {
        var vm = Create();

        vm.Displays[1].SelectCommand.Execute(null);

        Assert.Equal(new Rectangle(3840 + 320, 180, 1920, 1080), vm.CurrentFrame);
        Assert.Equal("1920 × 1080 logical px at 100% scaling", vm.LogicalSizeNote);
    }

    [Fact]
    public async Task Copy_bounds_uses_recorder_friendly_text()
    {
        var vm = Create();

        await vm.CopyBoundsCommand.ExecuteAsync(null);

        Assert.Equal("X=960, Y=540, Width=1920, Height=1080", _clipboard.Text);
        Assert.False(vm.IsCopied);
    }

    [Fact]
    public void Last_session_is_restored_on_startup()
    {
        var settings = new AppSettings
        {
            LastFrame = new FrameProfile { Width = 1080, Height = 1920, Anchor = Geometry.FrameAnchor.Right, DisplayDeviceName = @"\\.\DISPLAY1" }
        };

        var vm = Create(settings);

        Assert.Equal(new Rectangle(3840 - 1080, 120, 1080, 1920), vm.CurrentFrame);
        Assert.True(vm.Presets.Single(p => p.Key == "vertical").IsSelected);

        vm.Shutdown();
        Assert.Equal(1080, _store.Settings.LastFrame!.Width);
    }
}

public sealed class MinimizedWindowTests : IDisposable
{
    private readonly FakeWindows _windows = new();
    private readonly FakeOverlay _overlay = new();
    private MainViewModel? _vm;

    private MainViewModel CreateInResizeMode()
    {
        _vm = new MainViewModel(new FakeDisplays(), _windows, new FakePicker(), _overlay, new FakeHotkeys(),
            new FakeClipboard(), new FakeDialogs(), new MemoryStore());
        _vm.Initialize();
        _vm.ShowResizeModeCommand.Execute(null);
        return _vm;
    }

    private WindowItem Notes(MainViewModel vm) => vm.Windows.Single(w => w.Window.IsSameWindow(FakeWindows.Notes));

    public void Dispose() => _vm?.Dispose();

    [Fact]
    public void Minimized_windows_are_labelled_in_the_list_and_on_the_stage()
    {
        var vm = CreateInResizeMode();

        Notes(vm).SelectCommand.Execute(null);

        Assert.Equal("notepad.exe · minimized · 1200 × 800", Notes(vm).Meta);
        Assert.Equal("notepad · minimized, it will be restored", vm.StageSubtitle);
        Assert.Null(vm.Preview.Ghost);
    }

    [Fact]
    public async Task Resizing_a_minimized_window_restores_it_into_the_frame()
    {
        var vm = CreateInResizeMode();
        Notes(vm).SelectCommand.Execute(null);

        await vm.PrimaryCommand.ExecuteAsync(null);

        var (window, desired, _) = Assert.Single(_windows.Resizes);
        Assert.True(window.IsSameWindow(FakeWindows.Notes));
        Assert.Equal(new Rectangle(960, 540, 1920, 1080), desired);
        Assert.True(_overlay.IsVisible);
        Assert.StartsWith("Restored notepad to a normal window first.", vm.StatusDetail);
        Assert.False(Notes(vm).Window.IsMinimized);
        Assert.True(Notes(vm).IsSelected);
        Assert.Equal("notepad · currently 1920 × 1080", vm.StageSubtitle);
        Assert.True(vm.CanRestore);
    }

    [Fact]
    public void Keep_position_centers_a_minimized_window_instead()
    {
        var vm = CreateInResizeMode();
        Notes(vm).SelectCommand.Execute(null);

        vm.PlacementOptions.Single(o => o.Key == "keep").SelectCommand.Execute(null);

        Assert.Equal("Centered", vm.AnchorName);
        Assert.True(vm.PlacementOptions.Single(o => o.Key == "center").IsSelected);
        Assert.Contains("minimized, so it has no position to keep", vm.StatusDetail);
        Assert.NotEqual(StatusKind.Warning, vm.StatusKind);
    }

    [Fact]
    public void Switching_to_a_minimized_window_while_keeping_position_centers_it()
    {
        var vm = CreateInResizeMode();
        vm.Windows.Single(w => w.Window.IsSameWindow(FakeWindows.Terminal)).SelectCommand.Execute(null);
        vm.PlacementOptions.Single(o => o.Key == "keep").SelectCommand.Execute(null);
        Assert.Equal("Custom position", vm.AnchorName);

        Notes(vm).SelectCommand.Execute(null);

        Assert.Equal("Centered", vm.AnchorName);
        Assert.True(vm.IsFit);
    }
}
