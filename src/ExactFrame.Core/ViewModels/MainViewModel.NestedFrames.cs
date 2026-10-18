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
            { SafeArea = NestedFrameGeometry.SafeArea(NestedFrameGeometry.Place(parent, item.Frame), item.Frame.SafeMarginPercent) })
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
            item.UpdateBounds(NestedFrameGeometry.Place(parent, item.Frame), parent);
    }

    private void NotifyNestedCount()
    {
        PreviewOutputsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNestedFrames));
        OnPropertyChanged(nameof(HasNoNestedFrames));
    }
}
