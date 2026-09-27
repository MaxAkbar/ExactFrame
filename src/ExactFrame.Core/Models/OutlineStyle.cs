namespace ExactFrame.Core.Models;

public enum OutlineColor
{
    Teal,
    Red,
    Amber,
    Blue,
    White
}

public enum OutlineLine
{
    Solid,
    Dashed,
    Corners
}

/// <summary>How the on-screen outline looks. Immutable; change it with <c>with</c> expressions.</summary>
public sealed record OutlineStyle
{
    public static IReadOnlyList<int> Thicknesses { get; } = [2, 4, 6];

    public OutlineColor Color { get; init; } = OutlineColor.Teal;

    /// <summary>Border thickness in physical pixels, drawn inside the frame.</summary>
    public int Thickness { get; init; } = 4;

    public OutlineLine Line { get; init; } = OutlineLine.Solid;

    public bool ShowSizeLabel { get; init; } = true;

    public bool DimOutside { get; init; } = true;

    public int DimOpacityPercent { get; init; } = 45;

    public bool ShowThirds { get; init; }

    public bool ShowCenterMark { get; init; }

    public bool ShowSafeArea { get; init; }

    public bool ShowHud { get; init; } = true;

    public bool HasGuides => ShowThirds || ShowCenterMark || ShowSafeArea;
}

public static class OutlinePalette
{
    public static IReadOnlyList<OutlineColor> All { get; } = Enum.GetValues<OutlineColor>();

    /// <summary>Opaque color as 0xAARRGGBB.</summary>
    public static uint Argb(OutlineColor color) => color switch
    {
        OutlineColor.Red => 0xFFFB5A70,
        OutlineColor.Amber => 0xFFF7B538,
        OutlineColor.Blue => 0xFF6CB2FF,
        OutlineColor.White => 0xFFF4F7F6,
        _ => 0xFF34D5B5
    };

    public static string Hex(OutlineColor color) => "#" + (Argb(color) & 0xFFFFFF).ToString("X6");

    public static string Name(OutlineColor color) => color == OutlineColor.Red ? "Record red" : color.ToString();
}
