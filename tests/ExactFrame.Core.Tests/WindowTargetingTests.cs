using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;
using System.Text.Json.Nodes;

namespace ExactFrame.Core.Tests;

public sealed class WindowTargetingTests : IDisposable
{
    private static WindowInfo First => FakeWindows.Browser with
    { Title = "Alpha project — Editor", ExecutablePath = @"C:\Apps\Editor.exe", ClassName = "EditorWindow", ZOrder = 3 };
    private static WindowInfo Second => First with { Handle = 0x5005, Title = "Beta project — Editor", ZOrder = 0 };
    private static WindowMatchCriteria FullMatch => new()
    { ExecutablePath = First.ExecutablePath, Title = "Alpha project", ClassName = First.ClassName };
    private readonly FakeWindows _windows = new();
    private readonly MemoryStore _store = new();
    private readonly FakeDialogs _dialogs = new() { ProfileName = "Alpha setup" };
    private MainViewModel? _vm;
    private MainViewModel Create(FrameProfile? profile = null, params RememberedAppSize[] sizes)
    {
        _windows.Windows.Clear(); _windows.Windows.AddRange([First, Second]);
        _store.Settings = new AppSettings
        { Profiles = profile is null ? [] : [profile], RememberedAppSizes = [.. sizes], ActiveProfileId = null, ProfileShortcutVersion = 1 };
        _vm = new MainViewModel(new FakeDisplays(), _windows, new FakePicker(), new FakeOverlay(), new FakeHotkeys(),
            new FakeClipboard(), _dialogs, _store, new FakeStartup());
        _vm.Initialize(); _vm.ShowResizeModeCommand.Execute(null);
        return _vm;
    }
    public void Dispose() => _vm?.Dispose();
    private static FrameProfile Profile(WindowMatchCriteria? match) => new()
    { Id = "target", Name = "Alpha", Mode = FrameMode.Resize, Width = 1280, Height = 720, TargetProcessName = "msedge", TargetMatch = match };
    private static Task Select(SelectableItem item) => ((IAsyncRelayCommand)item.SelectCommand).ExecuteAsync(null);

    [Fact]
    public void All_conditions_are_required_and_case_is_ignored()
    {
        var rule = FullMatch with { ExecutablePath = "c:/APPS/editor.exe", ClassName = "editorwindow", Title = "ALPHA PROJECT" };
        Assert.True(WindowMatcher.Matches(First, "MSEDGE", rule));
        Assert.False(WindowMatcher.Matches(Second, "msedge", rule));
        Assert.False(WindowMatcher.Matches(First, "chrome", rule));
        Assert.False(WindowMatcher.Matches(First with { ExecutablePath = null }, "msedge", rule));
        Assert.False(WindowMatcher.Matches(First with { ClassName = null }, "msedge", rule));
        Assert.False(WindowMatcher.Matches(First, "msedge", rule with { ClassName = "Other" }));
        Assert.False(WindowMatcher.Matches(First, "msedge", rule with { ExecutablePath = @"D:\Apps\Editor.exe" }));
    }

    [Fact]
    public void Titles_have_literal_contains_and_exact_modes()
    {
        Assert.False(WindowMatcher.Matches(First, "msedge", new() { Title = "Alpha*" }));
        Assert.False(WindowMatcher.Matches(First, "msedge", FullMatch with { TitleMode = WindowTitleMatchMode.Exact }));
        Assert.True(WindowMatcher.Matches(First, "msedge", FullMatch with { Title = First.Title, TitleMode = WindowTitleMatchMode.Exact }));
        Assert.True(WindowMatcher.Matches(First with { Title = " Alpha " }, "msedge", new() { Title = " Alpha ", TitleMode = WindowTitleMatchMode.Exact }));
        Assert.Null(WindowMatcher.Normalize(new() { Title = "  ", ClassName = "" }));
        Assert.True(WindowMatcher.Equivalent(FullMatch, FullMatch with { ExecutablePath = "c:/apps/editor.exe" }));
    }

    [Theory]
    [InlineData("Editor.exe")]
    [InlineData(@"C:Editor.exe")]
    [InlineData(@"C:\Apps\*.exe")]
    [InlineData(@"C:\Apps\..\Editor.exe")]
    public void Invalid_paths_never_broaden_the_match(string path)
    {
        var rule = FullMatch with { ExecutablePath = path };
        Assert.NotNull(WindowMatcher.ValidationError(rule));
        Assert.False(WindowMatcher.Matches(First, "msedge", rule));
    }

    [Fact]
    public void Unknown_modes_control_characters_and_long_fields_are_invalid()
    {
        Assert.NotNull(WindowMatcher.ValidationError(new() { TitleMode = (WindowTitleMatchMode)99 }));
        Assert.NotNull(WindowMatcher.ValidationError(new() { Title = "Alpha\nBeta" }));
        Assert.NotNull(WindowMatcher.ValidationError(new() { ClassName = new string('x', 2049) }));
        Assert.Null(WindowMatcher.ValidationError(new() { ExecutablePath = @"\\server\share\Editor.exe" }));
    }

    [Fact]
    public async Task Profile_conditions_select_the_matching_window_instead_of_a_different_selected_or_frontmost_window()
    {
        var vm = Create(Profile(FullMatch));
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(Second)));
        await Select(vm.Profiles.Single());
        Assert.True(Assert.Single(_windows.Resizes).Window.IsSameWindow(First));
    }

    [Fact]
    public async Task No_match_does_not_fall_back_to_the_process_or_previous_selection()
    {
        var vm = Create(Profile(FullMatch with { ExecutablePath = @"D:\Missing\Editor.exe" }));
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(Second)));
        await Select(vm.Profiles.Single());
        Assert.Empty(_windows.Resizes); Assert.False(vm.HasTargetWindow);
        Assert.Contains("No window matches", vm.StatusDetail);
    }

    [Fact]
    public async Task Entering_a_process_name_previews_matches_without_applying_remembered_sizes()
    {
        var vm = Create(null, new RememberedAppSize("msedge", 1600, 900, WindowArea.Client));
        vm.TargetProcessName = "msedge.exe";
        Assert.Equal(2, vm.MatchingWindows.Count);
        vm.TargetTitle = "Alpha";
        await Select(vm.MatchingWindows.Single());
        Assert.Empty(_windows.Resizes);
        Assert.True(vm.CanResizeSelectedWindow);
    }

    [Fact]
    public async Task Preview_selection_is_read_only_even_when_a_remembered_size_exists()
    {
        var vm = Create(Profile(null), new RememberedAppSize("msedge", 1600, 900, WindowArea.Client));
        await Select(vm.Profiles.Single());
        Assert.Equal(2, vm.MatchingWindows.Count);
        await Select(vm.MatchingWindows.Single(item => item.Window.IsSameWindow(First)));
        Assert.Empty(_windows.Resizes); Assert.True(vm.CanResizeSelectedWindow);
        Assert.Contains(First.Title, vm.TargetWindowPreview);
        Assert.Contains(First.ExecutablePath!, vm.TargetWindowPreview);
        Assert.Contains(First.ClassName!, vm.TargetWindowPreview);
        Assert.Equal(First.Bounds, vm.Preview.Ghost);
    }

    [Fact]
    public async Task Changed_title_after_preview_is_rechecked_before_a_resize()
    {
        var vm = Create();
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(First)));
        vm.UseSelectedWindowMatchCommand.Execute(null);
        _windows.Windows[0] = First with { Title = "Different project" };
        await vm.PrimaryCommand.ExecuteAsync(null);
        Assert.Empty(_windows.Resizes);
        Assert.Contains("no longer matches", vm.StatusDetail);
    }

    [Fact]
    public async Task Preview_rechecks_metadata_and_does_not_select_a_stale_match()
    {
        var vm = Create();
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(First)));
        vm.TargetTitle = "Alpha";
        var stale = vm.MatchingWindows.Single();
        _windows.Windows.RemoveAt(0);
        await Select(stale);
        Assert.Empty(vm.MatchingWindows); Assert.False(vm.HasTargetWindow);
        Assert.Contains("no longer matches", vm.StatusDetail);
    }

    [Fact]
    public async Task Different_rules_for_one_process_remember_independent_sizes_and_forget_only_the_selected_rule()
    {
        var vm = Create();
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(First)));
        vm.TargetTitle = "Alpha"; vm.WidthValue = 1280;
        await vm.PrimaryCommand.ExecuteAsync(null);
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(Second)));
        vm.TargetTitle = "Beta"; vm.WidthValue = 1600;
        await vm.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal(2, _store.Settings.RememberedAppSizes.Count);
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(First)));
        Assert.Equal(1280, _windows.Resizes.Last().Desired.Width);
        Assert.Equal("Alpha", vm.TargetTitle);
        vm.ForgetAppSizeCommand.Execute(null);
        Assert.Equal("Beta", Assert.Single(_store.Settings.RememberedAppSizes).TargetMatch!.Title);
    }

    [Fact]
    public async Task A_more_specific_remembered_rule_wins_over_the_legacy_process_default()
    {
        var vm = Create(null, new("msedge", 1600, 900, WindowArea.Client),
            new RememberedAppSize("msedge", 1280, 720, WindowArea.Client) { TargetMatch = FullMatch });
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(First)));
        Assert.Equal(1280, Assert.Single(_windows.Resizes).Desired.Width);
        Assert.Equal(FullMatch.Title, vm.TargetTitle);
    }

    [Fact]
    public async Task Equally_specific_conflicting_remembered_rules_do_not_auto_resize()
    {
        var vm = Create(null,
            new RememberedAppSize("msedge", 1280, 720, WindowArea.Client) { TargetMatch = new() { Title = "Alpha" } },
            new RememberedAppSize("msedge", 1600, 900, WindowArea.Client) { TargetMatch = new() { Title = "project" } });
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(First)));
        Assert.Empty(_windows.Resizes); Assert.Contains("equally", vm.StatusDetail);
    }

    [Fact]
    public async Task Saving_and_last_session_preserve_conditions_and_invalid_input_cannot_be_saved()
    {
        var vm = Create();
        await Select(vm.Windows.Single(item => item.Window.IsSameWindow(First)));
        vm.UseSelectedWindowMatchCommand.Execute(null);
        await vm.SaveProfileCommand.ExecuteAsync(null);
        var match = Assert.Single(_store.Settings.Profiles).TargetMatch;
        Assert.Equal(FullMatch with { Title = First.Title, TitleMode = WindowTitleMatchMode.Exact }, match);
        vm.Shutdown();
        Assert.Equal(match, _store.Settings.LastFrame!.TargetMatch);
        vm.TargetExecutablePath = "relative.exe";
        await vm.SaveProfileCommand.ExecuteAsync(null);
        Assert.Single(_store.Settings.Profiles);
        Assert.False(vm.PrimaryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Updating_the_active_resize_profile_keeps_identity_and_conditions_without_resizing()
    {
        var vm = Create(Profile(null));
        await Select(vm.Profiles.Single()); // Ambiguous: loads the setup without resizing.
        await Select(vm.MatchingWindows.Single(item => item.Window.IsSameWindow(First)));
        vm.UseSelectedWindowMatchCommand.Execute(null);
        Assert.True(vm.UpdateTargetProfileCommand.CanExecute(null));
        await vm.UpdateTargetProfileCommand.ExecuteAsync(null);
        var saved = Assert.Single(_store.Settings.Profiles);
        Assert.Equal("target", saved.Id);
        Assert.Equal(First.Title, saved.TargetMatch!.Title);
        Assert.Empty(_windows.Resizes);
    }

    [Fact]
    public void Targeted_exports_use_version_three_and_old_versions_cannot_silently_ignore_conditions()
    {
        string json = ProfilePackageSerializer.Export([Profile(FullMatch)], [FakeDisplays.Primary]);
        var parsed = ProfilePackageSerializer.Parse(json);
        Assert.Equal(3, parsed.Version); Assert.Equal(FullMatch, parsed.Profiles[0].Profile.TargetMatch);
        var mapped = ProfilePackageSerializer.Remap(parsed, new Dictionary<string, string> { [ProfilePackageSerializer.DisplayKey(parsed.Profiles[0])] = FakeDisplays.Primary.DeviceName }, [FakeDisplays.Primary]);
        Assert.Equal(FullMatch, mapped.Profiles[0].TargetMatch);
        var old = JsonNode.Parse(json)!; old["version"] = 2;
        Assert.Throws<InvalidOperationException>(() => ProfilePackageSerializer.Parse(old.ToJsonString()));
        Assert.Equal(2, ProfilePackageSerializer.Parse(ProfilePackageSerializer.Export([Profile(null)], [FakeDisplays.Primary])).Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_settings_stores_preserve_profiles_remembered_rules_and_last_session(bool database)
    {
        string folder = Path.Combine(Path.GetTempPath(), "exactframe-targeting-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            ISettingsStore NewStore() => database ? new CSharpDbAppStore(Path.Combine(folder, "settings.db")) : new JsonSettingsStore(Path.Combine(folder, "settings.json"));
            var profile = Profile(FullMatch);
            var settings = new AppSettings { Profiles = [profile], LastFrame = profile,
                RememberedAppSizes = [new RememberedAppSize("msedge", 1280, 720, WindowArea.Client) { TargetMatch = FullMatch }] };
            var store = NewStore();
            try { store.Save(settings); } finally { (store as IDisposable)?.Dispose(); }
            var reopened = NewStore();
            try
            {
                var loaded = reopened.Load(); Assert.Equal(settings.Profiles, loaded.Profiles);
                Assert.Equal(settings.LastFrame, loaded.LastFrame);
                Assert.Equal(settings.RememberedAppSizes, loaded.RememberedAppSizes);
            }
            finally { (reopened as IDisposable)?.Dispose(); }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
