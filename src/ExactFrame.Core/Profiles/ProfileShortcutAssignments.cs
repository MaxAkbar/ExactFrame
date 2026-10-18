using ExactFrame.Core.Models;
using ExactFrame.Core.Settings;

namespace ExactFrame.Core.Profiles;

/// <summary>Upgrades positional shortcuts once, then preserves assignments independently of list order.</summary>
public static class ProfileShortcutAssignments
{
    /// <summary>
    /// Returns true when settings changed and should be saved. Legacy settings keep their first-nine shortcuts.
    /// Later loads clear invalid or duplicate slots, keeping the first valid owner and leaving holes unassigned.
    /// </summary>
    public static bool EnsureMigrated(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool changed = false;
        if (settings.Profiles is null)
        {
            settings.Profiles = [];
            changed = true;
        }

        if (settings.ProfileShortcutVersion == 0)
        {
            for (int index = 0; index < settings.Profiles.Count; index++)
            {
                int slot = index < 9 ? index + 1 : 0;
                var profile = settings.Profiles[index];
                if (profile.HotkeySlot != slot) settings.Profiles[index] = profile with { HotkeySlot = slot };
            }
            settings.ProfileShortcutVersion = 1;
            return true;
        }

        var assigned = new bool[10];
        for (int index = 0; index < settings.Profiles.Count; index++)
        {
            var profile = settings.Profiles[index];
            int slot = profile.HotkeySlot;
            if (slot == 0) continue;
            if (slot is >= 1 and <= 9 && !assigned[slot])
            {
                assigned[slot] = true;
                continue;
            }
            settings.Profiles[index] = profile with { HotkeySlot = 0 };
            changed = true;
        }
        return changed;
    }

    /// <summary>Finds the lowest free numbered shortcut without changing existing assignments.</summary>
    public static int FirstFree(IReadOnlyList<FrameProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var assigned = new bool[10];
        foreach (var profile in profiles)
            if (profile.HotkeySlot is >= 1 and <= 9) assigned[profile.HotkeySlot] = true;
        for (int slot = 1; slot <= 9; slot++)
            if (!assigned[slot]) return slot;
        return 0;
    }
}
