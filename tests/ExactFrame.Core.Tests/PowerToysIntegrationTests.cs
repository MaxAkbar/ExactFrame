using System.Text.Json;
using ExactFrame.Core.Integration;
using ExactFrame.Core.Models;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class PowerToysIntegrationTests : IDisposable
{
    private readonly FakeDisplays _displays = new();
    private readonly FakeWindows _windows = new();
    private readonly FakeOverlay _overlay = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly MemoryStore _store = new();
    private readonly CatalogPublisher _publisher = new();
    private readonly PowerToys _powerToys = new();
    private int _powerToysResolutions;
    private MainViewModel? _main;

    private MainViewModel Create(AppSettings? settings = null)
    {
        if (settings is not null) _store.Settings = settings;
        _main = new(_displays, _windows, new FakePicker(), _overlay, new FakeHotkeys(), _clipboard,
            new FakeDialogs(), _store, new FakeStartup(), commandCatalog: _publisher,
            powerToys: () => { _powerToysResolutions++; return _powerToys; });
        _main.Initialize();
        return _main;
    }

    [Fact]
    public void Normal_startup_edits_and_shutdown_never_publish_without_opt_in()
    {
        var main = Create();
        main.HideFromRecorders = false;
        main.WidthValue = 1280;
        main.Shutdown();
        Assert.False(main.ShareWithCommandPalette);
        Assert.Equal(0, _publisher.Publishes);
        Assert.Equal(0, _publisher.Disables);
        Assert.Equal(0, _powerToysResolutions);
        Assert.True(_store.SaveCount > 0);
    }

    [Fact]
    public void Opt_in_publishes_only_safe_profile_metadata_and_opt_out_revokes_it()
    {
        var main = Create(new AppSettings { Profiles = [new FrameProfile
        {
            Id = "private-profile", Name = "Demo", TargetProcessName = "private-app",
            TargetMatch = new() { ExecutablePath = @"C:\Private\Secret.exe", Title = "Private document" }
        }] });
        main.ShareWithCommandPalette = true;
        Assert.True(_store.Settings.ShareWithCommandPalette);
        var profile = Assert.Single(_publisher.Profiles!);
        Assert.Equal("private-profile", profile.Id);
        Assert.Equal("Demo", profile.Name);
        string json = JsonSerializer.Serialize(profile);
        Assert.DoesNotContain("Private", json);
        Assert.DoesNotContain("Secret", json);
        main.ShareWithCommandPalette = false;
        Assert.False(_store.Settings.ShareWithCommandPalette);
        Assert.Equal(1, _publisher.Disables);
    }

    [Fact]
    public void Catalog_failure_cannot_fail_a_normal_save_or_change_main_status()
    {
        _publisher.Fail = true;
        var main = Create(new AppSettings { ShareWithCommandPalette = true });
        main.HideFromRecorders = false;
        Assert.False(_store.Settings.HideFromRecorders);
        Assert.Contains("could not refresh", main.CommandPaletteStatus);
        Assert.NotEqual(StatusKind.Warning, main.StatusKind);
        Assert.Equal("Choose a size, then show the outline.", main.StatusDetail);
    }

    [Fact]
    public async Task Profile_commands_apply_by_stable_id_without_PowerToys()
    {
        var main = Create();
        bool result = await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--profile", "course-1440p", "--show-outline", "--background"]));
        Assert.True(result);
        Assert.True(_overlay.IsVisible);
        Assert.Equal(2560, main.CurrentFrame.Width);
        Assert.Equal(1440, main.CurrentFrame.Height);
        Assert.Equal("Course 1440p", main.ActiveProfileName);
        Assert.Equal(0, _publisher.Publishes);
    }

    [Fact]
    public async Task Profiles_also_resolve_names_case_insensitively()
    {
        var main = Create();
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--profile", "youtube 1080P"])));
        Assert.Equal("YouTube 1080p", main.ActiveProfileName);
    }

    [Fact]
    public async Task A_linked_layout_is_applied_before_the_frame_and_an_unlinked_profile_never_resolves_PowerToys()
    {
        var main = Create(new AppSettings { PowerToysProfileLayouts = new() { ["course-1440p"] = "columns" } });
        Assert.Equal(0, _powerToysResolutions);
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--profile", "course-1440p", "--show-outline"])));
        Assert.Equal("columns", Assert.Single(_powerToys.AppliedLayouts));
        Assert.Equal(2560, main.CurrentFrame.Width);
        Assert.Equal(1, _powerToysResolutions);
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--profile", "youtube-1080p"])));
        Assert.Equal(1, _powerToysResolutions);
    }

    [Fact]
    public async Task Missing_PowerToys_cannot_prevent_a_previously_linked_profile_from_applying()
    {
        _powerToys.FailActions = true;
        var main = Create(new AppSettings { PowerToysProfileLayouts = new() { ["course-1440p"] = "columns" } });
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--profile", "course-1440p", "--show-outline"])));
        Assert.True(_overlay.IsVisible);
        Assert.Equal(2560, main.CurrentFrame.Width);
        Assert.NotEqual(StatusKind.Warning, main.StatusKind);
        Assert.Contains("still applies", main.PowerToysProfileStatus);
    }

    [Fact]
    public async Task Removing_a_layout_link_stops_all_PowerToys_work_for_that_profile()
    {
        var main = Create();
        main.LinkProfilePowerToysLayout("columns");
        Assert.True(main.HasProfilePowerToysLayout);
        Assert.Equal("columns", _store.Settings.PowerToysProfileLayouts["youtube-1080p"]);
        main.LinkProfilePowerToysLayout(null);
        Assert.False(main.HasProfilePowerToysLayout);
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--profile", "youtube-1080p"])));
        Assert.Equal(0, _powerToysResolutions);
    }

    [Fact]
    public async Task Setup_panels_can_open_after_an_unrelated_warning()
    {
        var main = Create();
        main.ReportLaunchError("An earlier action failed.");
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--powertoys"])));
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--obs"])));
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse([])));
    }

    [Fact]
    public async Task Showing_an_existing_outline_rechecks_fit_and_recovers_from_an_old_warning()
    {
        var main = Create();
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--show-outline"])));
        main.ReportLaunchError("An earlier action failed.");
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--show-outline"])));
        Assert.True(_overlay.IsVisible);
        Assert.NotEqual(StatusKind.Warning, main.StatusKind);
    }

    [Fact]
    public async Task Missing_profile_stops_remaining_actions_and_keeps_the_current_frame()
    {
        var main = Create();
        var before = main.CurrentFrame;
        Assert.False(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--profile", "deleted-profile", "--show-outline", "--copy-bounds"])));
        Assert.False(_overlay.IsVisible);
        Assert.Null(_clipboard.Text);
        Assert.Equal(before, main.CurrentFrame);
        Assert.Contains("not found", main.StatusDetail);
    }

    [Fact]
    public async Task Ambiguous_window_profile_preserves_the_existing_targeting_rules()
    {
        _windows.Windows.Add(FakeWindows.Studio with { Handle = 0x9000, ProcessId = 200, Title = "Another project" });
        var main = Create(new AppSettings { Profiles = [new FrameProfile
        {
            Id = "studio", Name = "Studio", Mode = FrameMode.Resize, TargetProcessName = "devenv", Width = 1280, Height = 720
        }] });
        Assert.False(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--profile", "studio", "--show-outline"])));
        Assert.Empty(_windows.Resizes);
        Assert.False(_overlay.IsVisible);
        Assert.Contains("matches 2 windows", main.StatusDetail);
    }

    [Fact]
    public async Task Clipboard_and_outline_commands_work_independently()
    {
        var main = Create();
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--show-outline", "--copy-bounds"])));
        Assert.True(_overlay.IsVisible);
        Assert.Contains("Width=1920", _clipboard.Text!);
        Assert.True(await main.ExecuteLaunchAsync(LaunchCommand.Parse(["--hide-outline"])));
        Assert.False(_overlay.IsVisible);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--profile")]
    [InlineData("--delay-ms", "-1")]
    [InlineData("--delay-ms", "30001")]
    [InlineData("--delay-ms", "forever")]
    [InlineData("--profile", "--show-outline")]
    [InlineData("--profile", "one", "--profile", "two")]
    public void Invalid_launch_arguments_are_rejected_before_execution(params string[] arguments) =>
        Assert.Throws<ArgumentException>(() => LaunchCommand.Parse(arguments));

    [Fact]
    public void Workspace_arguments_round_trip_through_the_command_parser()
    {
        var main = Create();
        Assert.Equal("--profile \"youtube-1080p\" --show-outline --background --delay-ms 2000", main.WorkspaceArguments);
        var parsed = LaunchCommand.Parse(["--profile", "youtube-1080p", "--show-outline", "--background", "--delay-ms", "2000"]);
        Assert.True(parsed.Background);
        Assert.Equal(2000, parsed.DelayMilliseconds);
        Assert.Equal(FrameAction.ShowOutline, Assert.Single(parsed.Actions));
    }

    [Fact]
    public async Task Opening_optional_tools_is_the_first_time_discovery_runs_and_absence_is_normal()
    {
        var main = Create();
        var service = new PowerToys();
        using var tools = new PowerToysViewModel(main, service, _clipboard);
        Assert.Equal(0, service.Discoveries);
        await tools.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, service.Discoveries);
        Assert.Contains("not installed", tools.Status);
        Assert.False(tools.ApplyLayoutCommand.CanExecute(null));
        Assert.False(tools.StartAwakeCommand.CanExecute(null));
        Assert.Empty(tools.Layouts);
        Assert.True(main.PrimaryCommand.CanExecute(null));
        Assert.Equal(0, _publisher.Publishes);
    }

    [Fact]
    public async Task Optional_tool_errors_do_not_change_the_frame_or_main_status()
    {
        var main = Create();
        var frame = main.CurrentFrame;
        var status = main.StatusDetail;
        var service = new PowerToys { Capabilities = new(true, true, true), FailActions = true };
        using var tools = new PowerToysViewModel(main, service, _clipboard);
        await tools.RefreshCommand.ExecuteAsync(null);
        await tools.ApplyLayoutCommand.ExecuteAsync(null);
        Assert.Contains("unavailable", tools.Status);
        Assert.Equal(frame, main.CurrentFrame);
        Assert.Equal(status, main.StatusDetail);
        Assert.True(tools.IsIdle);
    }

    [Fact]
    public async Task Removed_installation_disables_actions_when_refreshed()
    {
        var service = new PowerToys { Capabilities = new(true, true, true) };
        using var tools = new PowerToysViewModel(Create(), service, _clipboard);
        await tools.RefreshCommand.ExecuteAsync(null);
        Assert.True(tools.ApplyLayoutCommand.CanExecute(null));
        service.Capabilities = new(false, false, false);
        await tools.RefreshCommand.ExecuteAsync(null);
        Assert.False(tools.ApplyLayoutCommand.CanExecute(null));
        Assert.False(tools.StartAwakeCommand.CanExecute(null));
        Assert.Null(tools.SelectedLayout);
    }

    [Fact]
    public void Catalog_atomic_publication_and_revocation_do_not_touch_the_database()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ExactFrame-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "command-palette.json");
        try
        {
            var publisher = new CommandCatalogPublisher(path, Path.Combine(folder, "ExactFrame.exe"));
            publisher.Publish([new("demo", "Demo", "1920 × 1080")]);
            Assert.Equal("demo", Assert.Single(CommandCatalog.Read(path)!.Profiles).Id);
            publisher.Publish([new("renamed", "Renamed", "1280 × 720")]);
            Assert.Equal("Renamed", Assert.Single(CommandCatalog.Read(path)!.Profiles).Name);
            Assert.Single(Directory.GetFiles(folder));
            publisher.Disable();
            Assert.Null(CommandCatalog.Read(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Version\":999}")]
    [InlineData("{\"Version\":1,\"ExecutablePath\":null,\"Profiles\":[]}")]
    [InlineData("{\"Version\":1,\"ExecutablePath\":\"relative.exe\",\"Profiles\":[]}")]
    public void Invalid_catalogs_are_rejected(string json)
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, json); Assert.Throws<InvalidDataException>(() => CommandCatalog.Read(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Installation_discovery_does_not_treat_an_unrelated_cli_as_PowerToys()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ExactFrame-installation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "FancyZonesCLI.exe"), "test");
            Assert.Null(PowerToysInstallation.Find([folder]));
            File.WriteAllText(Path.Combine(folder, "PowerToys.exe"), "test");
            var found = PowerToysInstallation.Find([folder]);
            Assert.NotNull(found?.FancyZonesCli);
            Assert.Null(found!.AwakeExecutable);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    public void Dispose() => _main?.Dispose();

    private sealed class CatalogPublisher : ICommandCatalogPublisher
    {
        public int Publishes { get; private set; }
        public int Disables { get; private set; }
        public IReadOnlyList<CatalogProfile>? Profiles { get; private set; }
        public bool Fail { get; set; }
        public void Publish(IReadOnlyList<CatalogProfile> profiles)
        {
            Publishes++;
            if (Fail) throw new IOException("Read-only catalog folder");
            Profiles = profiles;
        }
        public void Disable() => Disables++;
    }

    private sealed class PowerToys : IPowerToysService
    {
        public PowerToysCapabilities Capabilities { get; set; } = new(false, false, false);
        public int Discoveries { get; private set; }
        public bool FailActions { get; set; }
        public List<string> AppliedLayouts { get; } = [];
        public Task<PowerToysCapabilities> DiscoverAsync(CancellationToken cancellationToken) { Discoveries++; return Task.FromResult(Capabilities); }
        public Task<IReadOnlyList<PowerToysLayout>> GetLayoutsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PowerToysLayout>>([new("columns", "Columns")]);
        public Task ApplyLayoutAsync(string layoutId, CancellationToken cancellationToken)
        {
            AppliedLayouts.Add(layoutId);
            return FailActions ? Task.FromException(new InvalidOperationException("FancyZones is unavailable.")) : Task.CompletedTask;
        }
        public Task OpenLayoutEditorAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAwakeAsync(int minutes, CancellationToken cancellationToken) => Task.CompletedTask;
        public void StopAwake() { }
    }
}
