using System.Collections.ObjectModel;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

// Saved profiles and global hotkeys.
public sealed partial class MainViewModel
{
    private const int HotkeyToggleOutline = 1;
    private const int HotkeyToggleLock = 2;
    private const int HotkeyNextProfile = 3;
    private const int HotkeyProfileBase = 100;
    private const int ProfileHotkeyCount = 9;

    private readonly HashSet<int> _registeredHotkeys = [];
    private bool _toggleHotkeyRegistered;

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

    private bool IsProfileEdited => ActiveProfile is { } profile && Snapshot(profile.Id, profile.Name) != Normalize(profile);

    private static HotkeyGesture ProfileGesture(int index) => HotkeyGesture.CtrlAlt(0x31 + index);

    private void ApplyProfile(FrameProfile profile)
    {
        if (IsBusy) return;
        if (profile.Mode == FrameMode.Resize) Guard(RefreshWindows);

        ApplyFrameState(profile);
        _settings.ActiveProfileId = profile.Id;
        SaveSettings();
        SelectOnly(Profiles, p => p.Profile.Id == profile.Id);
        UpdateFrame($"Applied “{profile.Name}”.");
    }

    private void ApplyNextProfile()
    {
        if (_settings.Profiles.Count == 0) return;
        int current = _settings.Profiles.FindIndex(p => p.Id == _settings.ActiveProfileId);
        ApplyProfile(_settings.Profiles[(current + 1) % _settings.Profiles.Count]);
    }

    private async Task SaveProfileAsync()
    {
        string size = _preset.IsCustom ? $"{_width} × {_height}" : _preset.Name;
        string suggestion = IsResizeMode && _targetWindow is not null
            ? $"{_targetWindow.ProcessName} {size}"
            : $"{size}, {AnchorName.ToLowerInvariant()}";

        string? name = (await _dialogs.PromptProfileNameAsync(suggestion))?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        int existing = _settings.Profiles.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var profile = Snapshot(existing >= 0 ? _settings.Profiles[existing].Id : Guid.NewGuid().ToString("N"), name);
        if (existing >= 0) _settings.Profiles[existing] = profile;
        else _settings.Profiles.Add(profile);

        _settings.ActiveProfileId = profile.Id;
        SaveSettings();
        RegisterHotkeys();
        RebuildProfiles();

        int index = _settings.Profiles.FindIndex(p => p.Id == profile.Id);
        string hint = index < ProfileHotkeyCount ? $" Press {ProfileGesture(index)} to apply it from any app." : string.Empty;
        SetStatus((existing >= 0 ? $"Updated “{name}”." : $"Saved “{name}”.") + hint);
    }

    private async Task DeleteProfileAsync(FrameProfile profile)
    {
        bool confirmed = await _dialogs.ConfirmAsync("Delete profile?", $"“{profile.Name}” will be removed. This can’t be undone.", "Delete");
        if (!confirmed) return;

        _settings.Profiles.RemoveAll(p => p.Id == profile.Id);
        if (_settings.ActiveProfileId == profile.Id) _settings.ActiveProfileId = null;
        SaveSettings();
        RegisterHotkeys();
        RebuildProfiles();
        SetStatus($"Deleted “{profile.Name}”.");
    }

    private void RebuildProfiles()
    {
        Profiles.Clear();
        for (int i = 0; i < _settings.Profiles.Count; i++)
        {
            var profile = _settings.Profiles[i];
            bool hasKeys = i < ProfileHotkeyCount;
            Profiles.Add(new ProfileItem(
                profile,
                DescribeProfile(profile),
                hasKeys ? ProfileGesture(i).Keys : [],
                hasKeys && _registeredHotkeys.Contains(HotkeyProfileBase + i),
                () => ApplyProfile(profile),
                () => DeleteProfileAsync(profile))
            {
                IsSelected = profile.Id == _settings.ActiveProfileId
            });
        }
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasNoProfiles));
        NotifyProfileHeader();
    }

    private string DescribeProfile(FrameProfile profile)
    {
        string size = $"{profile.Width} × {profile.Height}";
        if (profile.Mode == FrameMode.Resize)
        {
            string target = profile.TargetProcessName ?? "a window";
            return $"Resize {target} · {size} {AreaLabel(profile.Area)}";
        }

        string display = profile.DisplayDeviceName is null
            ? _displays.FirstOrDefault(d => d.IsPrimary)?.Title ?? "Primary display"
            : _displays.FirstOrDefault(d => d.DeviceName == profile.DisplayDeviceName)?.Title ?? "Disconnected display";
        string position = profile.Anchor?.DisplayName() ?? $"At {profile.X}, {profile.Y}";
        string nested = profile.NestedFrames is { Count: > 0 } frames
            ? $" · {frames.Count} extra {(frames.Count == 1 ? "frame" : "frames")}" : string.Empty;
        return $"{size} · {position} · {display}{nested}";
    }

    private void NotifyProfileHeader()
    {
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(ProfileEyebrow));
    }

    // ---- Hotkeys --------------------------------------------------------------------------------

    private void RegisterHotkeys()
    {
        foreach (int id in _registeredHotkeys) _hotkeys.Unregister(id);
        _registeredHotkeys.Clear();

        _toggleHotkeyRegistered = TryRegister(HotkeyToggleOutline, _settings.ToggleOutlineHotkey);
        bool lockRegistered = TryRegister(HotkeyToggleLock, _settings.ToggleLockHotkey);
        bool nextRegistered = TryRegister(HotkeyNextProfile, _settings.NextProfileHotkey);
        for (int i = 0; i < Math.Min(ProfileHotkeyCount, _settings.Profiles.Count); i++)
            TryRegister(HotkeyProfileBase + i, ProfileGesture(i));

        Hotkeys.Clear();
        Hotkeys.Add(new HotkeyItem("Show or hide outline", _settings.ToggleOutlineHotkey, _toggleHotkeyRegistered,
            () => ChangeHotkeyAsync(HotkeyToggleOutline)));
        Hotkeys.Add(new HotkeyItem("Lock or unlock click-through", _settings.ToggleLockHotkey, lockRegistered,
            () => ChangeHotkeyAsync(HotkeyToggleLock)));
        Hotkeys.Add(new HotkeyItem("Next profile", _settings.NextProfileHotkey, nextRegistered,
            () => ChangeHotkeyAsync(HotkeyNextProfile)));
        OnPropertyChanged(nameof(ClickThroughHint));
        OnPropertyChanged(nameof(LockHotkeyKeys));
        NotifyFooter();
    }

    private bool TryRegister(int id, HotkeyGesture gesture)
    {
        if (!gesture.IsValid || !_hotkeys.Register(id, gesture)) return false;
        _registeredHotkeys.Add(id);
        return true;
    }

    private async Task ChangeHotkeyAsync(int id)
    {
        var (label, current) = id switch
        {
            HotkeyToggleOutline => ("Show or hide outline", _settings.ToggleOutlineHotkey),
            HotkeyToggleLock => ("Lock or unlock click-through", _settings.ToggleLockHotkey),
            _ => ("Next profile", _settings.NextProfileHotkey)
        };

        var recorded = await _dialogs.RecordHotkeyAsync(label, current);
        if (recorded is not { IsValid: true } gesture) return;

        switch (id)
        {
            case HotkeyToggleOutline: _settings.ToggleOutlineHotkey = gesture; break;
            case HotkeyToggleLock: _settings.ToggleLockHotkey = gesture; break;
            default: _settings.NextProfileHotkey = gesture; break;
        }

        SaveSettings();
        RegisterHotkeys();
        RebuildProfiles();

        bool registered = _registeredHotkeys.Contains(id);
        SetStatus(registered
            ? $"{label}: {gesture}."
            : $"Windows couldn’t register {gesture}. Another app may be using it; try a different shortcut.",
            warning: !registered);
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
                ApplyNextProfile();
                break;
            default:
                int index = id - HotkeyProfileBase;
                if (index >= 0 && index < _settings.Profiles.Count) ApplyProfile(_settings.Profiles[index]);
                break;
        }
    }
}
