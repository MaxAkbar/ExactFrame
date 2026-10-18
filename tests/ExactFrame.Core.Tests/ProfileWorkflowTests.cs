using System.Drawing;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;
using ExactFrame.Core.Services;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class ProfileWorkflowTests : IDisposable
{
    private readonly FakeDisplays _displays = new();
    private readonly FakeWindows _windows = new();
    private readonly FakeOverlay _overlay = new();
    private readonly FakeHotkeys _hotkeys = new();
    private readonly WorkflowDialogs _dialogs = new();
    private readonly WorkflowTransfer _transfer = new();
    private readonly WorkflowStore _store = new();
    private MainViewModel? _vm;

    private MainViewModel Create(AppSettings? settings = null)
    {
        if (settings is not null) _store.Settings = settings;
        _vm = new MainViewModel(_displays, _windows, new FakePicker(), _overlay, _hotkeys,
            new FakeClipboard(), _dialogs, _store, new FakeStartup(), profileTransfer: _transfer);
        _vm.Initialize();
        return _vm;
    }

    public void Dispose() => _vm?.Dispose();

    [Fact]
    public async Task Deleting_an_earlier_legacy_profile_keeps_surviving_numbered_hotkeys_and_dispatch()
    {
        var vm = Create();
        Assert.Equal([1, 2, 3], _store.Settings.Profiles.Select(profile => profile.HotkeySlot));
        string secondId = vm.Profiles[1].Profile.Id;
        string thirdId = vm.Profiles[2].Profile.Id;

        await vm.Profiles[0].DeleteCommand.ExecuteAsync(null);

        Assert.Equal([2, 3], _store.Settings.Profiles.Select(profile => profile.HotkeySlot));
        Assert.Equal(["Ctrl", "Alt", "2"], vm.Profiles[0].Keys);
        Assert.False(_hotkeys.Registered.ContainsKey(100));
        Assert.Equal(HotkeyGesture.CtrlAlt(0x32), _hotkeys.Registered[101]);
        _hotkeys.Press(101);
        Assert.Equal(secondId, _store.Settings.ActiveProfileId);
        Assert.Equal(new Size(1080, 1920), vm.CurrentFrame.Size);
        _hotkeys.Press(102);
        Assert.Equal(thirdId, _store.Settings.ActiveProfileId);
        Assert.Equal(new Rectangle(0, 0, 2560, 1440), vm.CurrentFrame);
        _hotkeys.Press(100);
        Assert.Equal(thirdId, _store.Settings.ActiveProfileId);
    }

    [Fact]
    public async Task Renaming_preserves_the_saved_setup_identity_and_shortcut_without_adopting_current_edits()
    {
        var saved = DetailedProfile();
        var vm = Create(SettingsWith(saved));
        var edited = new Rectangle(500, 400, 800, 500);
        vm.SetFrameFromInteraction(edited, freeform: true);
        _dialogs.Name = "Renamed recording";

        await vm.Profiles.Single().RenameCommand.ExecuteAsync(null);

        Assert.Equal(saved with { Name = "Renamed recording" }, Assert.Single(_store.Settings.Profiles));
        Assert.Equal(edited, vm.CurrentFrame);
        Assert.Equal(["Ctrl", "Alt", "4"], vm.Profiles.Single().Keys);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public async Task Duplicate_has_fresh_profile_and_guide_ids_no_shortcut_and_does_not_apply_a_resize_profile()
    {
        var saved = DetailedProfile() with { Mode = FrameMode.Resize, TargetProcessName = "WindowsTerminal" };
        var settings = SettingsWith(saved);
        settings.ActiveProfileId = null;
        var vm = Create(settings);
        var originalFrame = vm.CurrentFrame;
        _dialogs.Name = "Recording copy";

        await vm.Profiles.Single().DuplicateCommand.ExecuteAsync(null);

        var duplicate = _store.Settings.Profiles.Single(profile => profile.Id != saved.Id);
        Assert.Equal("Recording copy", duplicate.Name);
        Assert.Equal(0, duplicate.HotkeySlot);
        Assert.Equal(saved.Mode, duplicate.Mode);
        Assert.Equal(saved.Appearance, duplicate.Appearance);
        Assert.NotEqual(saved.NestedFrames[0].Id, duplicate.NestedFrames[0].Id);
        Assert.Equal(saved.NestedFrames[0] with { Id = duplicate.NestedFrames[0].Id }, duplicate.NestedFrames[0]);
        Assert.NotSame(saved.NestedFrames, duplicate.NestedFrames);
        Assert.Equal(originalFrame, vm.CurrentFrame);
        Assert.Null(_store.Settings.ActiveProfileId);
        Assert.Empty(_windows.Resizes);
        Assert.False(vm.Profiles.Single(profile => profile.Profile.Id == duplicate.Id).HasKeys);
    }

    [Fact]
    public async Task Explicit_update_captures_current_frame_appearance_and_guides_but_preserves_name_id_and_slot()
    {
        var vm = Create();
        var target = _store.Settings.Profiles[1];
        var edited = new Rectangle(300, 200, 1280, 720);
        vm.SetFrameFromInteraction(edited, freeform: true);
        vm.AddSquareFrameCommand.Execute(null);
        vm.IncludeProfileAppearance = true;
        vm.Swatches.Single(swatch => swatch.Color == OutlineColor.Red).SelectCommand.Execute(null);

        await vm.Profiles.Single(profile => profile.Profile.Id == target.Id).UpdateCommand.ExecuteAsync(null);

        var updated = _store.Settings.Profiles.Single(profile => profile.Id == target.Id);
        Assert.Equal(target.Name, updated.Name);
        Assert.Equal(target.HotkeySlot, updated.HotkeySlot);
        Assert.Equal(edited, new Rectangle(updated.X, updated.Y, updated.Width, updated.Height));
        Assert.Null(updated.Anchor);
        Assert.False(updated.IsAspectLocked);
        Assert.Equal(OutlineColor.Red, updated.Appearance!.Color);
        Assert.Single(updated.NestedFrames);
        Assert.Equal(target.Id, _store.Settings.ActiveProfileId);
        Assert.Equal("Profile", vm.ProfileEyebrow);
        Assert.Equal(edited, vm.CurrentFrame);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public async Task Canceling_explicit_update_preserves_the_saved_profile()
    {
        var vm = Create();
        var saved = _store.Settings.Profiles[0];
        int saves = _store.SaveCount;
        vm.WidthValue = 1280;
        _dialogs.Confirm = false;

        await vm.Profiles[0].UpdateCommand.ExecuteAsync(null);

        Assert.Equal(saved, _store.Settings.Profiles[0]);
        Assert.Equal(saves, _store.SaveCount);
    }

    [Fact]
    public async Task Save_with_an_existing_case_insensitive_name_does_not_silently_overwrite()
    {
        var vm = Create();
        var profiles = _store.Settings.Profiles.ToArray();
        int saves = _store.SaveCount;
        vm.WidthValue = 1280;
        _dialogs.Name = "  YOUTUBE 1080P  ";

        await vm.SaveProfileCommand.ExecuteAsync(null);

        Assert.Equal(profiles, _store.Settings.Profiles);
        Assert.Equal(saves, _store.SaveCount);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Contains("Update", vm.StatusDetail);
    }

    [Fact]
    public async Task Saving_a_new_profile_uses_a_free_hole_without_renumbering_survivors()
    {
        var vm = Create();
        await vm.Profiles[0].DeleteCommand.ExecuteAsync(null);
        _dialogs.Name = "New recording";

        await vm.SaveProfileCommand.ExecuteAsync(null);

        Assert.Equal([2, 3, 1], _store.Settings.Profiles.Select(profile => profile.HotkeySlot));
        Assert.Equal(["Ctrl", "Alt", "1"], vm.Profiles[^1].Keys);
    }

    [Fact]
    public async Task Appearance_override_is_temporary_and_plain_profiles_restore_the_global_fallback()
    {
        var styled = new FrameProfile { Id = "styled", Name = "Styled", HotkeySlot = 1,
            Appearance = new OutlineStyle { Color = OutlineColor.Red }, IsAspectLocked = false };
        var plain = new FrameProfile { Id = "plain", Name = "Plain", HotkeySlot = 2, Width = 1280, Height = 720 };
        var baseline = new OutlineStyle { Color = OutlineColor.Blue, ShowCenterMark = true };
        var vm = Create(new AppSettings { Profiles = [styled, plain], ActiveProfileId = plain.Id,
            Style = baseline, ProfileShortcutVersion = 1 });

        await Apply(vm.Profiles.Single(profile => profile.Profile.Id == styled.Id));
        Assert.Equal(styled.Appearance, vm.Style);
        Assert.Equal(baseline, _store.Settings.Style);
        Assert.True(vm.IncludeProfileAppearance);
        Assert.False(vm.IsAspectLocked);
        Assert.Equal("Profile", vm.ProfileEyebrow);

        vm.ShowThirds = true;
        Assert.Equal("Profile · edited", vm.ProfileEyebrow);
        Assert.Equal(baseline, _store.Settings.Style);

        await Apply(vm.Profiles.Single(profile => profile.Profile.Id == plain.Id));
        Assert.Equal(baseline, vm.Style);
        Assert.Equal(baseline, _overlay.Style);
        Assert.Equal(baseline, _store.Settings.Style);
        Assert.False(vm.IncludeProfileAppearance);
        Assert.Equal("Profile", vm.ProfileEyebrow);
    }

    [Fact]
    public void Profiles_without_saved_appearance_do_not_become_edited_when_the_global_appearance_changes()
    {
        var vm = Create();
        vm.ShowThirds = true;
        Assert.True(_store.Settings.Style.ShowThirds);
        Assert.False(vm.IncludeProfileAppearance);
        Assert.Equal("Profile", vm.ProfileEyebrow);
        vm.IncludeProfileAppearance = true;
        Assert.Equal("Profile · edited", vm.ProfileEyebrow);
    }

    [Fact]
    public async Task Shortcut_reassignment_requires_confirmation_and_releases_only_the_previous_owner()
    {
        var vm = Create();
        string first = vm.Profiles[0].Profile.Id, second = vm.Profiles[1].Profile.Id;
        _dialogs.Shortcut = 1;
        _dialogs.Confirm = false;

        await vm.Profiles.Single(profile => profile.Profile.Id == second).ShortcutCommand.ExecuteAsync(null);
        Assert.Equal([1, 2, 3], _store.Settings.Profiles.Select(profile => profile.HotkeySlot));
        _dialogs.Confirm = true;
        await vm.Profiles.Single(profile => profile.Profile.Id == second).ShortcutCommand.ExecuteAsync(null);

        Assert.Equal(0, _store.Settings.Profiles.Single(profile => profile.Id == first).HotkeySlot);
        Assert.Equal(1, _store.Settings.Profiles.Single(profile => profile.Id == second).HotkeySlot);
        Assert.Equal(3, _store.Settings.Profiles[2].HotkeySlot);
        _hotkeys.Press(100);
        Assert.Equal(second, _store.Settings.ActiveProfileId);
    }

    [Fact]
    public async Task Import_review_cancellation_is_atomic_and_does_not_resize_or_apply()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        var profiles = _store.Settings.Profiles.ToArray();
        string? active = _store.Settings.ActiveProfileId;
        var original = vm.CurrentFrame;
        int saves = _store.SaveCount;
        _transfer.ImportJson = ImportDocument();
        _transfer.CancelReview = true;

        await vm.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal(1, _transfer.ReviewCount);
        Assert.Equal(profiles, _store.Settings.Profiles);
        Assert.Equal(active, _store.Settings.ActiveProfileId);
        Assert.Equal(original, vm.CurrentFrame);
        Assert.Equal(original, _overlay.Frame);
        Assert.True(_overlay.IsVisible);
        Assert.Equal(saves, _store.SaveCount);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public async Task Canceling_the_import_file_picker_does_not_open_review_or_change_settings()
    {
        var vm = Create();
        var profiles = _store.Settings.Profiles.ToArray();
        int saves = _store.SaveCount;

        await vm.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal(0, _transfer.ReviewCount);
        Assert.Equal(profiles, _store.Settings.Profiles);
        Assert.Equal(saves, _store.SaveCount);
    }

    [Fact]
    public async Task One_invalid_profile_rejects_the_entire_import_before_review()
    {
        var vm = Create();
        var profiles = _store.Settings.Profiles.ToArray();
        int saves = _store.SaveCount;
        _transfer.ImportJson = ImportDocument().Replace("\"width\": 1280", "\"width\": 0", StringComparison.Ordinal);

        await vm.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal(0, _transfer.ReviewCount);
        Assert.Equal(profiles, _store.Settings.Profiles);
        Assert.Equal(saves, _store.SaveCount);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public async Task Imported_profiles_get_new_ids_unique_names_and_no_shortcuts_without_applying_resize_profiles()
    {
        var vm = Create();
        vm.PrimaryCommand.Execute(null);
        var oldProfiles = _store.Settings.Profiles.ToArray();
        string? active = _store.Settings.ActiveProfileId;
        var original = vm.CurrentFrame;
        var originalStyle = vm.Style;
        _transfer.ImportJson = ImportDocument();

        await vm.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.Profiles.Count);
        Assert.Equal(oldProfiles, _store.Settings.Profiles.Take(3));
        var imported = _store.Settings.Profiles.Skip(3).ToArray();
        Assert.All(imported, profile =>
        {
            Assert.Equal(0, profile.HotkeySlot);
            Assert.DoesNotContain(oldProfiles, existing => existing.Id == profile.Id);
            Assert.DoesNotContain(profile.Id, new[] { "incoming-outline", "incoming-resize" });
            Assert.InRange(profile.Name.Length, 1, 60);
        });
        Assert.Equal(5, _store.Settings.Profiles.Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(imported, profile => profile.Mode == FrameMode.Resize);
        Assert.NotEqual("incoming-guide", imported[0].NestedFrames[0].Id);
        Assert.Equal(active, _store.Settings.ActiveProfileId);
        Assert.Equal(original, vm.CurrentFrame);
        Assert.Equal(original, _overlay.Frame);
        Assert.Equal(originalStyle, vm.Style);
        Assert.True(_overlay.IsVisible);
        Assert.Empty(_windows.Resizes);
        Assert.All(vm.Profiles.Skip(3), profile => Assert.False(profile.HasKeys));
    }

    [Fact]
    public async Task Missing_or_disconnected_import_mapping_rejects_all_profiles_after_review()
    {
        var vm = Create();
        var profiles = _store.Settings.Profiles.ToArray();
        int saves = _store.SaveCount;
        _transfer.ImportJson = ImportDocument();
        _transfer.Mappings = new Dictionary<string, string>();

        await vm.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal(1, _transfer.ReviewCount);
        Assert.Equal(profiles, _store.Settings.Profiles);
        Assert.Equal(saves, _store.SaveCount);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public async Task A_display_disconnecting_during_review_does_not_create_a_partial_import()
    {
        var vm = Create();
        var profiles = _store.Settings.Profiles.ToArray();
        _transfer.ImportJson = ImportDocument();
        _transfer.Mappings = new Dictionary<string, string> { [FakeDisplays.Primary.DeviceName] = FakeDisplays.Secondary.DeviceName };
        _transfer.OnReview = () => _displays.Displays.Remove(FakeDisplays.Secondary);

        await vm.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal(profiles, _store.Settings.Profiles);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public async Task Save_failure_rolls_back_imported_profiles_active_selection_and_registered_shortcuts()
    {
        var vm = Create();
        var profiles = _store.Settings.Profiles.ToArray();
        var registered = _hotkeys.Registered.ToDictionary();
        string? active = _store.Settings.ActiveProfileId;
        _transfer.ImportJson = ImportDocument();
        _store.Fail = true;

        await vm.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal(profiles, _store.Settings.Profiles);
        Assert.Equal(active, _store.Settings.ActiveProfileId);
        Assert.Equal(registered.OrderBy(pair => pair.Key), _hotkeys.Registered.OrderBy(pair => pair.Key));
        Assert.Equal(profiles.Length, vm.Profiles.Count);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public async Task Single_profile_and_all_profile_exports_contain_only_the_requested_saved_setups()
    {
        var vm = Create();
        var saved = _store.Settings.Profiles[1];
        vm.WidthValue = 1280;

        await vm.Profiles[1].ExportCommand.ExecuteAsync(null);

        var package = ProfilePackageSerializer.Parse(_transfer.ExportedJson!);
        Assert.Equal(saved.Name, Assert.Single(package.Profiles).Profile.Name);
        Assert.Equal(saved.Width, package.Profiles[0].Profile.Width);
        Assert.Equal(saved.Height, package.Profiles[0].Profile.Height);
        Assert.All(package.Profiles, profile => Assert.Equal(0, profile.Profile.HotkeySlot));

        await vm.ExportProfilesCommand.ExecuteAsync(null);
        Assert.Equal(3, ProfilePackageSerializer.Parse(_transfer.ExportedJson!).Profiles.Count);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public async Task Rename_and_duplicate_name_collisions_preserve_existing_profiles()
    {
        var vm = Create();
        var profiles = _store.Settings.Profiles.ToArray();
        _dialogs.Name = profiles[0].Name;

        await vm.Profiles[1].RenameCommand.ExecuteAsync(null);
        await vm.Profiles[1].DuplicateCommand.ExecuteAsync(null);

        Assert.Equal(profiles, _store.Settings.Profiles);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
    }

    [Fact]
    public void Changing_aspect_lock_notifies_the_edited_profile_header()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        vm.IsAspectLocked = false;

        Assert.Equal("Profile · edited", vm.ProfileEyebrow);
        Assert.Contains(nameof(MainViewModel.ProfileEyebrow), changed);
    }

    [Fact]
    public async Task Failed_profile_modifier_save_keeps_previous_settings_runtime_registration_and_warning()
    {
        var vm = Create();
        var registered = _hotkeys.Registered.ToDictionary();
        var previous = _store.Settings.ProfileHotkeyModifiers;
        var keys = vm.Hotkeys[3].Keys.ToArray();
        _dialogs.RecordedHotkey = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x31);
        _store.Fail = true;

        await vm.Hotkeys[3].ChangeCommand.ExecuteAsync(null);

        Assert.Equal(previous, _store.Settings.ProfileHotkeyModifiers);
        Assert.Equal(registered.OrderBy(pair => pair.Key), _hotkeys.Registered.OrderBy(pair => pair.Key));
        Assert.Equal(keys, vm.Hotkeys[3].Keys);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Contains("couldn’t be saved", vm.StatusDetail);
    }

    [Fact]
    public async Task Failed_global_hotkey_save_keeps_previous_settings_runtime_registration_and_warning()
    {
        var vm = Create();
        var registered = _hotkeys.Registered.ToDictionary();
        var previous = _store.Settings.ToggleOutlineHotkey;
        _dialogs.RecordedHotkey = HotkeyGesture.CtrlAlt(0x50);
        _store.Fail = true;

        await vm.Hotkeys[0].ChangeCommand.ExecuteAsync(null);

        Assert.Equal(previous, _store.Settings.ToggleOutlineHotkey);
        Assert.Equal(registered.OrderBy(pair => pair.Key), _hotkeys.Registered.OrderBy(pair => pair.Key));
        Assert.Equal(previous.Keys, vm.Hotkeys[0].Keys);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Contains("couldn’t be saved", vm.StatusDetail);
    }

    [Fact]
    public async Task Applying_a_profile_does_not_overwrite_a_persistence_failure_with_success_status()
    {
        var vm = Create();
        _store.Fail = true;

        await Apply(vm.Profiles[1]);

        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.Contains("couldn’t be saved", vm.StatusDetail);
    }

    [Fact]
    public async Task Resize_profile_without_a_saved_app_does_not_resize_a_previously_selected_application()
    {
        var profile = new FrameProfile
        {
            Id = "unbound-resize", Name = "Choose an app", Mode = FrameMode.Resize,
            TargetProcessName = null, Width = 1280, Height = 720, HotkeySlot = 1
        };
        var settings = SettingsWith(profile);
        settings.ActiveProfileId = null;
        var vm = Create(settings);
        vm.ShowResizeModeCommand.Execute(null);
        vm.Windows.Single(window => window.Window.IsSameWindow(FakeWindows.Terminal)).SelectCommand.Execute(null);
        Assert.True(vm.HasTargetWindow);

        await Apply(vm.Profiles.Single());

        Assert.False(vm.HasTargetWindow);
        Assert.False(vm.PrimaryCommand.CanExecute(null));
        Assert.Empty(_windows.Resizes);
        Assert.Null(_windows.TrackedWindow);
        Assert.Contains("Choose a window", vm.StatusDetail);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saving_or_updating_without_profile_appearance_aligns_the_live_style_with_the_default(bool update)
    {
        var styled = DetailedProfile();
        var baseline = new OutlineStyle { Color = OutlineColor.Blue, ShowCenterMark = true };
        var settings = SettingsWith(styled);
        settings.Style = baseline;
        var vm = Create(settings);
        Assert.Equal(OutlineColor.Red, vm.Style.Color);
        vm.IncludeProfileAppearance = false;
        _dialogs.Name = "Default appearance copy";

        if (update) await vm.Profiles.Single().UpdateCommand.ExecuteAsync(null);
        else await vm.SaveProfileCommand.ExecuteAsync(null);

        var active = _store.Settings.Profiles.Single(profile => profile.Id == _store.Settings.ActiveProfileId);
        Assert.Null(active.Appearance);
        Assert.Equal(baseline, vm.Style);
        Assert.Equal(baseline, _overlay.Style);
        Assert.Equal(baseline, _store.Settings.Style);
        Assert.False(vm.IncludeProfileAppearance);
        Assert.Equal("Profile", vm.ProfileEyebrow);
    }

    private static Task Apply(ProfileItem item) => ((IAsyncRelayCommand)item.SelectCommand).ExecuteAsync(null);

    private static AppSettings SettingsWith(FrameProfile profile) => new()
    {
        Profiles = [profile], ActiveProfileId = profile.Id, ProfileShortcutVersion = 1
    };

    private static FrameProfile DetailedProfile() => new()
    {
        Id = "recording", Name = "Recording", HotkeySlot = 4,
        Width = 1280, Height = 720, X = 100, Y = 80, Anchor = null,
        IsAspectLocked = false, Appearance = new OutlineStyle { Color = OutlineColor.Red, ShowThirds = true },
        DisplaySnapshot = FakeDisplays.Primary,
        NestedFrames = [new() { Id = "guide", Name = "Shorts", AspectWidth = 9, AspectHeight = 16, ScalePercent = 60 }]
    };

    private static string ImportDocument() => ProfilePackageSerializer.Export(
    [
        new FrameProfile
        {
            Id = "incoming-outline", Name = "YouTube 1080p", Width = 1280, Height = 720, HotkeySlot = 8,
            Appearance = new OutlineStyle { Color = OutlineColor.Red },
            NestedFrames = [new() { Id = "incoming-guide", Name = "Shorts" }]
        },
        new FrameProfile
        {
            Id = "incoming-resize", Name = "YouTube 1080p", Mode = FrameMode.Resize, Width = 1280, Height = 720,
            HotkeySlot = 9, TargetProcessName = "WindowsTerminal", Area = WindowArea.VisibleFrame
        }
    ], [FakeDisplays.Primary]);

    private sealed class WorkflowDialogs : IDialogService
    {
        public string? Name { get; set; }
        public bool Confirm { get; set; } = true;
        public int? Shortcut { get; set; }
        public HotkeyGesture? RecordedHotkey { get; set; }

        public Task<string?> PromptProfileNameAsync(string suggestion) => Task.FromResult(Name);
        public Task<int?> ChooseProfileShortcutAsync(FrameProfile profile, IReadOnlyList<FrameProfile> profiles) => Task.FromResult(Shortcut);
        public Task<HotkeyGesture?> RecordHotkeyAsync(string actionName, HotkeyGesture current, string? instructions = null) => Task.FromResult(RecordedHotkey);
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) => Task.FromResult(Confirm);
    }

    private sealed class WorkflowTransfer : IProfileTransferService
    {
        public string? ImportJson { get; set; }
        public string? ExportedJson { get; private set; }
        public bool CancelReview { get; set; }
        public int ReviewCount { get; private set; }
        public IReadOnlyDictionary<string, string>? Mappings { get; set; }
        public Action? OnReview { get; set; }

        public Task<string?> ReadImportAsync(CancellationToken cancellation = default) => Task.FromResult(ImportJson);
        public Task<bool> WriteExportAsync(string suggestedName, string json, CancellationToken cancellation = default)
        {
            ExportedJson = json;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyDictionary<string, string>?> ReviewImportAsync(ProfilePackage package,
            IReadOnlyList<DisplayInfo> displays, CancellationToken cancellation = default)
        {
            ReviewCount++;
            OnReview?.Invoke();
            IReadOnlyDictionary<string, string>? result = CancelReview ? null : Mappings ?? package.Profiles
                .Select(ProfilePackageSerializer.DisplayKey).Distinct(StringComparer.Ordinal)
                .ToDictionary(key => key, _ => displays.First(display => display.IsPrimary).DeviceName, StringComparer.Ordinal);
            return Task.FromResult(result);
        }
    }

    private sealed class WorkflowStore : ISettingsStore
    {
        public AppSettings Settings { get; set; } = new();
        public int SaveCount { get; private set; }
        public bool Fail { get; set; }

        public AppSettings Load() => Settings;
        public void Save(AppSettings settings)
        {
            if (Fail) throw new IOException("The test store is unavailable.");
            Settings = settings;
            SaveCount++;
        }
    }
}
