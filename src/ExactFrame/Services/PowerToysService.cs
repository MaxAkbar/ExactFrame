using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ExactFrame.Core.Integration;
using Microsoft.Win32;

namespace ExactFrame.Services;

/// <summary>No discovery, process launch or settings access occurs in this service's constructor.</summary>
internal sealed class PowerToysService : IPowerToysService, IDisposable
{
    private readonly IReadOnlyList<string>? _installationRoots;
    private Process? _awake;

    internal PowerToysService(IReadOnlyList<string>? installationRoots = null) => _installationRoots = installationRoots;

    public Task<PowerToysCapabilities> DiscoverAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installation = FindInstallation();
        return new PowerToysCapabilities(installation is not null, installation?.FancyZonesCli is not null,
            installation?.AwakeExecutable is not null);
    }, cancellationToken);

    public Task<IReadOnlyList<PowerToysLayout>> GetLayoutsAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<PowerToysLayout>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var layouts = new List<PowerToysLayout>
        {
            new("focus", "Focus"), new("columns", "Columns"), new("rows", "Rows"), new("grid", "Grid"), new("priority-grid", "Priority grid")
        };
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "PowerToys", "FancyZones", "custom-layouts.json");
        if (!File.Exists(path)) return layouts;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The FancyZones layouts file is too large to read.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("custom-layouts", out var custom) && custom.ValueKind == JsonValueKind.Array)
        {
            foreach (var layout in custom.EnumerateArray())
                if (layout.TryGetProperty("uuid", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var uuid) &&
                    layout.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                    layouts.Add(new(uuid.ToString("D"), name.GetString()!));
        }
        return layouts;
    }, cancellationToken);

    public async Task ApplyLayoutAsync(string layoutId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(layoutId, out _) && layoutId is not ("focus" or "columns" or "rows" or "grid" or "priority-grid"))
            throw new ArgumentException("Choose a FancyZones layout first.");
        // FancyZones defaults to the first monitor in its own list; this isn't a Windows display index.
        await RunCliAsync(["set-layout", layoutId], cancellationToken);
    }

    public async Task OpenLayoutEditorAsync(CancellationToken cancellationToken) =>
        await RunCliAsync(["open-editor"], cancellationToken);

    public async Task StartAwakeAsync(int minutes, CancellationToken cancellationToken)
    {
        if (minutes is < 1 or > 240) throw new ArgumentException("Choose an Awake duration from 1 to 240 minutes.");
        cancellationToken.ThrowIfCancellationRequested();
        var installation = FindInstallation();
        string executable = installation?.AwakeExecutable ?? throw new InvalidOperationException("PowerToys Awake is unavailable. ExactFrame continues to work normally.");
        if (_awake is { HasExited: false }) throw new InvalidOperationException("This ExactFrame Awake session is already running. Stop it before starting another.");
        var existing = Process.GetProcessesByName("PowerToys.Awake");
        try
        {
            if (existing.Length > 0) throw new InvalidOperationException("Awake is already running. Manage that session in PowerToys; ExactFrame will leave it as it is.");
        }
        finally { foreach (var process in existing) process.Dispose(); }
        _awake?.Dispose();
        var start = NewProcess(executable, ["--time-limit", (minutes * 60).ToString(CultureInfo.InvariantCulture),
            "--display-on", "true", "--pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)]);
        _awake = Process.Start(start) ?? throw new InvalidOperationException("PowerToys Awake could not be started.");
        // Report immediate failures rather than claiming a session has started when the executable rejected it.
        await Task.Delay(300, cancellationToken);
        if (_awake.HasExited && _awake.ExitCode != 0) throw new InvalidOperationException("PowerToys Awake could not start. Check your PowerToys installation.");
    }

    public void StopAwake()
    {
        if (_awake is null) return;
        try { if (!_awake.HasExited) _awake.Kill(); }
        catch (InvalidOperationException) { /* The owned process exited between checking and stopping it. */ }
        finally { _awake.Dispose(); _awake = null; }
    }

    public void Dispose()
    {
        try { StopAwake(); }
        catch (System.ComponentModel.Win32Exception) { /* The PID-bound timer also ends when ExactFrame exits. */ }
    }

    private PowerToysInstallation? FindInstallation()
    {
        if (_installationRoots is not null) return PowerToysInstallation.Find(_installationRoots);
        var roots = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\PowerToys.exe");
            if (key?.GetValue(null) is string path && Path.IsPathFullyQualified(path))
                roots.Add(Path.GetDirectoryName(path)!);
        }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerToys"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerToys"));
        return PowerToysInstallation.Find(roots);
    }

    private async Task RunCliAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string executable = FindInstallation()?.FancyZonesCli ??
            throw new InvalidOperationException("The FancyZones command tool is unavailable. Install a current PowerToys version to use this optional action.");
        var start = NewProcess(executable, arguments);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The FancyZones command tool could not be started.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            string text = (await output) + " " + (await error);
            if (process.ExitCode != 0) throw new InvalidOperationException(text.Trim() is { Length: > 0 } detail
                ? detail[..Math.Min(detail.Length, 1000)] : "FancyZones did not accept the action. Enable FancyZones in PowerToys and try again.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("FancyZones did not respond in time. ExactFrame is still available.");
        }
        finally { if (!process.HasExited) process.Kill(); }
    }

    private static ProcessStartInfo NewProcess(string executable, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
}
