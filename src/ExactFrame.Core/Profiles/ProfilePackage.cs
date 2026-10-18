using ExactFrame.Core.Models;

namespace ExactFrame.Core.Profiles;

/// <summary>A portable profile document, independent of local settings and shortcut registrations.</summary>
public sealed record ProfilePackage
{
    public string Format { get; init; } = "exactframe.profiles";
    public int Version { get; init; } = 2;
    public List<PortableProfile> Profiles { get; init; } = [];
}

public sealed record PortableProfile
{
    public FrameProfile Profile { get; init; } = new();
    public DisplayInfo? SourceDisplay { get; init; }
}

public sealed record ProfileImportResult(IReadOnlyList<FrameProfile> Profiles, IReadOnlyList<string> Warnings);
