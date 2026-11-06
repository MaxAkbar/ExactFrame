using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ExactFrame.Core.Integration;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ExactFrame.CommandPalette;

internal sealed partial class ExactFramePage : ListPage
{
    private readonly string _catalogPath;
    public ExactFramePage(string? catalogPath = null)
    {
        _catalogPath = catalogPath ?? CommandCatalog.DefaultPath;
        Title = "ExactFrame"; Name = "Open";
        Icon = IconHelpers.FromRelativePath("Assets/StoreLogo.png");
    }

    public override IListItem[] GetItems()
    {
        try
        {
            var catalog = CommandCatalog.Read(_catalogPath);
            if (catalog is null) return Explain("Enable sharing in ExactFrame", "Open Profiles → PowerToys (optional) → Share saved profiles. ExactFrame works normally with sharing off.");
            if (!File.Exists(catalog.ExecutablePath)) return Explain("ExactFrame has moved", "Open the portable app in its new folder, then refresh sharing in Profiles → PowerToys (optional).");
            var items = catalog.Profiles.Select(profile => Item(profile.Name, profile.Description,
                new LaunchFrameCommand(["--profile", profile.Id, "--show-outline", "--background"]))).ToList();
            items.AddRange(new[]
            {
                Item("Show outline", "Show the current recording frame", new LaunchFrameCommand(["--show-outline", "--background"])),
                Item("Hide outline", "Hide the recording frame", new LaunchFrameCommand(["--hide-outline", "--background"])),
                Item("Toggle outline", "Show or hide the recording frame", new LaunchFrameCommand(["--toggle-outline", "--background"])),
                Item("Toggle click-through", "Let clicks pass through the frame", new LaunchFrameCommand(["--toggle-click-through", "--background"])),
                Item("Copy frame bounds", "Copy X, Y, width and height", new LaunchFrameCommand(["--copy-bounds", "--background"])),
                Item("Capture in SnapStudio", "Capture the main frame as an editable screenshot", new LaunchFrameCommand(["--capture-snapstudio", "--background"])),
                Item("Send to OBS", "Open the preview and connection dialog", new LaunchFrameCommand(["--obs"])),
                Item("PowerToys setup", "Optional tools and sharing preferences", new LaunchFrameCommand(["--powertoys"])),
                Item("Open ExactFrame", "Open framing controls", new LaunchFrameCommand([]))
            });
            items.Add(RefreshItem());
            return items.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return Explain("Refresh ExactFrame sharing", "The profile list could not be read. Open ExactFrame and refresh sharing in Profiles → PowerToys (optional).");
        }
    }

    private ListItem Item(string title, string subtitle, ICommand command)
    {
        if (command is LaunchFrameCommand launch) launch.CatalogPath = _catalogPath;
        return new(command) { Title = title, Subtitle = subtitle };
    }
    private ListItem RefreshItem() => Item("Refresh profiles", "Reload the shared profile list", new AnonymousCommand(() => RaiseItemsChanged())
        { Result = CommandResult.KeepOpen() });
    private IListItem[] Explain(string title, string detail) => [Item(title, detail, new NoOpCommand()), RefreshItem()];
}

internal sealed partial class LaunchFrameCommand(string[] arguments) : InvokableCommand
{
    internal string CatalogPath { get; set; } = CommandCatalog.DefaultPath;
    internal Action<ProcessStartInfo>? LaunchForCheck { get; set; }
    public override ICommandResult Invoke()
    {
        try
        {
            // Re-read on every invocation, so turning sharing off invalidates existing palette results.
            var catalog = CommandCatalog.Read(CatalogPath);
            if (catalog is null || !File.Exists(catalog.ExecutablePath))
                return CommandResult.ShowToast("Open ExactFrame and enable Command Palette sharing.");
            var start = new ProcessStartInfo(catalog.ExecutablePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(catalog.ExecutablePath)! };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            if (LaunchForCheck is { } launch) launch(start);
            else { using var process = Process.Start(start); }
            return CommandResult.Dismiss();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or Win32Exception or InvalidOperationException)
        {
            return CommandResult.ShowToast("ExactFrame could not be opened. Open it directly and refresh Command Palette sharing.");
        }
    }
}
