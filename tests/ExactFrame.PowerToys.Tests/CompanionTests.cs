extern alias palette;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ExactFrame.Core.Integration;
using ExactFrame.Services;
using Microsoft.CommandPalette.Extensions;
using Page = palette::ExactFrame.CommandPalette.ExactFramePage;
using Launch = palette::ExactFrame.CommandPalette.LaunchFrameCommand;

namespace ExactFrame.PowerToys.Tests;

public sealed class CompanionTests : IDisposable
{
    [Theory]
    [InlineData(new string[] { }, false)]
    [InlineData(new[] { "-RegisterProcessAsComServer" }, true)]
    [InlineData(new[] { "-RegisterProcessAsComServer", "-Embedding" }, true)]
    [InlineData(new[] { "--unknown" }, false)]
    public void Com_activation_accepts_the_additional_embedding_argument(string[] arguments, bool expected) =>
        Assert.Equal(expected, palette::ExactFrame.CommandPalette.Program.IsServerLaunch(arguments));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ExactFrame-palette-check-" + Guid.NewGuid().ToString("N"));
    private string CatalogPath => Path.Combine(_folder, "command-palette.json");

    public CompanionTests() => Directory.CreateDirectory(_folder);

    private CommandCatalogPublisher Publish()
    {
        string executable = Path.Combine(_folder, "ExactFrame.exe");
        File.WriteAllText(executable, "Fixture; never executed");
        var publisher = new CommandCatalogPublisher(CatalogPath, executable);
        publisher.Publish([new("demo", "Demo 1080p", "1920 × 1080 · Outline area")]);
        return publisher;
    }

    [Fact]
    public void Missing_catalog_shows_setup_help_and_refresh_without_launching_anything()
    {
        var items = new Page(CatalogPath).GetItems();
        Assert.Contains(items, item => item.Title == "Enable sharing in ExactFrame");
        Assert.Contains(items, item => item.Title == "Refresh profiles");
        Assert.DoesNotContain(items, item => item.Command is Launch);
    }

    [Fact]
    public void Profiles_and_supported_commands_are_exposed_through_the_real_SDK()
    {
        Publish();
        var items = new Page(CatalogPath).GetItems();
        Assert.Contains(items, item => item.Title == "Demo 1080p" && item.Subtitle.Contains("1920"));
        Assert.Contains(items, item => item.Title == "Copy frame bounds");
        Assert.Contains(items, item => item.Title == "Send to OBS");
        Assert.Contains(items, item => item.Title == "Capture in SnapStudio");
    }

    [Fact]
    public void Disabling_sharing_invalidates_a_command_that_was_already_visible()
    {
        var publisher = Publish();
        var item = new Page(CatalogPath).GetItems().Single(item => item.Title == "Demo 1080p");
        var launch = Assert.IsType<Launch>(item.Command);
        bool started = false;
        launch.LaunchForCheck = _ => started = true;
        publisher.Disable();
        Assert.Equal(CommandResultKind.ShowToast, launch.Invoke().Kind);
        Assert.False(started);
    }

    [Fact]
    public void Profile_launch_uses_separate_arguments_and_no_shell()
    {
        Publish();
        var item = new Page(CatalogPath).GetItems().Single(item => item.Title == "Demo 1080p");
        var launch = Assert.IsType<Launch>(item.Command);
        ProcessStartInfo? received = null;
        launch.LaunchForCheck = start => received = start;
        Assert.Equal(CommandResultKind.Dismiss, launch.Invoke().Kind);
        Assert.NotNull(received);
        Assert.False(received.UseShellExecute);
        Assert.Equal(new[] { "--profile", "demo", "--show-outline", "--background" }, received.ArgumentList);
    }

    [Fact]
    public void Refresh_notifies_the_host_and_reads_updated_profiles()
    {
        var publisher = Publish();
        var page = new Page(CatalogPath);
        var items = page.GetItems();
        bool changed = false;
        page.ItemsChanged += (_, _) => changed = true;
        publisher.Publish([new("new", "New profile", "1280 × 720")]);
        var refresh = Assert.IsAssignableFrom<IInvokableCommand>(items.Single(item => item.Title == "Refresh profiles").Command);
        Assert.Equal(CommandResultKind.KeepOpen, refresh.Invoke(page).Kind);
        Assert.True(changed);
        Assert.Contains(page.GetItems(), item => item.Title == "New profile");
        Assert.DoesNotContain(page.GetItems(), item => item.Title == "Demo 1080p");
    }

    [Fact]
    public void Corrupt_catalog_is_an_informational_result_instead_of_a_failed_extension()
    {
        File.WriteAllText(CatalogPath, "not json");
        Assert.Contains(new Page(CatalogPath).GetItems(), item => item.Title == "Refresh ExactFrame sharing");
    }

    [Fact]
    public async Task Production_service_handles_a_machine_without_PowerToys()
    {
        using var service = new PowerToysService([_folder]);
        Assert.Equal(new PowerToysCapabilities(false, false, false), await service.DiscoverAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyLayoutAsync("columns", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAwakeAsync(60, TestContext.Current.CancellationToken));
        service.StopAwake();
    }

    [Fact]
    public async Task An_older_installation_does_not_claim_missing_utilities_are_available()
    {
        File.WriteAllText(Path.Combine(_folder, "PowerToys.exe"), "Marker only");
        using var service = new PowerToysService([_folder]);
        Assert.Equal(new PowerToysCapabilities(true, false, false), await service.DiscoverAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenLayoutEditorAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("demo with spaces")]
    [InlineData("demo\"quoted")]
    [InlineData("demo\\")]
    [InlineData("demo\\\"quoted\\")]
    public void Workspace_profile_IDs_round_trip_through_Windows_quoting(string id)
    {
        string command = "ExactFrame.exe --profile " + LaunchCommand.QuoteArgument(id) + " --show-outline";
        nint buffer = CommandLineToArgvW(command, out int count);
        Assert.NotEqual(0, buffer);
        try
        {
            var arguments = Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, i * nint.Size))!).ToArray();
            Assert.Equal(id, LaunchCommand.Parse(arguments).Profile);
        }
        finally { LocalFree(buffer); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
}
