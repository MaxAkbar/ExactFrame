using System.Drawing;

namespace ExactFrame.Core.Models;

/// <summary>One gesture's resize constraint and snapshot of physical-pixel snap targets.</summary>
public sealed record FrameEditingOptions(double? AspectRatio = null, IReadOnlyList<Rectangle>? SnapTargets = null, int SnapDistance = 0);
