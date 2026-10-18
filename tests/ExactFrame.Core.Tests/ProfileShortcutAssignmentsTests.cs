using System.Text.Json;
using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;
using ExactFrame.Core.Settings;

namespace ExactFrame.Core.Tests;

public sealed class ProfileShortcutAssignmentsTests
{
    [Fact]
    public void Legacy_shortcuts_are_migrated_in_list_order_once_with_only_nine_assignments()
    {
        var settings = new AppSettings
        {
            Profiles = [.. Enumerable.Range(1, 12).Select(number => new FrameProfile
                { Id = $"profile-{number}", Name = $"Profile {number}", HotkeySlot = 9 })]
        };
        var originalIds = settings.Profiles.Select(profile => profile.Id).ToArray();

        Assert.True(ProfileShortcutAssignments.EnsureMigrated(settings));

        Assert.Equal(1, settings.ProfileShortcutVersion);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0, 0], settings.Profiles.Select(profile => profile.HotkeySlot));
        Assert.Equal(originalIds, settings.Profiles.Select(profile => profile.Id));
        Assert.False(ProfileShortcutAssignments.EnsureMigrated(settings));
    }

    [Fact]
    public void Missing_legacy_json_fields_receive_backwards_compatible_defaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {"Profiles":[{"Id":"old","Name":"Old frame","Width":1280,"Height":720}]}
            """)!;

        Assert.Equal(0, settings.ProfileShortcutVersion);
        Assert.True(settings.Profiles[0].IsAspectLocked);
        Assert.Null(settings.Profiles[0].Appearance);
        Assert.Null(settings.Profiles[0].DisplaySnapshot);
        Assert.True(ProfileShortcutAssignments.EnsureMigrated(settings));
        Assert.Equal(1, settings.Profiles[0].HotkeySlot);
    }

    [Fact]
    public void Deleting_and_reordering_profiles_never_changes_surviving_slots()
    {
        var settings = new AppSettings();
        ProfileShortcutAssignments.EnsureMigrated(settings);
        string thirdId = settings.Profiles[2].Id;
        settings.Profiles.RemoveAt(0);
        settings.Profiles.Reverse();

        Assert.False(ProfileShortcutAssignments.EnsureMigrated(settings));
        Assert.Equal(3, settings.Profiles.Single(profile => profile.Id == thirdId).HotkeySlot);
        Assert.Equal([3, 2], settings.Profiles.Select(profile => profile.HotkeySlot));
        Assert.Equal(1, ProfileShortcutAssignments.FirstFree(settings.Profiles));
    }

    [Fact]
    public void Later_loads_keep_unassigned_profiles_and_holes_instead_of_refilling_them()
    {
        var settings = new AppSettings
        {
            ProfileShortcutVersion = 1,
            Profiles = [new() { Id = "none", HotkeySlot = 0 }, new() { Id = "third", HotkeySlot = 3 }]
        };

        Assert.False(ProfileShortcutAssignments.EnsureMigrated(settings));
        Assert.Equal([0, 3], settings.Profiles.Select(profile => profile.HotkeySlot));
        Assert.Equal(1, ProfileShortcutAssignments.FirstFree(settings.Profiles));
    }

    [Fact]
    public void Invalid_and_duplicate_assignments_are_cleared_while_the_first_valid_owner_wins()
    {
        var settings = new AppSettings
        {
            ProfileShortcutVersion = 1,
            Profiles = [.. new[] { 1, 1, -1, 10, 9, 0, 9 }.Select((slot, index) =>
                new FrameProfile { Id = $"p-{index}", HotkeySlot = slot })]
        };

        Assert.True(ProfileShortcutAssignments.EnsureMigrated(settings));
        Assert.Equal([1, 0, 0, 0, 9, 0, 0], settings.Profiles.Select(profile => profile.HotkeySlot));
        Assert.False(ProfileShortcutAssignments.EnsureMigrated(settings));
        Assert.Equal(2, ProfileShortcutAssignments.FirstFree(settings.Profiles));
    }

    [Fact]
    public void Later_schema_markers_are_not_downgraded_or_treated_as_legacy()
    {
        var settings = new AppSettings { ProfileShortcutVersion = 2, Profiles = [new() { HotkeySlot = 7 }] };

        Assert.False(ProfileShortcutAssignments.EnsureMigrated(settings));
        Assert.Equal(2, settings.ProfileShortcutVersion);
        Assert.Equal(7, settings.Profiles[0].HotkeySlot);
    }

    [Fact]
    public void All_nine_occupied_slots_leave_new_profiles_without_a_numbered_shortcut()
    {
        var profiles = Enumerable.Range(1, 9).Select(slot => new FrameProfile { HotkeySlot = slot }).ToArray();

        Assert.Equal(0, ProfileShortcutAssignments.FirstFree(profiles));
        Assert.Equal(1, ProfileShortcutAssignments.FirstFree([]));
        Assert.Equal(1, ProfileShortcutAssignments.FirstFree([new() { HotkeySlot = -1 }, new() { HotkeySlot = 99 }]));
    }

    [Fact]
    public void Profile_semantic_equality_includes_appearance_lock_slot_and_display_snapshot()
    {
        var original = new FrameProfile { Id = "profile", Name = "Recording" };
        Assert.NotEqual(original, original with { HotkeySlot = 1 });
        Assert.NotEqual(original, original with { IsAspectLocked = false });
        Assert.NotEqual(original, original with { Appearance = new OutlineStyle() });
        Assert.NotEqual(original, original with { DisplaySnapshot = FakeDisplays.Primary });

        var profile = original with
        {
            HotkeySlot = 3,
            IsAspectLocked = false,
            Appearance = new OutlineStyle { Color = OutlineColor.Red },
            DisplaySnapshot = FakeDisplays.Primary
        };
        var copy = profile with { };
        Assert.Equal(profile, copy);
        Assert.Equal(profile.GetHashCode(), copy.GetHashCode());
    }

    [Fact]
    public void Display_snapshots_strip_native_monitor_paths_on_construction_copy_and_deserialization()
    {
        var monitor = FakeDisplays.Primary with { MonitorDevicePath = "private-monitor-interface" };
        var profile = new FrameProfile { DisplaySnapshot = monitor };
        Assert.Null(profile.DisplaySnapshot!.MonitorDevicePath);
        Assert.Equal(monitor.Bounds, profile.DisplaySnapshot.Bounds);
        Assert.Equal(monitor.DeviceName, profile.DisplaySnapshot.DeviceName);
        Assert.Equal(monitor.Dpi, profile.DisplaySnapshot.Dpi);
        Assert.Equal("private-monitor-interface", monitor.MonitorDevicePath);

        var copied = profile with { DisplaySnapshot = monitor };
        Assert.Null(copied.DisplaySnapshot!.MonitorDevicePath);
        string document = JsonSerializer.Serialize(new { DisplaySnapshot = monitor });
        var deserialized = JsonSerializer.Deserialize<FrameProfile>(document)!;
        Assert.Null(deserialized.DisplaySnapshot!.MonitorDevicePath);
        Assert.DoesNotContain("private-monitor-interface", JsonSerializer.Serialize(deserialized));
    }

    [Fact]
    public void New_profile_fields_and_migration_marker_round_trip_without_changing_slot_assignments()
    {
        var settings = new AppSettings
        {
            ProfileShortcutVersion = 1,
            Profiles = [new()
            {
                Id = "portable", HotkeySlot = 7, IsAspectLocked = false,
                Appearance = new OutlineStyle { Color = OutlineColor.Blue, ShowThirds = true },
                DisplaySnapshot = FakeDisplays.Secondary
            }, new() { Id = "unassigned", HotkeySlot = 0 }]
        };

        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;

        Assert.Equal(settings.Profiles, loaded.Profiles);
        Assert.Equal(1, loaded.ProfileShortcutVersion);
        Assert.False(ProfileShortcutAssignments.EnsureMigrated(loaded));
        Assert.Equal([7, 0], loaded.Profiles.Select(profile => profile.HotkeySlot));
    }
}
