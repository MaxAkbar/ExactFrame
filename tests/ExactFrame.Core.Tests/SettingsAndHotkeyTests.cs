using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Settings;

namespace ExactFrame.Core.Tests;

public sealed class HotkeyGestureTests
{
    [Fact]
    public void Formats_keys_in_windows_order()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x77);
        Assert.Equal(["Ctrl", "Alt", "Shift", "F8"], gesture.Keys);
        Assert.Equal("Ctrl + Alt + Shift + F8", gesture.ToString());
    }

    [Fact]
    public void Requires_ctrl_alt_or_win()
    {
        Assert.False(new HotkeyGesture(HotkeyModifiers.Shift, 0x41).IsValid);
        Assert.False(new HotkeyGesture(HotkeyModifiers.Control, 0).IsValid);
        Assert.True(HotkeyGesture.CtrlAlt(0x31).IsValid);
    }

    [Theory]
    [InlineData(0x70, "F1")]
    [InlineData(0x79, "F10")]
    [InlineData(0x35, "5")]
    [InlineData(0x4B, "K")]
    [InlineData(0x20, "Space")]
    public void Names_virtual_keys(int virtualKey, string expected) => Assert.Equal(expected, HotkeyGesture.KeyName(virtualKey));
}

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "exactframe-tests-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var settings = new JsonSettingsStore(SettingsPath).Load();
        Assert.Equal(3, settings.Profiles.Count);
        Assert.True(settings.HideFromRecorders);
        Assert.Equal(HotkeyGesture.CtrlAlt(HotkeyGesture.VkF8), settings.ToggleOutlineHotkey);
    }

    [Fact]
    public void Round_trips_every_setting()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var settings = new AppSettings
        {
            Style = new OutlineStyle { Color = OutlineColor.Amber, Thickness = 6, Line = OutlineLine.Corners, ShowThirds = true, DimOpacityPercent = 60 },
            HideFromRecorders = false,
            Profiles = [new FrameProfile { Id = "p1", Name = "Left half", Width = 1280, Height = 720, Anchor = null, X = -1280, Y = 40, DisplayDeviceName = @"\\.\DISPLAY2" }],
            ActiveProfileId = "p1",
            NextProfileHotkey = new HotkeyGesture(HotkeyModifiers.Windows | HotkeyModifiers.Shift, 0x50),
            LastFrame = new FrameProfile { Id = "last", Mode = FrameMode.Resize, Area = WindowArea.Client, TargetProcessName = "devenv", Anchor = FrameAnchor.TopRight }
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(settings.Style, loaded.Style);
        Assert.False(loaded.HideFromRecorders);
        Assert.Equal(settings.Profiles, loaded.Profiles);
        Assert.Equal("p1", loaded.ActiveProfileId);
        Assert.Equal(settings.NextProfileHotkey, loaded.NextProfileHotkey);
        Assert.Equal(settings.LastFrame, loaded.LastFrame);
    }

    [Fact]
    public void Corrupt_file_is_kept_and_defaults_are_used()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ not json");

        var settings = new JsonSettingsStore(SettingsPath).Load();

        Assert.Equal(3, settings.Profiles.Count);
        Assert.True(File.Exists(SettingsPath + ".bad"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
