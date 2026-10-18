using System.Collections.ObjectModel;
using System.Drawing;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

// Additional recording guides inside the main frame.
public sealed partial class MainViewModel
{
    private static readonly OutlineColor[] NestedColors =
        [OutlineColor.Amber, OutlineColor.Blue, OutlineColor.Red, OutlineColor.White, OutlineColor.Teal];

    public ObservableCollection<NestedFrameItem> NestedFrames { get; } = [];

    public bool HasNestedFrames => NestedFrames.Count > 0;

    public bool HasNoNestedFrames => NestedFrames.Count == 0;

    private NestedFrameItem? _nestedEditItem;
    private NestedFrame? _nestedEditOriginal;
    private NestedFrame? _nestedEditExpected;
    private Rectangle _nestedEditParent;
    private string? _selectedNestedFrameId;
    public string? SelectedNestedFrameId => _selectedNestedFrameId;

    public void SelectNestedFrame(string? id)
    {
        if (id is not null && !NestedFrames.Any(item => item.Frame.Id == id)) return;
        if (_selectedNestedFrameId == id) return;
        _selectedNestedFrameId = id;
        OnPropertyChanged(nameof(SelectedNestedFrameId));
        OnPropertyChanged(nameof(FrameEditorHint));
    }

    public string? HitTestNestedFrame(Point point, int tolerance)
    {
        var candidates = NestedFrames.Where(item => !item.Bounds.IsEmpty).Reverse().ToList();
        return candidates.FirstOrDefault(item => FrameManipulation.HitTest(item.Bounds, point, tolerance, false) != FrameHandle.None)?.Frame.Id
            ?? candidates.FirstOrDefault(item => item.Bounds.Contains(point))?.Frame.Id;
    }

    public bool BeginNestedFrameInteraction(string id)
    {
        if (!CanEditFrame) return false;
        if (_frameEdit is not null) FinishFrameInteraction(cancel: true);
        FinishNestedFrameInteraction(cancel: true);
        var item = NestedFrames.FirstOrDefault(item => item.Frame.Id == id);
        if (item is null || item.Bounds.IsEmpty) return false;
        _nestedEditItem = item;
        _nestedEditOriginal = _nestedEditExpected = item.Frame;
        _nestedEditParent = CurrentFrame;
        SelectNestedFrame(id);
        return true;
    }

    /// <summary>Moves only the active crop; the parent and requested output size stay unchanged.</summary>
    public void MoveNestedFrameFromInteraction(string id, Point location)
    {
        if (_nestedEditItem is not { } item || item.Frame.Id != id) return;
        if (!CanEditFrame || CurrentFrame != _nestedEditParent || item.Frame != _nestedEditExpected || !NestedFrames.Contains(item))
        { FinishNestedFrameInteraction(cancel: false); return; }
        var proposed = NestedFrameGeometry.PositionAt(CurrentFrame, item.Frame,
            location.X - CurrentFrame.X, location.Y - CurrentFrame.Y);
        if (NestedFrameGeometry.Place(CurrentFrame, proposed) == item.Bounds) return;
        _nestedEditExpected = proposed;
        item.RestoreFrame(_nestedEditExpected);
    }

    public void FinishNestedFrameInteraction(bool cancel = false)
    {
        var item = _nestedEditItem;
        var original = _nestedEditOriginal;
        var expected = _nestedEditExpected;
        _nestedEditItem = null; _nestedEditOriginal = _nestedEditExpected = null;
        if (item is null) return;
        if (cancel && original is not null && item.Frame == expected && NestedFrames.Contains(item)) item.RestoreFrame(original);
        if (!cancel) SetStatus($"Moved “{item.Name}” to {item.XPosition:0}, {item.YPosition:0} inside the main frame.");
    }

    public void NudgeSelectedNestedFrame(int dx, int dy)
    {
        if (!CanEditFrame || dx is < -10 or > 10 || dy is < -10 or > 10 ||
            NestedFrames.FirstOrDefault(item => item.Frame.Id == _selectedNestedFrameId) is not { } item) return;
        if (!BeginNestedFrameInteraction(item.Frame.Id)) return;
        MoveNestedFrameFromInteraction(item.Frame.Id, new Point(item.Bounds.X + dx, item.Bounds.Y + dy));
        FinishNestedFrameInteraction();
    }

    private void OnNestedFrameInteraction(object? sender, NestedFrameInteraction interaction)
    {
        switch (interaction.Kind)
        {
            case NestedFrameInteractionKind.Started: BeginNestedFrameInteraction(interaction.Id); break;
            case NestedFrameInteractionKind.Moved: MoveNestedFrameFromInteraction(interaction.Id, interaction.Bounds.Location); break;
            case NestedFrameInteractionKind.Completed when _nestedEditItem?.Frame.Id == interaction.Id: FinishNestedFrameInteraction(); break;
            case NestedFrameInteractionKind.Canceled when _nestedEditItem?.Frame.Id == interaction.Id: FinishNestedFrameInteraction(cancel: true); break;
        }
    }

    public IRelayCommand AddShortsFrameCommand { get; private set; } = null!;

    public IRelayCommand AddSquareFrameCommand { get; private set; } = null!;

    public IRelayCommand AddPortraitFrameCommand { get; private set; } = null!;

    public IRelayCommand AddCustomFrameCommand { get; private set; } = null!;

    private Task PreviewOutputsAsync() => _outputPreview is not null && IsOutlineMode && HasNestedFrames
        ? _outputPreview.ShowAsync(CurrentFrame, NestedFrames.Select(item => item.Frame).ToArray(), _lifetime.Token)
        : Task.CompletedTask;

    private void AddNestedFrame(string name, int aspectWidth, int aspectHeight)
    {
        if (NestedFrames.Count >= 16)
        {
            SetStatus("A setup supports up to 16 additional frames. Remove one before adding another.", warning: true);
            return;
        }
        var frame = new NestedFrame
        {
            Name = name,
            AspectWidth = aspectWidth,
            AspectHeight = aspectHeight,
            OutputWidth = aspectWidth == 9 && aspectHeight == 16 || aspectWidth == 1 || aspectWidth == 4 ? 1080 : 0,
            OutputHeight = aspectWidth == 9 && aspectHeight == 16 ? 1920 : aspectWidth == 1 ? 1080 : aspectWidth == 4 ? 1350 : 0,
            SafeMarginPercent = 5,
            Color = NestedColors[NestedFrames.Count % NestedColors.Length]
        };
        AddNestedFrameItem(frame);
        RefreshNestedFrames();
        SetStatus($"Added {name.ToLowerInvariant()} inside the main frame.");
    }

    private void AddNestedFrameItem(NestedFrame frame)
    {
        frame = frame with
        {
            AspectWidth = Math.Clamp(frame.AspectWidth, 1, 32),
            AspectHeight = Math.Clamp(frame.AspectHeight, 1, 32),
            ScalePercent = Math.Clamp(frame.ScalePercent, 10, 100),
            Anchor = Enum.IsDefined(frame.Anchor) ? frame.Anchor : FrameAnchor.Center,
            InsetPixels = Math.Clamp(frame.InsetPixels, 0, 1000),
            SafeMarginPercent = Math.Clamp(frame.SafeMarginPercent, 0, 40)
        };
        NestedFrameItem? item = null;
        item = new NestedFrameItem(frame, _ => RefreshNestedFrames(), () =>
        {
            NestedFrames.Remove(item!);
            RefreshNestedFrames();
            SetStatus($"Removed {frame.Name.ToLowerInvariant()}.");
        });
        NestedFrames.Add(item);
        NotifyNestedCount();
    }

    private void LoadNestedFrames(IEnumerable<NestedFrame>? frames)
    {
        FinishNestedFrameInteraction(cancel: true);
        SelectNestedFrame(null);
        NestedFrames.Clear();
        foreach (var frame in frames ?? [])
        {
            if (frame is not null) AddNestedFrameItem(frame);
        }
        UpdateNestedBounds(CurrentFrame);
        NotifyNestedCount();
        NotifyStage();
    }

    private IReadOnlyList<NestedFrameBounds> NestedBounds(Rectangle parent)
    {
        if (!IsOutlineMode || NestedFrames.Count == 0) return [];
        return [.. NestedFrames.Select(item =>
            new NestedFrameBounds(NestedFrameGeometry.Place(parent, item.Frame), item.Name, item.Frame.Color)
            { Id = item.Frame.Id, MovementArea = NestedFrameGeometry.AvailableArea(parent, item.Frame),
                SafeArea = NestedFrameGeometry.SafeArea(NestedFrameGeometry.Place(parent, item.Frame), item.Frame.SafeMarginPercent) })
            .Where(item => !item.Bounds.IsEmpty)];
    }

    private void RefreshNestedFrames()
    {
        if (_nestedEditItem is { } edited && (edited.Frame != _nestedEditExpected || !NestedFrames.Contains(edited)))
        { FinishNestedFrameInteraction(); _overlay.CancelFrameInteraction(); }
        if (_selectedNestedFrameId is not null && !NestedFrames.Any(item => item.Frame.Id == _selectedNestedFrameId)) SelectNestedFrame(null);
        var parent = CurrentFrame;
        UpdateNestedBounds(parent);

        if (_overlay.IsVisible && _display is not null && IsOutlineMode)
            _overlay.Show(parent, _display, _keepClear, NestedBounds(parent));
        NotifyStage();
        NotifyProfileHeader();
        NotifyNestedCount();
    }

    private void UpdateNestedBounds(Rectangle parent)
    {
        foreach (var item in NestedFrames)
            item.UpdateBounds(NestedFrameGeometry.Place(parent, item.Frame), parent);
    }

    private void NotifyNestedCount()
    {
        PreviewOutputsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNestedFrames));
        OnPropertyChanged(nameof(HasNoNestedFrames));
    }
}
