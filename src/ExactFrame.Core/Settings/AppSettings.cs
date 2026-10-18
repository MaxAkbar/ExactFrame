using ExactFrame.Core.Models;

namespace ExactFrame.Core.Settings;

/// <summary>Everything ExactFrame remembers between sessions.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public OutlineStyle Style { get; set; } = new();

    public bool HideFromRecorders { get; set; } = true;

    public bool SnapToEdges { get; set; } = true;

    public List<FrameProfile> Profiles { get; set; } = [.. FrameProfile.Defaults()];

    /// <summary>0 migrates legacy list-position shortcuts once; 1 uses each profile's persisted numbered slot.</summary>
    public int ProfileShortcutVersion { get; set; }

    public List<RememberedAppSize> RememberedAppSizes { get; set; } = [];

    public string? ActiveProfileId { get; set; } = "youtube-1080p";

    public HotkeyGesture ToggleOutlineHotkey { get; set; } = HotkeyGesture.CtrlAlt(HotkeyGesture.VkF8);

    public HotkeyGesture ToggleLockHotkey { get; set; } = HotkeyGesture.CtrlAlt(HotkeyGesture.VkF9);

    public HotkeyGesture NextProfileHotkey { get; set; } = HotkeyGesture.CtrlAlt(HotkeyGesture.VkF10);

    /// <summary>The keys held with 1 to 9 to apply profiles assigned to those stable numbered slots.</summary>
    public HotkeyModifiers ProfileHotkeyModifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    /// <summary>Closing the main window leaves ExactFrame running in the notification area.</summary>
    public bool KeepRunningInBackground { get; set; } = true;

    /// <summary>Whether the user has been told that closing the window keeps ExactFrame running.</summary>
    public bool BackgroundHintShown { get; set; }

    /// <summary>The frame as it was when ExactFrame last closed.</summary>
    public FrameProfile? LastFrame { get; set; }
}
