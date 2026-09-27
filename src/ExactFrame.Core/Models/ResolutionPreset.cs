namespace ExactFrame.Core.Models;

public sealed record ResolutionPreset(string Id, string Name, int Width, int Height)
{
    public bool IsCustom => Width == 0 || Height == 0;

    public string Dimensions => IsCustom ? "Any size" : $"{Width} × {Height}";

    public static IReadOnlyList<ResolutionPreset> All { get; } =
    [
        new("720p", "720p", 1280, 720),
        new("1080p", "1080p", 1920, 1080),
        new("1440p", "1440p", 2560, 1440),
        new("4k", "4K", 3840, 2160),
        new("vertical", "Vertical", 1080, 1920),
        new("custom", "Custom", 0, 0)
    ];

    public static ResolutionPreset Custom => All[^1];

    public static ResolutionPreset Match(int width, int height) =>
        All.FirstOrDefault(p => p.Width == width && p.Height == height) ?? Custom;
}
