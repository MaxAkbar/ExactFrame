using System.Drawing;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

/// <summary>Everything the to-scale display map needs to draw the main and nested frames, in physical pixels.</summary>
public sealed record PreviewState(
    Rectangle DisplayBounds,
    Rectangle WorkArea,
    Rectangle Frame,
    bool IsResizeMode,
    OutlineStyle Style,
    bool ShowHandles,
    bool ShowWindowChrome,
    int TitleBarHeight,
    Rectangle? Ghost,
    string SizeText,
    bool IsFit,
    IReadOnlyList<NestedFrameBounds> NestedFrames)
{
    public static PreviewState Empty { get; } = new(
        new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040), new Rectangle(0, 0, 1280, 720),
        false, new OutlineStyle(), true, false, 32, null, "1280 × 720", true, []);
}

public enum SettingsTab
{
    Frame,
    Style,
    Profiles
}

public enum StatusKind
{
    Ready,
    Live,
    Busy,
    Warning
}
