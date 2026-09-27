using ExactFrame.Core.Models;

namespace ExactFrame.Core.Settings;

/// <summary>Everything ExactFrame remembers between sessions.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public OutlineStyle Style { get; set; } = new();

    public bool HideFromRecorders { get; set; } = true;

    public List<FrameProfile> Profiles { get; set; } = [.. FrameProfile.Defaults()];

    public string? ActiveProfileId { get; set; } = "youtube-1080p";

    public HotkeyGesture ToggleOutlineHotkey { get; set; } = HotkeyGesture.CtrlAlt(HotkeyGesture.VkF8);

    public HotkeyGesture ToggleLockHotkey { get; set; } = HotkeyGesture.CtrlAlt(HotkeyGesture.VkF9);

    public HotkeyGesture NextProfileHotkey { get; set; } = HotkeyGesture.CtrlAlt(HotkeyGesture.VkF10);

/// <summary>The frame as it was when ExactFrame last closed.</summary>
    public FrameProfile? LastFrame { get; set; }
}
