using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.Profiles;

/// <summary>
/// Reads a bounded, versioned interchange document. Importing never changes settings or applies a frame.
/// Explicit wire types keep local shortcuts, monitor paths and unrelated settings out of shared files.
/// </summary>
public static class ProfilePackageSerializer
{
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxProfiles = 100;
    private const int MaxGuides = 16;
    private const int MaxCoordinate = 100_000;
    private const int MaxDepth = 16;
    private const string Format = "exactframe.profiles";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = MaxDepth,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string Export(IReadOnlyList<FrameProfile> profiles, IReadOnlyList<DisplayInfo> displays)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(displays);
        if (profiles.Count is < 1 or > MaxProfiles)
            throw Invalid($"A profile file must contain between 1 and {MaxProfiles} profiles.");
        var package = new ProfilePackage
        {
            Profiles = [.. profiles.Select(profile =>
            {
                if (profile is null) throw Invalid("A profile is missing.");
                var display = profile.DisplayDeviceName is { } device
                    ? displays.FirstOrDefault(candidate => SameDevice(candidate.DeviceName, device))
                    : displays.FirstOrDefault(candidate => candidate.IsPrimary);
                return new PortableProfile
                {
                    Profile = profile,
                    // The saved X/Y belong to the saved topology, even if Windows has since moved this display.
                    SourceDisplay = Sanitize(profile.DisplaySnapshot ?? display)
                };
            })]
        };
        Validate(package);
        string json = JsonSerializer.Serialize(ToWire(package), Options);
        CheckSize(json);
        return json;
    }

    public static ProfilePackage Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        CheckSize(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
            RejectDuplicateProperties(document.RootElement);
            var wire = document.RootElement.Deserialize<WirePackage>(Options)
                ?? throw Invalid("The document is empty.");
            var package = FromWire(wire);
            Validate(package);
            return package;
        }
        catch (JsonException ex)
        {
            throw Invalid("The JSON contains missing, unsupported or incorrectly typed fields.", ex);
        }
        catch (NotSupportedException ex)
        {
            throw Invalid("The document contains an unsupported value.", ex);
        }
    }

    public static string DisplayKey(PortableProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.SourceDisplay?.DeviceName ?? profile.Profile.DisplayDeviceName ?? "$primary";
    }

    public static ProfileImportResult Remap(ProfilePackage package, IReadOnlyDictionary<string, string> mappings,
        IReadOnlyList<DisplayInfo> displays)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(displays);
        Validate(package);
        foreach (var display in displays) ValidateDisplay(display);
        if (displays.Select(display => display.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != displays.Count)
            throw Invalid("The connected display list contains duplicate identifiers.");

        // Resolve every choice before building results. A missing mapping never creates a partial import.
        var targets = new Dictionary<string, DisplayInfo>(StringComparer.Ordinal);
        foreach (var entry in package.Profiles)
        {
            string key = DisplayKey(entry);
            if (targets.ContainsKey(key)) continue;
            if (!mappings.TryGetValue(key, out string? chosen) || string.IsNullOrWhiteSpace(chosen))
                throw Invalid("Choose a connected display for every source display before importing.");
            var target = displays.FirstOrDefault(display => SameDevice(display.DeviceName, chosen))
                ?? throw Invalid("A chosen display is no longer connected. Choose a display again.");
            targets.Add(key, target);
        }

        var profiles = new List<FrameProfile>(package.Profiles.Count);
        var warnings = new List<string>();
        foreach (var entry in package.Profiles)
        {
            var profile = entry.Profile;
            var target = targets[DisplayKey(entry)];
            var targetArea = target.UsableArea(profile.KeepClearOfTaskbar);
            bool fits = profile.Width <= targetArea.Width && profile.Height <= targetArea.Height;
            int x = 0, y = 0;
            if (profile.Anchor is null)
            {
                long translatedX, translatedY;
                if (entry.SourceDisplay is { } source)
                {
                    var sourceArea = source.UsableArea(profile.KeepClearOfTaskbar);
                    translatedX = (long)targetArea.X + profile.X - sourceArea.X;
                    translatedY = (long)targetArea.Y + profile.Y - sourceArea.Y;
                }
                else
                {
                    translatedX = (long)targetArea.X + (targetArea.Width - profile.Width) / 2;
                    translatedY = (long)targetArea.Y + (targetArea.Height - profile.Height) / 2;
                    warnings.Add($"“{profile.Name}”: original display geometry is missing; its position was centered on {target.Title}.");
                }
                long boundedX = fits ? Math.Clamp(translatedX, targetArea.Left, (long)targetArea.Right - profile.Width) : translatedX;
                long boundedY = fits ? Math.Clamp(translatedY, targetArea.Top, (long)targetArea.Bottom - profile.Height) : translatedY;
                x = (int)Math.Clamp(boundedX, -MaxCoordinate, MaxCoordinate);
                y = (int)Math.Clamp(boundedY, -MaxCoordinate, MaxCoordinate);
                if (x != translatedX || y != translatedY)
                    warnings.Add($"“{profile.Name}”: its position was adjusted to stay within supported display coordinates.");
            }
            if (!fits)
                warnings.Add($"“{profile.Name}”: {profile.Width} × {profile.Height} px does not fit {target.Title}'s usable area. Its size was preserved.");

            profiles.Add(profile with
            {
                Id = Guid.NewGuid().ToString("N"),
                NestedFrames = [.. profile.NestedFrames.Select(guide => guide with { Id = Guid.NewGuid().ToString("N") })],
                HotkeySlot = 0,
                DisplayDeviceName = target.IsPrimary ? null : target.DeviceName,
                DisplaySnapshot = Sanitize(target),
                X = x,
                Y = y,
                Appearance = profile.Appearance is { } appearance ? appearance with { } : null
            });
        }
        return new(profiles.AsReadOnly(), warnings.AsReadOnly());
    }

    private static bool SameDevice(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static DisplayInfo? Sanitize(DisplayInfo? display) => display is null ? null :
        new(display.DeviceName, display.Number, display.Bounds, display.WorkArea, display.Dpi, display.IsPrimary);

    private static void CheckSize(string json)
    {
        if (json.Length > MaxFileBytes || Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
            throw Invalid("The profile file is larger than the 1 MiB limit.");
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("The JSON contains a duplicate field.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static void Validate(ProfilePackage package)
    {
        if (package.Format != Format || package.Version is not (1 or 2))
            throw Invalid("This is not a supported ExactFrame profile file (format versions 1 and 2).");
        if (package.Profiles is null || package.Profiles.Count is < 1 or > MaxProfiles)
            throw Invalid($"A profile file must contain between 1 and {MaxProfiles} profiles.");
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in package.Profiles)
        {
            if (entry?.Profile is not { } profile) throw Invalid("A profile is missing.");
            Identifier(profile.Id, "profile ID");
            if (!profileIds.Add(profile.Id)) throw Invalid("The file contains duplicate profile IDs.");
            Text(profile.Name, 120, "profile name");
            Defined(profile.Mode, "profile mode");
            Defined(profile.Area, "window area");
            if (profile.Anchor is { } anchor) Defined(anchor, "frame anchor");
            Range(profile.Width, 100, 16384, "frame width"); Range(profile.Height, 100, 16384, "frame height");
            Range(profile.X, -MaxCoordinate, MaxCoordinate, "frame X"); Range(profile.Y, -MaxCoordinate, MaxCoordinate, "frame Y");
            if (profile.DisplayDeviceName is { } device) Text(device, 256, "display identifier");
            if (profile.TargetProcessName is { } process)
            {
                Text(process, 120, "process name");
                if (process.IndexOfAny(['/', '\\', ':', '\0']) >= 0)
                    throw Invalid("A target process must be a process name, not a file path.");
            }
            if (profile.NestedFrames is null || profile.NestedFrames.Count > MaxGuides)
                throw Invalid($"A profile can contain at most {MaxGuides} additional guides.");
            var guideIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var guide in profile.NestedFrames)
            {
                if (guide is null) throw Invalid("An additional guide is missing.");
                Identifier(guide.Id, "guide ID");
                if (!guideIds.Add(guide.Id)) throw Invalid("A profile contains duplicate guide IDs.");
                Text(guide.Name, 80, "guide name");
                Range(guide.AspectWidth, 1, 32, "guide aspect width");
                Range(guide.AspectHeight, 1, 32, "guide aspect height");
                Range(guide.ScalePercent, 10, 100, "guide scale");
                Range(guide.InsetPixels, 0, 1000, "guide inset");
                Range(guide.SafeMarginPercent, 0, 40, "guide safe margin");
                if (!double.IsFinite(guide.PositionXPercent) || guide.PositionXPercent is < 0 or > 100 ||
                    !double.IsFinite(guide.PositionYPercent) || guide.PositionYPercent is < 0 or > 100)
                    throw Invalid("Guide positions must be between 0 and 100 percent.");
                if (guide.OutputWidth != 0 || guide.OutputHeight != 0)
                {
                    Range(guide.OutputWidth, 100, 16384, "output width");
                    Range(guide.OutputHeight, 100, 16384, "output height");
                }
                Defined(guide.Anchor, "guide anchor"); Defined(guide.Color, "guide color");
            }
            if (profile.Appearance is { } appearance)
            {
                Defined(appearance.Color, "outline color"); Defined(appearance.Line, "outline line");
                if (!OutlineStyle.Thicknesses.Contains(appearance.Thickness)) throw Invalid("The outline thickness is unsupported.");
                Range(appearance.DimOpacityPercent, 10, 80, "dimming opacity");
            }
            if (entry.SourceDisplay is { } source) ValidateDisplay(source);
        }
    }

    private static void ValidateDisplay(DisplayInfo display)
    {
        if (display is null) throw Invalid("A display is missing.");
        Text(display.DeviceName, 256, "display identifier");
        Range(display.Number, 1, 1000, "display number");
        Range(display.Dpi, 48, 960, "display DPI");
        DisplayRectangle(display.Bounds); DisplayRectangle(display.WorkArea);
        if (!display.Bounds.Contains(display.WorkArea)) throw Invalid("A display work area lies outside its bounds.");
    }

    private static void DisplayRectangle(Rectangle rectangle)
    {
        Range(rectangle.X, -MaxCoordinate, MaxCoordinate, "display X");
        Range(rectangle.Y, -MaxCoordinate, MaxCoordinate, "display Y");
        Range(rectangle.Width, 1, MaxCoordinate, "display width");
        Range(rectangle.Height, 1, MaxCoordinate, "display height");
        if ((long)rectangle.X + rectangle.Width > MaxCoordinate || (long)rectangle.Y + rectangle.Height > MaxCoordinate)
            throw Invalid("Display coordinates exceed the supported range.");
    }

    private static void Text(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw Invalid($"The {label} is missing, too long or contains control characters.");
    }

    private static void Identifier(string? value, string label)
    {
        Text(value, 80, label);
        if (value!.Any(character => character is < '!' or > '~')) throw Invalid($"The {label} contains unsupported characters.");
    }

    private static void Defined<T>(T value, string label) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw Invalid($"The {label} is unsupported.");
    }

    private static void Range(int value, int minimum, int maximum, string label)
    {
        if (value < minimum || value > maximum) throw Invalid($"The {label} must be between {minimum} and {maximum}.");
    }

    private static InvalidOperationException Invalid(string detail, Exception? inner = null) =>
        new("Could not use this profile file. " + detail, inner);

    private static WirePackage ToWire(ProfilePackage package) => new()
    {
        Format = package.Format, Version = package.Version,
        Profiles = [.. package.Profiles.Select(entry => new WireEntry
        {
            Profile = new WireProfile
            {
                Id = entry.Profile.Id, Name = entry.Profile.Name, Mode = entry.Profile.Mode,
                Width = entry.Profile.Width, Height = entry.Profile.Height,
                NestedFrames = [.. entry.Profile.NestedFrames.Select(guide => new WireGuide
                {
                    Id = guide.Id, Name = guide.Name, AspectWidth = guide.AspectWidth, AspectHeight = guide.AspectHeight,
                    ScalePercent = guide.ScalePercent, Anchor = guide.Anchor, Color = guide.Color,
                    UseCustomPosition = guide.UseCustomPosition, PositionXPercent = guide.PositionXPercent,
                    PositionYPercent = guide.PositionYPercent, InsetPixels = guide.InsetPixels,
                    SafeMarginPercent = guide.SafeMarginPercent, OutputWidth = guide.OutputWidth, OutputHeight = guide.OutputHeight
                })],
                Anchor = entry.Profile.Anchor, X = entry.Profile.X, Y = entry.Profile.Y,
                DisplayDeviceName = entry.Profile.DisplayDeviceName, KeepClearOfTaskbar = entry.Profile.KeepClearOfTaskbar,
                Area = entry.Profile.Area, TargetProcessName = entry.Profile.TargetProcessName,
                IsAspectLocked = entry.Profile.IsAspectLocked,
                Appearance = entry.Profile.Appearance is { } appearance ? new WireAppearance
                {
                    Color = appearance.Color, Thickness = appearance.Thickness, Line = appearance.Line,
                    ShowSizeLabel = appearance.ShowSizeLabel, DimOutside = appearance.DimOutside,
                    DimOpacityPercent = appearance.DimOpacityPercent, ShowThirds = appearance.ShowThirds,
                    ShowCenterMark = appearance.ShowCenterMark, ShowSafeArea = appearance.ShowSafeArea,
                    ShowEdgeMagnifier = appearance.ShowEdgeMagnifier, ShowHud = appearance.ShowHud
                } : null
            },
            SourceDisplay = entry.SourceDisplay is { } display ? new WireDisplay
            {
                DeviceName = display.DeviceName, Number = display.Number, Bounds = Rect(display.Bounds),
                WorkArea = Rect(display.WorkArea), Dpi = display.Dpi, IsPrimary = display.IsPrimary
            } : null
        })]
    };

    private static WireRectangle Rect(Rectangle rectangle) => new()
    { X = rectangle.X, Y = rectangle.Y, Width = rectangle.Width, Height = rectangle.Height };

    private static ProfilePackage FromWire(WirePackage wire)
    {
        if (wire.Profiles is null || wire.Profiles.Count is < 1 or > MaxProfiles)
            throw Invalid($"A profile file must contain between 1 and {MaxProfiles} profiles.");
        return new ProfilePackage
        {
            Format = wire.Format, Version = wire.Version,
            Profiles = [.. wire.Profiles.Select(entry =>
            {
                if (entry?.Profile is not { } profile || profile.NestedFrames is null)
                    throw Invalid("A profile or its guide list is missing.");
                if (profile.NestedFrames.Count > MaxGuides)
                    throw Invalid($"A profile can contain at most {MaxGuides} additional guides.");
                return new PortableProfile
                {
                    Profile = new FrameProfile
                    {
                        Id = profile.Id, Name = profile.Name, Mode = profile.Mode, Width = profile.Width, Height = profile.Height,
                        NestedFrames = [.. profile.NestedFrames.Select(guide => guide is null ? throw Invalid("An additional guide is missing.") : new NestedFrame
                        {
                            Id = guide.Id, Name = guide.Name, AspectWidth = guide.AspectWidth, AspectHeight = guide.AspectHeight,
                            ScalePercent = guide.ScalePercent, Anchor = guide.Anchor, Color = guide.Color,
                            UseCustomPosition = guide.UseCustomPosition, PositionXPercent = guide.PositionXPercent,
                            PositionYPercent = guide.PositionYPercent, InsetPixels = guide.InsetPixels,
                            SafeMarginPercent = guide.SafeMarginPercent, OutputWidth = guide.OutputWidth, OutputHeight = guide.OutputHeight
                        })],
                        Anchor = profile.Anchor, X = profile.X, Y = profile.Y, DisplayDeviceName = profile.DisplayDeviceName,
                        KeepClearOfTaskbar = profile.KeepClearOfTaskbar, Area = profile.Area, TargetProcessName = profile.TargetProcessName,
                        IsAspectLocked = profile.IsAspectLocked, HotkeySlot = 0,
                        Appearance = profile.Appearance is { } appearance ? new OutlineStyle
                        {
                            Color = appearance.Color, Thickness = appearance.Thickness, Line = appearance.Line,
                            ShowSizeLabel = appearance.ShowSizeLabel, DimOutside = appearance.DimOutside,
                            DimOpacityPercent = appearance.DimOpacityPercent, ShowThirds = appearance.ShowThirds,
                            ShowCenterMark = appearance.ShowCenterMark, ShowSafeArea = appearance.ShowSafeArea,
                            ShowEdgeMagnifier = appearance.ShowEdgeMagnifier, ShowHud = appearance.ShowHud
                        } : null
                    },
                    SourceDisplay = entry.SourceDisplay is { } display ? new DisplayInfo(display.DeviceName, display.Number,
                        FromRect(display.Bounds), FromRect(display.WorkArea), display.Dpi, display.IsPrimary) : null
                };
            })]
        };
    }

    private static Rectangle FromRect(WireRectangle rectangle) => rectangle is null
        ? throw Invalid("Display geometry is missing.") : new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    private sealed class WirePackage
    {
        public required string Format { get; init; }
        public required int Version { get; init; }
        public required List<WireEntry> Profiles { get; init; }
    }

    private sealed class WireEntry
    {
        public required WireProfile Profile { get; init; }
        public WireDisplay? SourceDisplay { get; init; }
    }

    private sealed class WireProfile
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required FrameMode Mode { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required List<WireGuide> NestedFrames { get; init; }
        public required FrameAnchor? Anchor { get; init; }
        public required int X { get; init; }
        public required int Y { get; init; }
        public string? DisplayDeviceName { get; init; }
        public required bool KeepClearOfTaskbar { get; init; }
        public required WindowArea Area { get; init; }
        public string? TargetProcessName { get; init; }
        public required bool IsAspectLocked { get; init; }
        public WireAppearance? Appearance { get; init; }
    }

    private sealed class WireGuide
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required int AspectWidth { get; init; }
        public required int AspectHeight { get; init; }
        public required int ScalePercent { get; init; }
        public required FrameAnchor Anchor { get; init; }
        public required OutlineColor Color { get; init; }
        public bool UseCustomPosition { get; init; }
        public double PositionXPercent { get; init; } = 50;
        public double PositionYPercent { get; init; } = 50;
        public int InsetPixels { get; init; } = 8;
        public int SafeMarginPercent { get; init; }
        public int OutputWidth { get; init; }
        public int OutputHeight { get; init; }
    }

    private sealed class WireAppearance
    {
        public required OutlineColor Color { get; init; }
        public required int Thickness { get; init; }
        public required OutlineLine Line { get; init; }
        public required bool ShowSizeLabel { get; init; }
        public required bool DimOutside { get; init; }
        public required int DimOpacityPercent { get; init; }
        public required bool ShowThirds { get; init; }
        public required bool ShowCenterMark { get; init; }
        public required bool ShowSafeArea { get; init; }
        public required bool ShowEdgeMagnifier { get; init; }
        public required bool ShowHud { get; init; }
    }

    private sealed class WireDisplay
    {
        public required string DeviceName { get; init; }
        public required int Number { get; init; }
        public required WireRectangle Bounds { get; init; }
        public required WireRectangle WorkArea { get; init; }
        public required int Dpi { get; init; }
        public required bool IsPrimary { get; init; }
    }

    private sealed class WireRectangle
    {
        public required int X { get; init; }
        public required int Y { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
    }
}
