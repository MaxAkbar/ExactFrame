using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;
using ExactFrame.Core.Settings;

namespace ExactFrame.Core.Tests;

public sealed class ProfilePersistenceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "exactframe-profile-persistence-" + Guid.NewGuid().ToString("N"));
    private const string PrivateMonitorPath = "private-monitor-interface-path";

    [Fact]
    public void New_profile_fields_survive_reopening_the_real_json_store_without_native_monitor_paths()
    {
        string path = Path.Combine(_folder, "settings.json");
        var expected = Settings();

        new JsonSettingsStore(path).Save(expected);
        var loaded = new JsonSettingsStore(path).Load();

        AssertPortableSettings(expected, loaded);
        Assert.DoesNotContain(PrivateMonitorPath, File.ReadAllText(path));
    }

    [Fact]
    public async Task New_profile_fields_survive_reopening_the_real_database_store()
    {
        string path = Path.Combine(_folder, "exactframe.db");
        var expected = Settings();
        await using (var store = new CSharpDbAppStore(path))
            store.Save(expected);

        await using (var reopened = new CSharpDbAppStore(path))
        {
            var loaded = reopened.Load();
            AssertPortableSettings(expected, loaded);
        }
    }

    private static AppSettings Settings()
    {
        var display = FakeDisplays.Secondary with { MonitorDevicePath = PrivateMonitorPath };
        var saved = new FrameProfile
        {
            Id = "portable-profile", Name = "Portable recording", HotkeySlot = 7,
            Width = 1280, Height = 720, X = 3900, Y = 80, Anchor = null,
            DisplayDeviceName = display.DeviceName, DisplaySnapshot = display,
            IsAspectLocked = false,
            Appearance = new OutlineStyle
            {
                Color = OutlineColor.Red, Thickness = 6, Line = OutlineLine.Corners,
                DimOpacityPercent = 60, ShowThirds = true, ShowSafeArea = true, ShowHud = false
            },
            NestedFrames = [new()
            {
                Id = "nested-guide", Name = "Portrait", AspectWidth = 9, AspectHeight = 16,
                ScalePercent = 60, Anchor = FrameAnchor.Right, Color = OutlineColor.Blue,
                UseCustomPosition = true, PositionXPercent = 28.5, PositionYPercent = 76,
                InsetPixels = 14, SafeMarginPercent = 6, OutputWidth = 1080, OutputHeight = 1920
            }]
        };
        return new AppSettings
        {
            ProfileShortcutVersion = 1,
            Profiles = [saved, new() { Id = "unassigned", Name = "No shortcut", HotkeySlot = 0 }],
            ActiveProfileId = saved.Id,
            Style = new OutlineStyle { Color = OutlineColor.Blue },
            LastFrame = saved with { Id = "last-session", Name = "Last session", HotkeySlot = 0 }
        };
    }

    private static void AssertPortableSettings(AppSettings expected, AppSettings loaded)
    {
        Assert.Equal(1, loaded.ProfileShortcutVersion);
        Assert.Equal(expected.Profiles, loaded.Profiles);
        Assert.Equal(expected.LastFrame, loaded.LastFrame);
        Assert.Equal(expected.Style, loaded.Style);
        Assert.Equal(expected.ActiveProfileId, loaded.ActiveProfileId);
        Assert.False(ProfileShortcutAssignments.EnsureMigrated(loaded));
        Assert.Equal([7, 0], loaded.Profiles.Select(profile => profile.HotkeySlot));
        var saved = loaded.Profiles[0];
        Assert.False(saved.IsAspectLocked);
        Assert.Equal(OutlineColor.Red, saved.Appearance!.Color);
        Assert.True(saved.Appearance.ShowThirds);
        Assert.Equal(FakeDisplays.Secondary, saved.DisplaySnapshot);
        Assert.Null(saved.DisplaySnapshot!.MonitorDevicePath);
        Assert.Null(loaded.LastFrame!.DisplaySnapshot!.MonitorDevicePath);
        Assert.Null(loaded.Profiles[1].Appearance);
        Assert.True(loaded.Profiles[1].IsAspectLocked);
        Assert.Equal("nested-guide", Assert.Single(saved.NestedFrames).Id);
        Assert.Equal(28.5, saved.NestedFrames[0].PositionXPercent);
        Assert.Equal(1920, saved.NestedFrames[0].OutputHeight);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_folder)) return;
        try
        {
            foreach (string file in Directory.EnumerateFiles(_folder)) File.Delete(file);
            Directory.Delete(_folder);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
