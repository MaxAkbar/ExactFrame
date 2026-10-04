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
    private const int HotkeyProfileKeys = 4;
    private const int HotkeyProfileBase = 100;
    private const int ProfileHotkeyCount = 9;
    private const int VkOne = 0x31;

    /// <summary>A shortcut ExactFrame didn't register. <see cref="Character"/> is set when it would type one.</summary>
    private sealed record HotkeyFailure(HotkeyGesture Gesture, string? Character);

    private readonly HashSet<int> _registeredHotkeys = [];
    private readonly Dictionary<int, HotkeyFailure> _hotkeyFailures = [];
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

    private HotkeyGesture ProfileGesture(int index) => new(_settings.ProfileHotkeyModifiers, VkOne + index);

    /// <summary>The profile keys as one row, such as Ctrl, Alt, 1 … 9.</summary>
    private IReadOnlyList<string> ProfileKeys => [.. ProfileGesture(0).Keys.SkipLast(1), "1 … 9"];

    private async Task ApplyProfileAsync(FrameProfile profile)
    {
        if (IsBusy) return;
        bool resize = profile.Mode == FrameMode.Resize;
        if (resize) Guard(RefreshWindows);

        ApplyFrameState(profile);
        _settings.ActiveProfileId = profile.Id;
        SaveSettings();
        SelectOnly(Profiles, p => p.Profile.Id == profile.Id);
        if (!resize)
        {
            UpdateFrame($"Applied “{profile.Name}”.");
            return;
        }

        // A Resize window profile resizes its app straight away, so its shortcut works from any app.
        UpdateFrame();
        if (_targetWindow is null)
        {
            bool missing = !string.IsNullOrEmpty(profile.TargetProcessName);
            SetStatus(missing
                ? $"“{profile.Name}” resizes {profile.TargetProcessName}, which isn’t open. Open it, then apply the profile again."
                : $"Applied “{profile.Name}”. Choose a window to resize.", warning: missing);
            return;
        }
        await RunBusyAsync(() => ResizeTargetAsync(ResizeReason.Profile, profile.Name));
    }

    private Task ApplyNextProfileAsync()
    {
        if (_settings.Profiles.Count == 0) return Task.CompletedTask;
        int current = _settings.Profiles.FindIndex(p => p.Id == _settings.ActiveProfileId);
        return ApplyProfileAsync(_settings.Profiles[(current + 1) % _settings.Profiles.Count]);
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
        string hint = index < ProfileHotkeyCount && _registeredHotkeys.Contains(HotkeyProfileBase + index)
            ? $" Press {ProfileGesture(index)} to apply it from any app." : string.Empty;
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
                hasKeys && _hotkeyFailures.TryGetValue(HotkeyProfileBase + i, out var failure) ? ProfileProblem(failure) : null,
                () => ApplyProfileAsync(profile),
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
        _hotkeyFailures.Clear();

        _toggleHotkeyRegistered = TryRegister(HotkeyToggleOutline, _settings.ToggleOutlineHotkey);
        TryRegister(HotkeyToggleLock, _settings.ToggleLockHotkey);
        TryRegister(HotkeyNextProfile, _settings.NextProfileHotkey);
        for (int i = 0; i < Math.Min(ProfileHotkeyCount, _settings.Profiles.Count); i++)
            TryRegister(HotkeyProfileBase + i, ProfileGesture(i));

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
        var failures = Enumerable.Range(0, Math.Min(ProfileHotkeyCount, _settings.Profiles.Count))
            .Select(i => _hotkeyFailures.GetValueOrDefault(HotkeyProfileBase + i))
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

        SaveSettings();
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

        _settings.ProfileHotkeyModifiers = gesture.Modifiers;
        SaveSettings();
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
                int index = id - HotkeyProfileBase;
                if (index >= 0 && index < _settings.Profiles.Count) _ = ApplyProfileAsync(_settings.Profiles[index]);
                break;
        }
    }
}
