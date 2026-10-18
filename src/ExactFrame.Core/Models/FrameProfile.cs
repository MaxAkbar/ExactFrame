using ExactFrame.Core.Geometry;

namespace ExactFrame.Core.Models;

public enum FrameMode
{
    Outline,
    Resize
}

/// <summary>A saved setup with an optional stable numbered shortcut and appearance.</summary>
public sealed record FrameProfile
{
    private DisplayInfo? _displaySnapshot;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "Profile";

    /// <summary>A stable shortcut number from 1 to 9, or 0 for no numbered shortcut.</summary>
    public int HotkeySlot { get; init; }

    public FrameMode Mode { get; init; } = FrameMode.Outline;

    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    public bool IsAspectLocked { get; init; } = true;

    /// <summary>Optional profile appearance; null uses the application's default appearance.</summary>
    public OutlineStyle? Appearance { get; init; }

    /// <summary>Additional recording guides that move and scale with the main frame.</summary>
    public List<NestedFrame> NestedFrames { get; init; } = [];

    /// <summary>Anchor inside the display, or <c>null</c> for the exact <see cref="X"/>/<see cref="Y"/> position.</summary>
    public FrameAnchor? Anchor { get; init; } = FrameAnchor.Center;

    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>Windows device name, such as <c>\\.\DISPLAY1</c>. <c>null</c> means the primary display.</summary>
    public string? DisplayDeviceName { get; init; }

    /// <summary>Display geometry for remapping a portable frame; native monitor interface paths are never retained.</summary>
    public DisplayInfo? DisplaySnapshot
    {
        get => _displaySnapshot;
        init => _displaySnapshot = value is null ? null : value with { MonitorDevicePath = null };
    }

    public bool KeepClearOfTaskbar { get; init; }

    public WindowArea Area { get; init; } = WindowArea.Client;

    /// <summary>Process name to select in Resize window mode, such as <c>devenv</c>.</summary>
    public string? TargetProcessName { get; init; }

    public WindowMatchCriteria? TargetMatch { get; init; }

    public bool Equals(FrameProfile? other) => other is not null &&
        Id == other.Id && Name == other.Name && HotkeySlot == other.HotkeySlot && Mode == other.Mode &&
        Width == other.Width && Height == other.Height && IsAspectLocked == other.IsAspectLocked && Appearance == other.Appearance &&
        (NestedFrames ?? []).SequenceEqual(other.NestedFrames ?? []) && Anchor == other.Anchor && X == other.X &&
        Y == other.Y && DisplayDeviceName == other.DisplayDeviceName && DisplaySnapshot == other.DisplaySnapshot &&
        KeepClearOfTaskbar == other.KeepClearOfTaskbar &&
        Area == other.Area && TargetProcessName == other.TargetProcessName && TargetMatch == other.TargetMatch;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(HotkeySlot);
        hash.Add(Mode);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(IsAspectLocked);
        hash.Add(Appearance);
        foreach (var frame in NestedFrames ?? []) hash.Add(frame);
        hash.Add(Anchor);
        hash.Add(X);
        hash.Add(Y);
        hash.Add(DisplayDeviceName);
        hash.Add(DisplaySnapshot);
        hash.Add(KeepClearOfTaskbar);
        hash.Add(Area);
        hash.Add(TargetProcessName);
        hash.Add(TargetMatch);
        return hash.ToHashCode();
    }

    public static IReadOnlyList<FrameProfile> Defaults() =>
    [
        new() { Id = "youtube-1080p", Name = "YouTube 1080p", Width = 1920, Height = 1080 },
        new() { Id = "shorts-vertical", Name = "Shorts, vertical", Width = 1080, Height = 1920 },
        new() { Id = "course-1440p", Name = "Course 1440p", Width = 2560, Height = 1440, Anchor = FrameAnchor.TopLeft }
    ];
}
