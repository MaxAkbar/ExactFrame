using System.Drawing;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class SubframeInteractionTests : IDisposable
{
    private readonly FakeOverlay _overlay = new();
    private readonly FakeWindows _windows = new();
    private readonly MainViewModel _vm;
    public SubframeInteractionTests()
    {
        _vm = new MainViewModel(new FakeDisplays(), _windows, new FakePicker(), _overlay, new FakeHotkeys(),
            new FakeClipboard(), new FakeDialogs(), new MemoryStore(), new FakeStartup());
        _vm.Initialize(); _vm.AddShortsFrameCommand.Execute(null); _vm.AddSquareFrameCommand.Execute(null);
    }
    public void Dispose() => _vm.Dispose();

    [Fact]
    public void Moving_a_subframe_changes_only_its_position_and_clamps_to_the_parent_inset()
    {
        var parent = _vm.CurrentFrame; var item = _vm.NestedFrames[0]; var other = _vm.NestedFrames[1].Frame;
        var original = item.Frame; var size = item.Bounds.Size;
        Assert.True(_vm.BeginNestedFrameInteraction(original.Id));
        _vm.MoveNestedFrameFromInteraction(original.Id, new Point(parent.Left - 10000, parent.Bottom + 10000));
        _vm.FinishNestedFrameInteraction();
        Assert.Equal(parent, _vm.CurrentFrame); Assert.Equal(size, item.Bounds.Size);
        Assert.Equal(parent.Left + original.InsetPixels, item.Bounds.Left);
        Assert.Equal(parent.Bottom - original.InsetPixels, item.Bounds.Bottom);
        Assert.Equal(original.OutputWidth, item.Frame.OutputWidth); Assert.Equal(original.OutputHeight, item.Frame.OutputHeight);
        Assert.Equal(other, _vm.NestedFrames[1].Frame); Assert.Empty(_windows.Resizes);
        Assert.True(item.HasCustomPosition);
    }

    [Fact]
    public void Position_selection_binding_echo_does_not_reset_a_drag_before_bounds_refresh()
    {
        var item = _vm.NestedFrames[0];
        string? displayedDimensions = null;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == string.Empty) item.AnchorIndex = item.AnchorIndex;
            if (e.PropertyName is nameof(NestedFrameItem.Dimensions) or "") displayedDimensions = item.Dimensions;
        };
        var original = item.Bounds;
        _vm.BeginNestedFrameInteraction(item.Frame.Id);
        _vm.MoveNestedFrameFromInteraction(item.Frame.Id, new Point(original.X + 100, original.Y));
        _vm.MoveNestedFrameFromInteraction(item.Frame.Id, new Point(original.X + 150, original.Y));
        _vm.FinishNestedFrameInteraction();
        Assert.Equal(original.X + 150, item.Bounds.X);
        Assert.Equal(original.Y, item.Bounds.Y);
        Assert.Equal(item.Dimensions, displayedDimensions);
    }

    [Fact]
    public void Click_without_motion_does_not_convert_an_anchor_or_edit_the_setup()
    {
        var item = _vm.NestedFrames[0]; var original = item.Frame;
        _vm.BeginNestedFrameInteraction(original.Id);
        _vm.MoveNestedFrameFromInteraction(original.Id, item.Bounds.Location);
        _vm.FinishNestedFrameInteraction();
        Assert.Equal(original, item.Frame); Assert.False(item.HasCustomPosition);
    }

    [Fact]
    public void Cancel_restores_the_subframe_and_keyboard_moves_are_one_or_ten_pixels()
    {
        var item = _vm.NestedFrames[0]; var original = item.Frame;
        _vm.BeginNestedFrameInteraction(original.Id);
        _vm.MoveNestedFrameFromInteraction(original.Id, new Point(item.Bounds.X + 100, item.Bounds.Y));
        _vm.FinishNestedFrameInteraction(cancel: true);
        Assert.Equal(original, item.Frame);
        int x = item.Bounds.X;
        _vm.NudgeSelectedNestedFrame(1, 0); Assert.Equal(x + 1, item.Bounds.X);
        _vm.NudgeSelectedNestedFrame(10, 0); Assert.Equal(x + 11, item.Bounds.X);
    }

    [Fact]
    public void New_numeric_subframe_edit_wins_over_canceling_an_old_drag()
    {
        var item = _vm.NestedFrames[0];
        _vm.BeginNestedFrameInteraction(item.Frame.Id);
        _vm.MoveNestedFrameFromInteraction(item.Frame.Id, new Point(item.Bounds.X + 100, item.Bounds.Y));
        item.OutputWidth = 1440;
        var edited = item.Frame;
        _vm.FinishNestedFrameInteraction(cancel: true);
        Assert.Equal(edited, item.Frame); Assert.Equal(1440, item.OutputWidth);
    }

    [Fact]
    public void Parent_edit_cancels_child_drag_and_stale_moves_cannot_modify_the_new_setup()
    {
        var item = _vm.NestedFrames[0]; var original = item.Frame; string id = original.Id;
        _vm.BeginNestedFrameInteraction(id);
        _vm.MoveNestedFrameFromInteraction(id, new Point(item.Bounds.X + 100, item.Bounds.Y));
        _vm.WidthValue = 1280;
        var parent = _vm.CurrentFrame; var child = item.Frame;
        Assert.Equal(original, child);
        _vm.MoveNestedFrameFromInteraction(id, new Point(0, 0));
        Assert.Equal(child, item.Frame); Assert.Equal(parent, _vm.CurrentFrame);
    }

    [Fact]
    public void Click_through_cancels_a_move_and_blocks_new_interactions()
    {
        var item = _vm.NestedFrames[0]; var original = item.Frame;
        _vm.BeginNestedFrameInteraction(original.Id);
        _vm.MoveNestedFrameFromInteraction(original.Id, new Point(item.Bounds.X + 100, item.Bounds.Y));
        _vm.IsClickThrough = true;
        Assert.Equal(original, item.Frame); Assert.False(_vm.BeginNestedFrameInteraction(original.Id));
    }

    [Fact]
    public void Native_events_use_the_same_move_and_cancel_path_and_ignore_other_frame_completions()
    {
        var item = _vm.NestedFrames[0]; var original = item.Frame;
        _overlay.RaiseNested(new(original.Id, Rectangle.Empty, NestedFrameInteractionKind.Started));
        _overlay.RaiseNested(new(original.Id, new Rectangle(item.Bounds.X + 100, item.Bounds.Y, item.Bounds.Width, item.Bounds.Height), NestedFrameInteractionKind.Moved));
        Assert.NotEqual(original, item.Frame);
        _overlay.RaiseNested(new(_vm.NestedFrames[1].Frame.Id, Rectangle.Empty, NestedFrameInteractionKind.Completed));
        _overlay.RaiseNested(new(original.Id, Rectangle.Empty, NestedFrameInteractionKind.Canceled));
        Assert.Equal(original, item.Frame);
    }

    [Fact]
    public void Preview_and_live_regions_carry_the_same_identity_and_movement_limits()
    {
        _vm.PrimaryCommand.Execute(null);
        var child = _vm.NestedFrames[0];
        var region = _vm.Preview.NestedFrames.First(frame => frame.Id == child.Frame.Id);
        Assert.Equal(NestedFrameGeometry.AvailableArea(_vm.CurrentFrame, child.Frame), region.MovementArea);
        Assert.Equal(region, _overlay.NestedFrames.First(frame => frame.Id == child.Frame.Id));
    }
}
