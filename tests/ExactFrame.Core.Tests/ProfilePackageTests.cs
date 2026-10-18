using System.Drawing;
using System.Text;
using System.Text.Json.Nodes;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;

namespace ExactFrame.Core.Tests;

public sealed class ProfilePackageTests
{
    private static readonly DisplayInfo Source = new(@"\\.\DISPLAY2", 2,
        new Rectangle(-3840, -200, 3840, 2160), new Rectangle(-3840, -160, 3840, 2120), 144, false)
        { MonitorDevicePath = "PRIVATE-MONITOR-PATH" };
    private static readonly DisplayInfo Target = new(@"\\.\DISPLAY1", 1,
        new Rectangle(0, 0, 2560, 1440), new Rectangle(0, 40, 2560, 1400), 96, true)
        { MonitorDevicePath = "LOCAL-MONITOR-PATH" };

    [Fact]
    public void Round_trip_preserves_editable_fields_and_optional_appearance()
    {
        var original = Profile() with
        {
            Mode = FrameMode.Resize, Area = WindowArea.PageContent, TargetProcessName = "chrome",
            Appearance = new OutlineStyle
            {
                Color = OutlineColor.Red, Thickness = 6, Line = OutlineLine.Corners, DimOpacityPercent = 70,
                DimOutside = false, ShowCenterMark = true, ShowThirds = true, ShowSafeArea = true,
                ShowHud = false, ShowSizeLabel = false, ShowEdgeMagnifier = true
            }
        };
        string json = ProfilePackageSerializer.Export([original], [Source]);
        var entry = Assert.Single(ProfilePackageSerializer.Parse(json).Profiles);

        Assert.Equal(original with { HotkeySlot = 0, DisplaySnapshot = null }, entry.Profile);
        Assert.Equal(Source with { MonitorDevicePath = null }, entry.SourceDisplay);
        Assert.Equal(original.Appearance, entry.Profile.Appearance);
        Assert.False(entry.Profile.IsAspectLocked);
    }

    [Fact]
    public void Export_never_shares_local_shortcuts_snapshots_monitor_paths_or_computed_properties()
    {
        var profile = Profile() with { HotkeySlot = 7, DisplaySnapshot = Target, Appearance = new OutlineStyle() };
        string json = ProfilePackageSerializer.Export([profile], [Source]);
        var entry = JsonNode.Parse(json)!["profiles"]![0]!;
        var wireProfile = entry["profile"]!;
        Assert.Null(wireProfile["hotkeySlot"]);
        Assert.Null(wireProfile["displaySnapshot"]);
        Assert.Null(entry["sourceDisplay"]!["monitorDevicePath"]);
        Assert.Null(entry["sourceDisplay"]!["scale"]);
        Assert.Null(entry["sourceDisplay"]!["title"]);
        Assert.Null(wireProfile["appearance"]!["hasGuides"]);
        Assert.DoesNotContain("MONITOR-PATH", json);
    }

    [Fact]
    public void Export_uses_saved_geometry_when_the_original_display_is_disconnected()
    {
        var original = Profile() with { DisplaySnapshot = Source };
        var entry = Assert.Single(ProfilePackageSerializer.Parse(ProfilePackageSerializer.Export([original], [Target])).Profiles);
        Assert.Equal(Source with { MonitorDevicePath = null }, entry.SourceDisplay);
    }

    [Fact]
    public void Export_after_monitor_rearrangement_preserves_the_saved_relative_position()
    {
        var saved = Profile() with { DisplaySnapshot = Source };
        var rearranged = Source with
        {
            Bounds = new Rectangle(0, 0, 3840, 2160),
            WorkArea = new Rectangle(0, 40, 3840, 2120)
        };
        var target = Target with
        {
            Bounds = new Rectangle(6400, 0, 2560, 1440),
            WorkArea = new Rectangle(6400, 40, 2560, 1400)
        };
        var package = ProfilePackageSerializer.Parse(ProfilePackageSerializer.Export([saved], [rearranged]));
        Assert.Equal(Source with { MonitorDevicePath = null }, package.Profiles[0].SourceDisplay);

        var result = ProfilePackageSerializer.Remap(package,
            new Dictionary<string, string> { [Source.DeviceName] = target.DeviceName }, [target]);
        var imported = Assert.Single(result.Profiles);
        Assert.Equal(6500, imported.X);
        Assert.Equal(190, imported.Y);
        Assert.Equal(saved.Width, imported.Width);
        Assert.Equal(saved.Height, imported.Height);
        Assert.Empty(result.Warnings);
        Assert.Equal(-3740, saved.X);
        Assert.Equal(-10, saved.Y);
    }

    [Fact]
    public void Primary_profile_uses_the_current_primary_display()
    {
        var entry = Assert.Single(ProfilePackageSerializer.Parse(
            ProfilePackageSerializer.Export([Profile() with { DisplayDeviceName = null }], [Source, Target])).Profiles);
        Assert.Equal(Target.DeviceName, ProfilePackageSerializer.DisplayKey(entry));
        Assert.Equal(Target with { MonitorDevicePath = null }, entry.SourceDisplay);
    }

    [Fact]
    public void Display_key_has_a_primary_fallback_when_original_geometry_is_missing()
    {
        Assert.Equal("$primary", ProfilePackageSerializer.DisplayKey(new() { Profile = Profile() with { DisplayDeviceName = null } }));
        Assert.Equal(Source.DeviceName, ProfilePackageSerializer.DisplayKey(new() { Profile = Profile() }));
    }

    [Fact]
    public void Custom_position_remaps_usable_origin_without_scaling_physical_pixels()
    {
        var original = Profile() with { X = -3740, Y = -10 };
        var package = ProfilePackageSerializer.Parse(ProfilePackageSerializer.Export([original], [Source]));
        var result = ProfilePackageSerializer.Remap(package, Mapping(), [Target]);
        var mapped = Assert.Single(result.Profiles);

        Assert.Equal(100, mapped.X);
        Assert.Equal(190, mapped.Y);
        Assert.Equal(original.Width, mapped.Width);
        Assert.Equal(original.Height, mapped.Height);
        Assert.Null(mapped.Anchor);
        Assert.Null(mapped.DisplayDeviceName);
        Assert.Equal(Target with { MonitorDevicePath = null }, mapped.DisplaySnapshot);
        Assert.Empty(result.Warnings);
        Assert.Equal(-3740, original.X);
        Assert.Equal(-10, original.Y);
    }

    [Theory]
    [InlineData(false, -2460, 1630)]
    [InlineData(true, -2380, 1590)]
    public void Negative_destination_and_different_taskbar_edges_use_the_selected_physical_area(
        bool keepClear, int expectedX, int expectedY)
    {
        var target = new DisplayInfo(@"\\.\DISPLAY3", 3, new Rectangle(-2560, 1440, 2560, 1440),
            new Rectangle(-2480, 1440, 2480, 1440), 192, false);
        var original = Profile() with { KeepClearOfTaskbar = keepClear };
        var result = ProfilePackageSerializer.Remap(Package(original),
            new Dictionary<string, string> { [Source.DeviceName] = target.DeviceName }, [target]);
        var mapped = Assert.Single(result.Profiles);

        Assert.Equal(expectedX, mapped.X);
        Assert.Equal(expectedY, mapped.Y);
        Assert.Equal(800, mapped.Width);
        Assert.Equal(600, mapped.Height);
        Assert.Equal(keepClear, mapped.KeepClearOfTaskbar);
        Assert.Equal(target.DeviceName, mapped.DisplayDeviceName);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Imported_profile_and_guides_get_fresh_ids_without_assigning_shortcuts()
    {
        var original = Profile() with { HotkeySlot = 4 };
        var package = ProfilePackageSerializer.Parse(ProfilePackageSerializer.Export([original], [Source]));
        var first = Assert.Single(ProfilePackageSerializer.Remap(package, Mapping(), [Target]).Profiles);
        var second = Assert.Single(ProfilePackageSerializer.Remap(package, Mapping(), [Target]).Profiles);

        Assert.NotEqual(original.Id, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(original.NestedFrames[0].Id, first.NestedFrames[0].Id);
        Assert.NotEqual(first.NestedFrames[0].Id, second.NestedFrames[0].Id);
        Assert.Equal(0, first.HotkeySlot);
        Assert.Equal(original.Id, package.Profiles[0].Profile.Id);
        Assert.Equal(original.NestedFrames[0].Id, package.Profiles[0].Profile.NestedFrames[0].Id);
    }

    [Fact]
    public void Remapping_preserves_anchors_and_marks_the_chosen_nonprimary_display()
    {
        var original = Profile() with { Anchor = FrameAnchor.BottomRight, X = 321, Y = 654 };
        var secondary = Target with { DeviceName = @"\\.\DISPLAY3", IsPrimary = false };
        var package = ProfilePackageSerializer.Parse(ProfilePackageSerializer.Export([original], [Source]));
        var result = ProfilePackageSerializer.Remap(package,
            new Dictionary<string, string> { [Source.DeviceName] = secondary.DeviceName.ToLowerInvariant() }, [secondary]);
        var mapped = Assert.Single(result.Profiles);

        Assert.Equal(FrameAnchor.BottomRight, mapped.Anchor);
        Assert.Equal(0, mapped.X);
        Assert.Equal(0, mapped.Y);
        Assert.Equal(secondary.DeviceName, mapped.DisplayDeviceName);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_fitting_custom_frame_is_clamped_and_reports_the_position_adjustment()
    {
        var package = Package(Profile() with { X = -10, Y = 1900 });
        var result = ProfilePackageSerializer.Remap(package, Mapping(), [Target]);
        var mapped = Assert.Single(result.Profiles);
        Assert.Equal(1760, mapped.X);
        Assert.Equal(840, mapped.Y);
        Assert.Contains(result.Warnings, warning => warning.Contains("position was adjusted"));
    }

    [Fact]
    public void Missing_original_geometry_centers_custom_frames_with_a_clear_warning()
    {
        var package = new ProfilePackage
        {
            Profiles = [new() { Profile = Profile() with { DisplayDeviceName = null } }]
        };
        var result = ProfilePackageSerializer.Remap(package,
            new Dictionary<string, string> { ["$primary"] = Target.DeviceName }, [Target]);
        var mapped = Assert.Single(result.Profiles);
        Assert.Equal(880, mapped.X);
        Assert.Equal(440, mapped.Y);
        Assert.Contains(result.Warnings, warning => warning.Contains("geometry is missing") && warning.Contains("centered"));
    }

    [Fact]
    public void Oversized_profiles_keep_physical_dimensions_and_report_that_they_do_not_fit()
    {
        var original = Profile() with { Width = 3840, Height = 2160, X = -3800, Y = -100 };
        var result = ProfilePackageSerializer.Remap(Package(original), Mapping(), [Target]);
        var mapped = Assert.Single(result.Profiles);
        Assert.Equal(3840, mapped.Width);
        Assert.Equal(2160, mapped.Height);
        Assert.Equal(40, mapped.X);
        Assert.Equal(100, mapped.Y);
        Assert.Contains(result.Warnings, warning => warning.Contains("does not fit") && warning.Contains("size was preserved"));
    }

    [Fact]
    public void Missing_or_stale_mapping_fails_without_mutating_the_package()
    {
        var original = Profile();
        var second = Profile() with { Id = "second-profile", DisplayDeviceName = null };
        var package = new ProfilePackage
        {
            Profiles = [new() { Profile = original, SourceDisplay = Source }, new() { Profile = second }]
        };
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Remap(package, Mapping(), [Target]));
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Remap(package,
            new Dictionary<string, string> { [Source.DeviceName] = "disconnected", ["$primary"] = Target.DeviceName }, [Target]));
        Assert.Same(original, package.Profiles[0].Profile);
        Assert.Same(second, package.Profiles[1].Profile);
        Assert.Equal(Source, package.Profiles[0].SourceDisplay);
    }

    [Theory]
    [InlineData("format", "another-app")]
    [InlineData("version", 3)]
    [InlineData("version", 0)]
    public void Unsupported_format_or_version_is_rejected(string property, object value)
    {
        var document = Document();
        document[property] = JsonValue.Create(value);
        Reject(document);
    }

    [Theory]
    [InlineData("width", 99)]
    [InlineData("width", 16385)]
    [InlineData("height", 0)]
    [InlineData("x", -100001)]
    [InlineData("y", 100001)]
    [InlineData("mode", 1)]
    [InlineData("mode", "1")]
    [InlineData("mode", "Unknown")]
    [InlineData("anchor", 999)]
    [InlineData("area", "Desktop")]
    [InlineData("name", "")]
    [InlineData("name", "bad\nname")]
    [InlineData("id", "bad id")]
    [InlineData("targetProcessName", "C:\\secret\\chrome.exe")]
    public void Invalid_profile_values_are_rejected(string property, object value)
    {
        var document = Document();
        document["profiles"]![0]!["profile"]![property] = JsonValue.Create(value);
        Reject(document);
    }

    [Theory]
    [InlineData("aspectWidth", 0)]
    [InlineData("aspectHeight", 33)]
    [InlineData("scalePercent", 9)]
    [InlineData("scalePercent", 101)]
    [InlineData("anchor", "Invalid")]
    [InlineData("color", 0)]
    [InlineData("name", "\tname")]
    public void Invalid_guide_values_are_rejected(string property, object value)
    {
        var document = Document();
        document["profiles"]![0]!["profile"]!["nestedFrames"]![0]![property] = JsonValue.Create(value);
        Reject(document);
    }

    [Theory]
    [InlineData("thickness", 3)]
    [InlineData("dimOpacityPercent", 9)]
    [InlineData("dimOpacityPercent", 81)]
    [InlineData("color", "Invalid")]
    [InlineData("line", 1)]
    public void Invalid_appearance_values_are_rejected(string property, object value)
    {
        var document = Document(Profile() with { Appearance = new OutlineStyle() });
        document["profiles"]![0]!["profile"]!["appearance"]![property] = JsonValue.Create(value);
        Reject(document);
    }

    [Fact]
    public void Unknown_fields_cannot_smuggle_local_settings_or_shortcut_assignments()
    {
        var document = Document();
        document["profiles"]![0]!["profile"]!["hotkeySlot"] = 1;
        Reject(document);
        document = Document();
        document["profiles"]![0]!["sourceDisplay"]!["monitorDevicePath"] = "private";
        Reject(document);
        document = Document();
        document["hideFromRecorders"] = false;
        Reject(document);
    }

    [Fact]
    public void Duplicate_fields_are_rejected_even_when_the_values_are_equal()
    {
        string json = ProfilePackageSerializer.Export([Profile()], [Source]);
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Parse(
            json.Replace("\"version\": 2", "\"version\": 2, \"version\": 2", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Parse(
            json.Replace("\"width\": 800", "\"width\": 800, \"width\": 900", StringComparison.Ordinal)));
    }

    [Fact]
    public void Missing_required_or_null_profile_data_is_rejected()
    {
        var document = Document();
        document["profiles"]![0]!["profile"]!.AsObject().Remove("isAspectLocked");
        Reject(document);
        document = Document();
        document["profiles"]![0]!["profile"]!["nestedFrames"] = null;
        Reject(document);
        document = Document();
        document["profiles"]![0] = null;
        Reject(document);
        document = Document();
        document["profiles"]![0]!["profile"]!["name"] = null;
        Reject(document);
    }

    [Theory]
    [InlineData("package", "format")]
    [InlineData("package", "version")]
    [InlineData("profile", "width")]
    [InlineData("profile", "anchor")]
    [InlineData("profile", "keepClearOfTaskbar")]
    [InlineData("guide", "aspectWidth")]
    [InlineData("display", "dpi")]
    [InlineData("display", "bounds")]
    [InlineData("rectangle", "width")]
    [InlineData("appearance", "color")]
    public void Required_fields_in_each_wire_object_must_be_present(string section, string property)
    {
        var document = Document(Profile() with { Appearance = new OutlineStyle() });
        var entry = document["profiles"]![0]!;
        var profile = entry["profile"]!;
        var target = section switch
        {
            "package" => document,
            "profile" => profile,
            "guide" => profile["nestedFrames"]![0]!,
            "display" => entry["sourceDisplay"]!,
            "rectangle" => entry["sourceDisplay"]!["bounds"]!,
            "appearance" => profile["appearance"]!,
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        target.AsObject().Remove(property);
        Reject(document);
    }

    [Fact]
    public void Appearance_and_source_geometry_may_be_omitted_without_injecting_defaults()
    {
        var document = Document();
        var entry = document["profiles"]![0]!;
        entry.AsObject().Remove("sourceDisplay");
        entry["profile"]!.AsObject().Remove("appearance");
        var parsed = Assert.Single(ProfilePackageSerializer.Parse(document.ToJsonString()).Profiles);
        Assert.Null(parsed.SourceDisplay);
        Assert.Null(parsed.Profile.Appearance);
    }

    [Fact]
    public void Duplicate_profile_and_per_profile_guide_ids_are_rejected()
    {
        var document = Document();
        document["profiles"]!.AsArray().Add(document["profiles"]![0]!.DeepClone());
        Reject(document);
        document = Document();
        var guides = document["profiles"]![0]!["profile"]!["nestedFrames"]!.AsArray();
        guides.Add(guides[0]!.DeepClone());
        Reject(document);
    }

    [Fact]
    public void Guide_ids_may_repeat_in_different_saved_profiles()
    {
        var first = Profile();
        var second = first with { Id = "second", Name = "Second" };
        var parsed = ProfilePackageSerializer.Parse(ProfilePackageSerializer.Export([first, second], [Source]));
        Assert.Equal(2, parsed.Profiles.Count);
    }

    [Fact]
    public void List_limits_and_names_are_bounded()
    {
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Export([], [Source]));
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Export(
            Enumerable.Range(0, 101).Select(index => Profile() with { Id = "profile-" + index }).ToArray(), [Source]));
        var document = Document();
        var profiles = document["profiles"]!.AsArray();
        var template = profiles[0]!.DeepClone();
        for (int i = 1; i < 101; i++)
        {
            var copy = template.DeepClone(); copy["profile"]!["id"] = "profile-" + i; profiles.Add(copy);
        }
        Reject(document);
        document = Document();
        var guides = document["profiles"]![0]!["profile"]!["nestedFrames"]!.AsArray();
        template = guides[0]!.DeepClone();
        for (int i = 1; i < 17; i++)
        {
            var copy = template.DeepClone(); copy["id"] = "guide-" + i; guides.Add(copy);
        }
        Reject(document);
        document = Document();
        document["profiles"]![0]!["profile"]!["name"] = new string('x', 121);
        Reject(document);
    }

    [Fact]
    public void File_limit_counts_utf8_bytes_and_depth_is_bounded()
    {
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Parse(new string(' ', ProfilePackageSerializer.MaxFileBytes + 1)));
        string unicode = new('界', ProfilePackageSerializer.MaxFileBytes / 2);
        Assert.True(unicode.Length < ProfilePackageSerializer.MaxFileBytes);
        Assert.True(Encoding.UTF8.GetByteCount(unicode) > ProfilePackageSerializer.MaxFileBytes);
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Parse(unicode));
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Parse(new string('[', 30) + "0" + new string(']', 30)));
    }

    [Fact]
    public void Invalid_source_geometry_is_rejected_before_remapping()
    {
        var document = Document();
        document["profiles"]![0]!["sourceDisplay"]!["bounds"]!["width"] = int.MaxValue;
        Reject(document);
        document = Document();
        document["profiles"]![0]!["sourceDisplay"]!["workArea"]!["x"] = 100;
        Reject(document);
        document = Document();
        document["profiles"]![0]!["sourceDisplay"]!["dpi"] = 0;
        Reject(document);
    }

    [Fact]
    public void Remap_revalidates_manually_constructed_packages_and_target_geometry()
    {
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Remap(
            Package(Profile() with { Width = 0 }), Mapping(), [Target]));
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Remap(
            Package(Profile()), Mapping(), [Target with { Dpi = 0 }]));
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Remap(
            Package(Profile()), Mapping(), [Target, Target with { DeviceName = Target.DeviceName.ToLowerInvariant() }]));
    }

    private static FrameProfile Profile() => new()
    {
        Id = "example-profile", Name = "Camera and slides", Width = 800, Height = 600,
        Anchor = null, X = -3740, Y = -10, DisplayDeviceName = Source.DeviceName,
        KeepClearOfTaskbar = true, IsAspectLocked = false,
        NestedFrames = [new() { Id = "portrait-guide", Name = "Portrait", AspectWidth = 9, AspectHeight = 16,
            ScalePercent = 75, Anchor = FrameAnchor.Right, Color = OutlineColor.Amber }]
    };

    private static ProfilePackage Package(FrameProfile profile) => new()
    { Profiles = [new() { Profile = profile, SourceDisplay = Source }] };

    private static Dictionary<string, string> Mapping() => new() { [Source.DeviceName] = Target.DeviceName };

    private static JsonNode Document(FrameProfile? profile = null) =>
        JsonNode.Parse(ProfilePackageSerializer.Export([profile ?? Profile()], [Source]))!;

    private static void Reject(JsonNode document) =>
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Parse(document.ToJsonString()));
}
