using System.Drawing;
using ExactFrame.Core.Geometry;

namespace ExactFrame.Core.Models;

/// <summary>A secondary recording guide positioned relative to the main frame.</summary>
public sealed record NestedFrame
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "Extra frame";

    public int AspectWidth { get; init; } = 9;

    public int AspectHeight { get; init; } = 16;

    /// <summary>Percentage of the largest exact-ratio frame that fits inside the main frame.</summary>
    public int ScalePercent { get; init; } = 100;

    public FrameAnchor Anchor { get; init; } = FrameAnchor.Center;

    public OutlineColor Color { get; init; } = OutlineColor.Amber;
}

/// <summary>The resolved screen bounds and appearance sent to the preview and on-screen overlay.</summary>
public sealed record NestedFrameBounds(Rectangle Bounds, string Name, OutlineColor Color);
