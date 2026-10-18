using System.Collections.ObjectModel;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;

namespace ExactFrame.Core.ViewModels;

// Saved profiles and global hotkeys.
public sealed partial class MainViewModel
{
    private const int HotkeyToggleOutline = 1;
    private const int HotkeyToggleLock = 2;
    private const int HotkeyNextProfile = 3;
    private const int HotkeyProfileKeys = 4;
    private const int HotkeyProfileBase = 100;
    private const int ProfileHotkeyCount = 9;
    private const int VkOne = 0x31;

    /// <summary>A shortcut ExactFrame didn't register. <see cref="Character"/> is set when it would type one.</summary>
    private sealed record HotkeyFailure(HotkeyGesture Gesture, string? Character);

    private readonly HashSet<int> _registeredHotkeys = [];
    private readonly Dictionary<int, HotkeyFailure> _hotkeyFailures = [];
    private bool _toggleHotkeyRegistered;
    private bool _includeProfileAppearance;

    public bool IncludeProfileAppearance
    {
        get => _includeProfileAppearance;
        set
        {
            if (SetProperty(ref _includeProfileAppearance, value)) NotifyProfileHeader();
        }
    }

    public ObservableCollection<ProfileItem> Profiles { get; } = [];

    public ObservableCollection<HotkeyItem> Hotkeys { get; } = [];

    public string ClickThroughHint => _settings.ToggleLockHotkey.IsValid
        ? $"Clicks pass to the app underneath. Shortcut: {_settings.ToggleLockHotkey}."
        : "Clicks pass to the app underneath.";

    public IReadOnlyList<string> LockHotkeyKeys => _settings.ToggleLockHotkey.Keys;

    public bool HasProfiles => Profiles.Count > 0;

    public bool HasNoProfiles => Profiles.Count == 0;

    public string ActiveProfileName => ActiveProfile?.Name ?? "No profile";

    public string ProfileEyebrow => ActiveProfile is null ? "Profile" : IsProfileEdited ? "Profile · edited" : "Profile";

    private FrameProfile? ActiveProfile => _settings.Profiles.FirstOrDefault(p => p.Id == _settings.ActiveProfileId);

    private bool IsProfileEdited => ActiveProfile is { } profile &&
        (Snapshot(profile.Id, profile.Name) with { DisplaySnapshot = profile.DisplaySnapshot }) != Normalize(profile);

    private HotkeyGesture ProfileGesture(int index) => new(_settings.ProfileHotkeyModifiers, VkOne + index);

    /// <summary>The profile keys as one row, such as Ctrl, Alt, 1 … 9.</summary>
    private IReadOnlyList<string> ProfileKeys => [.. ProfileGesture(0).Keys.SkipLast(1), "1 … 9"];

    private async Task ApplyProfileAsync(FrameProfile profile)
    {
        if (IsBusy) return;
        if (profile.DisplayDeviceName is { } device && !_displays.Any(d => d.DeviceName == device))
        {
            SetStatus($"The display saved for “{profile.Name}” is disconnected. Export and import this profile to choose a connected display.", warning: true);
            return;
        }
        bool resize = profile.Mode == FrameMode.Resize;
        if (resize) Guard(RefreshWindows);

        ApplyFrameState(profile);
        _settings.ActiveProfileId = profile.Id;
        SelectOnly(Profiles, p => p.Profile.Id == profile.Id);
        if (!resize)
        {
            UpdateFrame($"Applied “{profile.Name}”.");
            SaveSettings();
            return;
        }

        // A Resize window profile resizes its app straight away, so its shortcut works from any app.
        UpdateFrame();
        if (_targetWindow is null)
        {
            bool missing = !string.IsNullOrEmpty(profile.TargetProcessName);
            bool ambiguous = MatchingWindows.Count > 1;
            SetStatus(TargetMatchError.Length > 0 ? TargetMatchError : ambiguous
                ? $"“{profile.Name}” matches {MatchingWindows.Count} windows. Select one under Match this application, then choose Resize window."
                : missing && profile.TargetMatch is not null
                ? $"No window matches “{profile.Name}”. Review the matching conditions, open the intended window, then refresh."
                : missing
                ? $"“{profile.Name}” resizes {profile.TargetProcessName}, which isn’t open. Open it, then apply the profile again."
                : $"Applied “{profile.Name}”. Choose a window to resize.", warning: missing);
            SaveSettings();
            return;
        }
        await RunBusyAsync(() => ResizeTargetAsync(ResizeReason.Profile, profile.Name));
        SaveSettings();
    }

    private Task ApplyNextProfileAsync()
    {
        if (_settings.Profiles.Count == 0) return Task.CompletedTask;
        int current = _settings.Profiles.FindIndex(p => p.Id == _settings.ActiveProfileId);
        return ApplyProfileAsync(_settings.Profiles[(current + 1) % _settings.Profiles.Count]);
    }

    private async Task SaveProfileAsync()
    {
        if (!ValidateTargetMatchForSave()) return;
        string size = _preset.IsCustom ? $"{_width} × {_height}" : _preset.Name;
        string suggestion = IsResizeMode && _targetWindow is not null
            ? $"{_targetWindow.ProcessName} {size}"
            : $"{size}, {AnchorName.ToLowerInvariant()}";

        string? name = (await _dialogs.PromptProfileNameAsync(suggestion))?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        if (!ValidateProfileName(name)) return;
        var profile = Snapshot(Guid.NewGuid().ToString("N"), name) with
        {
            HotkeySlot = ProfileShortcutAssignments.FirstFree(_settings.Profiles)
        };
        if (!TrySaveProfiles([.. _settings.Profiles, profile], profile.Id)) return;
        if (profile.Appearance is null) ApplyEffectiveStyle(_settings.Style);
        string hint = profile.HotkeySlot > 0 && _registeredHotkeys.Contains(HotkeyProfileBase + profile.HotkeySlot - 1)
            ? $" Press {ProfileGesture(profile.HotkeySlot - 1)} to apply it from any app." : string.Empty;
        SetStatus($"Saved “{name}”." + hint);
    }

    private bool ValidateProfileName(string name, string? exceptId = null)
    {
        if (name.Length > 60)
        {
            SetStatus("Use a profile name of 60 characters or fewer.", warning: true);
            return false;
        }
        if (_settings.Profiles.Any(p => p.Id != exceptId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"A profile named “{name}” already exists. Choose another name, or use Update with current setup.", warning: true);
            return false;
        }
        return true;
    }

    private bool TrySaveProfiles(List<FrameProfile> profiles, string? activeId)
    {
        var previous = _settings.Profiles;
        string? previousActive = _settings.ActiveProfileId;
        _settings.Profiles = profiles;
        _settings.ActiveProfileId = activeId;
        if (!SaveSettings())
        {
            _settings.Profiles = previous;
            _settings.ActiveProfileId = previousActive;
            return false;
        }
        RegisterHotkeys();
        RebuildProfiles();
        return true;
    }

    private async Task UpdateProfileAsync(FrameProfile profile)
    {
        if (!ValidateTargetMatchForSave()) return;
        string appearance = IncludeProfileAppearance ? "including the current appearance" : "using the default appearance";
        if (!await _dialogs.ConfirmAsync("Update profile?",
            $"Replace “{profile.Name}” with the current setup, {appearance}? Its name and shortcut will stay the same.", "Update")) return;
        var updated = Snapshot(profile.Id, profile.Name);
        if (!TrySaveProfiles(_settings.Profiles.Select(p => p.Id == profile.Id ? updated : p).ToList(), profile.Id)) return;
        if (updated.Appearance is null) ApplyEffectiveStyle(_settings.Style);
        SetStatus($"Updated “{profile.Name}”.");
    }

    private async Task RenameProfileAsync(FrameProfile profile)
    {
        string? name = (await _dialogs.PromptProfileNameAsync(profile.Name, "Rename profile", "Rename"))?.Trim();
        if (string.IsNullOrEmpty(name) || !ValidateProfileName(name, profile.Id)) return;
        if (TrySaveProfiles(_settings.Profiles.Select(p => p.Id == profile.Id ? p with { Name = name } : p).ToList(), _settings.ActiveProfileId))
            SetStatus($"Renamed profile to “{name}”.");
    }

    private async Task DuplicateProfileAsync(FrameProfile profile)
    {
        string? name = (await _dialogs.PromptProfileNameAsync(UniqueProfileName(profile.Name + " copy",
            _settings.Profiles.Select(p => p.Name)), "Duplicate profile", "Duplicate"))?.Trim();
        if (string.IsNullOrEmpty(name) || !ValidateProfileName(name)) return;
        var copy = profile with
        {
            Id = Guid.NewGuid().ToString("N"), Name = name, HotkeySlot = 0,
            NestedFrames = [.. profile.NestedFrames.Select(f => f with { Id = Guid.NewGuid().ToString("N") })]
        };
        if (TrySaveProfiles([.. _settings.Profiles, copy], _settings.ActiveProfileId))
            SetStatus($"Duplicated “{profile.Name}” as “{name}”. Choose a shortcut in More actions if needed.");
    }

    private async Task ChooseProfileShortcutAsync(FrameProfile profile)
    {
        int? chosen = await _dialogs.ChooseProfileShortcutAsync(profile, _settings.Profiles);
        if (chosen is not int slot || slot < 0 || slot > ProfileHotkeyCount || slot == profile.HotkeySlot) return;
        var owner = slot == 0 ? null : _settings.Profiles.FirstOrDefault(p => p.Id != profile.Id && p.HotkeySlot == slot);
        if (owner is not null && !await _dialogs.ConfirmAsync("Reassign shortcut?",
            $"{ProfileGesture(slot - 1)} belongs to “{owner.Name}”. Assign it to “{profile.Name}”? “{owner.Name}” will have no numbered shortcut.", "Reassign")) return;
        var next = _settings.Profiles.Select(p => p.Id == profile.Id ? p with { HotkeySlot = slot }
            : p.Id == owner?.Id ? p with { HotkeySlot = 0 } : p).ToList();
        if (!TrySaveProfiles(next, _settings.ActiveProfileId)) return;
        bool failed = slot > 0 && !_registeredHotkeys.Contains(HotkeyProfileBase + slot - 1);
        SetStatus(slot == 0 ? $"Removed the numbered shortcut from “{profile.Name}”."
            : failed ? $"Assigned {ProfileGesture(slot - 1)} to “{profile.Name}”, but Windows couldn’t register it. Check the shortcut warning."
            : $"“{profile.Name}” now uses {ProfileGesture(slot - 1)}.", warning: failed);
    }

    private async Task DeleteProfileAsync(FrameProfile profile)
    {
        bool confirmed = await _dialogs.ConfirmAsync("Delete profile?", $"“{profile.Name}” will be removed. This can’t be undone.", "Delete");
        if (!confirmed) return;

        if (TrySaveProfiles(_settings.Profiles.Where(p => p.Id != profile.Id).ToList(),
            _settings.ActiveProfileId == profile.Id ? null : _settings.ActiveProfileId))
            SetStatus($"Deleted “{profile.Name}”.");
    }

    private void RebuildProfiles()
    {
        Profiles.Clear();
        foreach (var profile in _settings.Profiles)
        {
            int index = profile.HotkeySlot - 1;
            bool hasKeys = index >= 0 && index < ProfileHotkeyCount;
            Profiles.Add(new ProfileItem(
                profile,
                DescribeProfile(profile),
                hasKeys ? ProfileGesture(index).Keys : [],
                hasKeys && _hotkeyFailures.TryGetValue(HotkeyProfileBase + index, out var failure) ? ProfileProblem(failure) : null,
                () => ApplyProfileAsync(profile),
                () => RunBusyAsync(() => DeleteProfileAsync(profile)),
                () => RunBusyAsync(() => UpdateProfileAsync(profile)),
                () => RunBusyAsync(() => RenameProfileAsync(profile)),
                () => RunBusyAsync(() => DuplicateProfileAsync(profile)),
                () => RunBusyAsync(() => ExportProfilesAsync(profile)),
                () => RunBusyAsync(() => ChooseProfileShortcutAsync(profile)),
                _profileTransfer is not null)
            {
                IsSelected = profile.Id == _settings.ActiveProfileId
            });
        }
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasNoProfiles));
        ExportProfilesCommand.NotifyCanExecuteChanged();
        NotifyProfileHeader();
    }

    private string DescribeProfile(FrameProfile profile)
    {
        string size = $"{profile.Width} × {profile.Height}";
        string appearance = profile.Appearance is null ? string.Empty : " · Saved appearance";
        if (profile.Mode == FrameMode.Resize)
        {
            string target = profile.TargetProcessName ?? "a window";
            string match = profile.TargetMatch is null ? string.Empty : " · Window conditions";
            return $"Resize {target} · {size} {AreaLabel(profile.Area)}{match}{appearance}";
        }

        string display = profile.DisplayDeviceName is null
            ? _displays.FirstOrDefault(d => d.IsPrimary)?.Title ?? "Primary display"
            : _displays.FirstOrDefault(d => d.DeviceName == profile.DisplayDeviceName)?.Title ?? "Disconnected display";
        string position = profile.Anchor?.DisplayName() ?? $"At {profile.X}, {profile.Y}";
        string nested = profile.NestedFrames is { Count: > 0 } frames
            ? $" · {frames.Count} extra {(frames.Count == 1 ? "frame" : "frames")}" : string.Empty;
        return $"{size} · {position} · {display}{nested}{appearance}";
    }

    private void NotifyProfileHeader()
    {
        OnPropertyChanged(nameof(HasActiveResizeProfile));
        UpdateTargetProfileCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(ProfileEyebrow));
    }

    // ---- Hotkeys --------------------------------------------------------------------------------

    private void RegisterHotkeys()
    {
        foreach (int id in _registeredHotkeys) _hotkeys.Unregister(id);
        _registeredHotkeys.Clear();
        _hotkeyFailures.Clear();

        _toggleHotkeyRegistered = TryRegister(HotkeyToggleOutline, _settings.ToggleOutlineHotkey);
        TryRegister(HotkeyToggleLock, _settings.ToggleLockHotkey);
        TryRegister(HotkeyNextProfile, _settings.NextProfileHotkey);
        foreach (var profile in _settings.Profiles.Where(p => p.HotkeySlot is >= 1 and <= ProfileHotkeyCount))
            TryRegister(HotkeyProfileBase + profile.HotkeySlot - 1, ProfileGesture(profile.HotkeySlot - 1));

        Hotkeys.Clear();
        Hotkeys.Add(HotkeyRow(HotkeyToggleOutline, "Show or hide outline", _settings.ToggleOutlineHotkey));
        Hotkeys.Add(HotkeyRow(HotkeyToggleLock, "Lock or unlock click-through", _settings.ToggleLockHotkey));
        Hotkeys.Add(HotkeyRow(HotkeyNextProfile, "Next profile", _settings.NextProfileHotkey));
        Hotkeys.Add(new HotkeyItem("Apply profile 1 to 9", ProfileKeys, ProfileKeysProblem(),
            () => ChangeHotkeyAsync(HotkeyProfileKeys)));
        OnPropertyChanged(nameof(ClickThroughHint));
        OnPropertyChanged(nameof(LockHotkeyKeys));
        NotifyFooter();
    }

    private HotkeyItem HotkeyRow(int id, string label, HotkeyGesture gesture) => new(label, gesture.Keys,
        _hotkeyFailures.TryGetValue(id, out var failure) ? Describe(failure) : null, () => ChangeHotkeyAsync(id));

    /// <summary>Registers a shortcut unless it would type a character or another app owns it.</summary>
    private bool TryRegister(int id, HotkeyGesture gesture)
    {
        string? character = gesture.IsValid ? _hotkeys.TypedCharacter(gesture) : null;
        if (!gesture.IsValid || character is not null || !_hotkeys.Register(id, gesture))
        {
            _hotkeyFailures[id] = new HotkeyFailure(gesture, character);
            return false;
        }
        _registeredHotkeys.Add(id);
        return true;
    }

    private static string Describe(HotkeyFailure failure) =>
        !failure.Gesture.IsValid ? "Not set"
        : failure.Character is { } character ? $"Types “{character}” on your keyboard"
        : "Taken by another app";

    private static string ProfileProblem(HotkeyFailure failure) => failure.Character is { } character
        ? $"Not used: {failure.Gesture} types “{character}” on your keyboard. Change the keys for Apply profile 1 to 9."
        : "Windows couldn’t register this shortcut; another app may be using it.";

    private string? ProfileKeysProblem()
    {
        if (!ProfileGesture(0).IsValid) return "Not set";
        var failures = _settings.Profiles.Where(p => p.HotkeySlot is >= 1 and <= ProfileHotkeyCount)
            .Select(p => _hotkeyFailures.GetValueOrDefault(HotkeyProfileBase + p.HotkeySlot - 1))
            .OfType<HotkeyFailure>()
            .ToList();
        if (failures.Count == 0) return null;
        if (failures.Count == 1)
        {
            return failures[0].Character is { } character
                ? $"{failures[0].Gesture} types “{character}” on your keyboard"
                : $"{failures[0].Gesture} is taken by another app";
        }
        return failures.All(f => f.Character is not null) ? $"{failures.Count} of these type characters on your keyboard"
            : failures.All(f => f.Character is null) ? $"{failures.Count} of these are taken by other apps"
            : $"{failures.Count} of these can’t be used";
    }

    private async Task ChangeHotkeyAsync(int id)
    {
        if (id == HotkeyProfileKeys)
        {
            await ChangeProfileKeysAsync();
            return;
        }

        var (label, current) = id switch
        {
            HotkeyToggleOutline => ("Show or hide outline", _settings.ToggleOutlineHotkey),
            HotkeyToggleLock => ("Lock or unlock click-through", _settings.ToggleLockHotkey),
            _ => ("Next profile", _settings.NextProfileHotkey)
        };

        var recorded = await _dialogs.RecordHotkeyAsync(label, current);
        if (recorded is not { IsValid: true } gesture) return;
        if (_hotkeys.TypedCharacter(gesture) is { } character)
        {
            SetStatus($"{gesture} types “{character}” on your keyboard, so it would stop you typing it. Choose different keys.",
                warning: true);
            return;
        }

        switch (id)
        {
            case HotkeyToggleOutline: _settings.ToggleOutlineHotkey = gesture; break;
            case HotkeyToggleLock: _settings.ToggleLockHotkey = gesture; break;
            default: _settings.NextProfileHotkey = gesture; break;
        }

        if (!SaveSettings())
        {
            switch (id)
            {
                case HotkeyToggleOutline: _settings.ToggleOutlineHotkey = current; break;
                case HotkeyToggleLock: _settings.ToggleLockHotkey = current; break;
                default: _settings.NextProfileHotkey = current; break;
            }
            return;
        }
        RegisterHotkeys();
        RebuildProfiles();

        bool registered = _registeredHotkeys.Contains(id);
        SetStatus(registered
            ? $"{label}: {gesture}."
            : $"Windows couldn’t register {gesture}. Another app may be using it; try a different shortcut.",
            warning: !registered);
    }

    /// <summary>Records the keys held with 1 to 9. Only the modifiers of the recorded shortcut are used.</summary>
    private async Task ChangeProfileKeysAsync()
    {
        var recorded = await _dialogs.RecordHotkeyAsync("Apply profile 1 to 9", ProfileGesture(0),
            "Press the keys to hold with a number, such as Ctrl + Shift + 1. Use Ctrl, Alt or Win.");
        if (recorded is not { IsValid: true } gesture) return;

        for (int i = 0; i < ProfileHotkeyCount; i++)
        {
            var candidate = new HotkeyGesture(gesture.Modifiers, VkOne + i);
            if (_hotkeys.TypedCharacter(candidate) is { } character)
            {
                SetStatus($"{candidate} types “{character}” on your keyboard. Choose other keys for profile shortcuts.", warning: true);
                return;
            }
        }

        var previous = _settings.ProfileHotkeyModifiers;
        _settings.ProfileHotkeyModifiers = gesture.Modifiers;
        if (!SaveSettings())
        {
            _settings.ProfileHotkeyModifiers = previous;
            return;
        }
        RegisterHotkeys();
        RebuildProfiles();

        string? problem = ProfileKeysProblem();
        string keys = string.Join(" + ", ProfileKeys);
        SetStatus(problem is null ? $"Profiles now use {keys}." : $"Profiles now use {keys}. {problem}.", warning: problem is not null);
    }

    private void OnHotkeyPressed(object? sender, int id)
    {
        if (IsBusy) return;
        switch (id)
        {
            case HotkeyToggleOutline:
                Guard(ToggleOutline);
                break;
            case HotkeyToggleLock:
                IsClickThrough = !IsClickThrough;
                break;
            case HotkeyNextProfile:
                _ = ApplyNextProfileAsync();
                break;
            default:
                int slot = id - HotkeyProfileBase + 1;
                if (slot is >= 1 and <= ProfileHotkeyCount && _registeredHotkeys.Contains(id) &&
                    _settings.Profiles.FirstOrDefault(p => p.HotkeySlot == slot) is { } profile)
                    _ = ApplyProfileAsync(profile);
                break;
        }
    }
}
