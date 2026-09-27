using ExactFrame.Core.Geometry;

namespace ExactFrame.Core.Models;

public enum FrameMode
{
    Outline,
    Resize
}

/// <summary>A saved setup. Profiles 1–9 get a Ctrl + Alt + number hotkey in list order.</summary>
public sealed record FrameProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "Profile";

    public FrameMode Mode { get; init; } = FrameMode.Outline;

    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    /// <summary>Anchor inside the display, or <c>null</c> for the exact <see cref="X"/>/<see cref="Y"/> position.</summary>
    public FrameAnchor? Anchor { get; init; } = FrameAnchor.Center;

    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>Windows device name, such as <c>\\.\DISPLAY1</c>. <c>null</c> means the primary display.</summary>
    public string? DisplayDeviceName { get; init; }

    public bool KeepClearOfTaskbar { get; init; }

    public WindowArea Area { get; init; } = WindowArea.Client;

    /// <summary>Process name to select in Resize window mode, such as <c>devenv</c>.</summary>
    public string? TargetProcessName { get; init; }

    public static IReadOnlyList<FrameProfile> Defaults() =>
    [
        new() { Id = "youtube-1080p", Name = "YouTube 1080p", Width = 1920, Height = 1080 },
        new() { Id = "shorts-vertical", Name = "Shorts, vertical", Width = 1080, Height = 1920 },
        new() { Id = "course-1440p", Name = "Course 1440p", Width = 2560, Height = 1440, Anchor = FrameAnchor.TopLeft }
    ];
}
