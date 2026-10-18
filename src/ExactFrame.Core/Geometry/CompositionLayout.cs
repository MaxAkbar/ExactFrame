using System.Drawing;
using System.Globalization;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.Geometry;

/// <summary>A crop's physical source pixels fitted uniformly into its requested output, without stretching.</summary>
public sealed record CompositionLayout(Rectangle Crop, Rectangle SafeArea, Size OutputSize,
    RectangleF ContentBounds, double Scale, bool HasOutput)
{
    public bool IsAvailable => !Crop.IsEmpty;
    public bool NeedsEnlargement => IsAvailable && Scale > 1.000001;
    public bool HasBars => IsAvailable && (ContentBounds.Width < OutputSize.Width - 0.01 || ContentBounds.Height < OutputSize.Height - 0.01);
    public string SourceText => IsAvailable ? $"Source crop {Crop.Width} × {Crop.Height} px" : "Crop does not fit. Reduce the inset or increase the main frame.";
    public string OutputText => HasOutput ? $"Output {OutputSize.Width} × {OutputSize.Height} px" : "Guide only · no output size";
    public string ScaleText => !IsAvailable ? "No source pixels available"
        : !HasOutput ? "Source pixels only; choose an output size to check scaling."
        : $"{(NeedsEnlargement ? "Enlarges" : Scale < 0.999999 ? "Reduces" : "Uses")} source {Scale.ToString("0.##", CultureInfo.InvariantCulture)}×" +
          (NeedsEnlargement ? "; text may look softer." : "; no enlargement.") +
          (HasBars ? " Aspect ratios differ; empty bars preserve proportions." : string.Empty);

    public static CompositionLayout Create(Rectangle parent, NestedFrame frame)
    {
        var crop = NestedFrameGeometry.Place(parent, frame);
        bool output = frame.OutputWidth is >= 100 and <= 16384 && frame.OutputHeight is >= 100 and <= 16384;
        var size = output ? new Size(frame.OutputWidth, frame.OutputHeight) : crop.Size;
        if (crop.IsEmpty) return new(crop, Rectangle.Empty, size, RectangleF.Empty, 0, output);
        double scale = output ? Math.Min((double)size.Width / crop.Width, (double)size.Height / crop.Height) : 1;
        float width = (float)(crop.Width * scale), height = (float)(crop.Height * scale);
        return new(crop, NestedFrameGeometry.SafeArea(crop, frame.SafeMarginPercent), size,
            new RectangleF((size.Width - width) / 2f, (size.Height - height) / 2f, width, height), scale, output);
    }
}

public sealed record CompositionPreset(string Name, int Width, int Height, int AspectWidth, int AspectHeight)
{
    public static IReadOnlyList<CompositionPreset> All { get; } =
    [
        new("Guide only", 0, 0, 0, 0),
        new("Shorts · 1080 × 1920", 1080, 1920, 9, 16),
        new("Square · 1080 × 1080", 1080, 1080, 1, 1),
        new("Portrait · 1080 × 1350", 1080, 1350, 4, 5),
        new("Landscape · 1920 × 1080", 1920, 1080, 16, 9)
    ];
}
