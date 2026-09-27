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

    public IRelayCommand AddShortsFrameCommand { get; private set; } = null!;

    public IRelayCommand AddSquareFrameCommand { get; private set; } = null!;

    public IRelayCommand AddPortraitFrameCommand { get; private set; } = null!;

    public IRelayCommand AddCustomFrameCommand { get; private set; } = null!;

    private void AddNestedFrame(string name, int aspectWidth, int aspectHeight)
    {
        var frame = new NestedFrame
        {
            Name = name,
            AspectWidth = aspectWidth,
            AspectHeight = aspectHeight,
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
            Anchor = Enum.IsDefined(frame.Anchor) ? frame.Anchor : FrameAnchor.Center
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
            new NestedFrameBounds(NestedFrameGeometry.Place(parent, item.Frame), item.Name, item.Frame.Color))
            .Where(item => !item.Bounds.IsEmpty)];
    }

    private void RefreshNestedFrames()
    {
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
            item.UpdateBounds(NestedFrameGeometry.Place(parent, item.Frame));
    }

    private void NotifyNestedCount()
    {
        OnPropertyChanged(nameof(HasNestedFrames));
        OnPropertyChanged(nameof(HasNoNestedFrames));
    }
}
