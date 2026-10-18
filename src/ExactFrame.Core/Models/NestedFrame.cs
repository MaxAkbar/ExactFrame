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

    public bool UseCustomPosition { get; init; }

    /// <summary>Position within the available travel, retained when the parent changes size.</summary>
    public double PositionXPercent { get; init; } = 50;

    public double PositionYPercent { get; init; } = 50;

    /// <summary>Clearance from the parent outline, in physical pixels.</summary>
    public int InsetPixels { get; init; } = 8;

    /// <summary>Content-safe margin on each side of this crop. Zero hides the guide.</summary>
    public int SafeMarginPercent { get; init; }

    /// <summary>Zero on both dimensions means a guide with no requested output size.</summary>
    public int OutputWidth { get; init; }

    public int OutputHeight { get; init; }

    public OutlineColor Color { get; init; } = OutlineColor.Amber;
}

/// <summary>The resolved screen bounds and appearance sent to the preview and on-screen overlay.</summary>
public sealed record NestedFrameBounds(Rectangle Bounds, string Name, OutlineColor Color)
{
    public string Id { get; init; } = string.Empty;
    public Rectangle MovementArea { get; init; }
    public Rectangle SafeArea { get; init; }
}

public enum NestedFrameInteractionKind { Started, Moved, Completed, Canceled }
public sealed record NestedFrameInteraction(string Id, Rectangle Bounds, NestedFrameInteractionKind Kind);
