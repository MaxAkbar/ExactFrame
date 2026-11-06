using ExactFrame.Core.Integration;
using CommunityToolkit.Mvvm.Input;

namespace ExactFrame.Core.ViewModels;

public sealed partial class MainViewModel
{
    public event EventHandler? PowerToysRequested;
    public IRelayCommand OpenPowerToysCommand { get; }
    private string _powerToysProfileStatus = "";
    public string PowerToysProfileStatus
    {
        get => _powerToysProfileStatus;
        private set => SetProperty(ref _powerToysProfileStatus, value);
    }
    public bool HasProfilePowerToysLayout => ActiveProfile is { } profile && _settings.PowerToysProfileLayouts.ContainsKey(profile.Id);
    public string ProfilePowerToysSummary => ActiveProfile is not { } profile ? "Apply a saved profile to link a layout."
        : HasProfilePowerToysLayout ? $"“{profile.Name}” is linked to a FancyZones layout on its first monitor."
        : $"“{profile.Name}” has no FancyZones layout link.";

    public void LinkProfilePowerToysLayout(string? layoutId)
    {
        if (ActiveProfile is not { } profile) return;
        var previous = _settings.PowerToysProfileLayouts;
        var next = new Dictionary<string, string>(previous);
        if (layoutId is null) next.Remove(profile.Id);
        else next[profile.Id] = layoutId;
        _settings.PowerToysProfileLayouts = next;
        if (!SaveSettings()) _settings.PowerToysProfileLayouts = previous;
        else PowerToysProfileStatus = layoutId is null ? "Removed the layout link. The profile applies independently."
            : "Layout linked. Applying this profile will request that layout before sizing the frame.";
        OnPropertyChanged(nameof(ProfilePowerToysSummary)); OnPropertyChanged(nameof(HasProfilePowerToysLayout));
    }

    private async Task ApplyProfilePowerToysLayoutAsync(string profileId)
    {
        if (_powerToys is null || !_settings.PowerToysProfileLayouts.TryGetValue(profileId, out string? layout)) return;
        try
        {
            await _powerToys().ApplyLayoutAsync(layout, _lifetime.Token);
            PowerToysProfileStatus = "The linked FancyZones layout was applied before the profile.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsExpected(ex) || ex is System.Security.SecurityException)
        {
            PowerToysProfileStatus = $"The frame profile still applies. Its optional FancyZones layout was unavailable: {ex.Message}";
        }
    }
    private string _commandPaletteStatus = "Sharing is off. ExactFrame works independently of PowerToys.";
    public string CommandPaletteStatus
    {
        get => _commandPaletteStatus;
        private set => SetProperty(ref _commandPaletteStatus, value);
    }

    public bool ShareWithCommandPalette
    {
        get => _settings.ShareWithCommandPalette;
        set
        {
            if (_settings.ShareWithCommandPalette == value) return;
            _settings.ShareWithCommandPalette = value;
            if (!SaveSettings()) { _settings.ShareWithCommandPalette = !value; OnPropertyChanged(); return; }
            if (!value)
            {
                try { _commandCatalog?.Disable(); CommandPaletteStatus = "Sharing is off."; }
                catch (Exception ex) when (IsCatalogError(ex)) { CommandPaletteStatus = $"Sharing was turned off, but the old catalog could not be removed: {ex.Message}"; }
            }
            OnPropertyChanged();
        }
    }

    public void RefreshCommandCatalog()
    {
        if (!_settings.ShareWithCommandPalette || _commandCatalog is null) return;
        try
        {
            // Target paths, window titles and monitor identities are intentionally omitted.
            _commandCatalog.Publish(_settings.Profiles.Select(p => new CatalogProfile(p.Id, p.Name,
                $"{p.Width} × {p.Height} · {(p.Mode == Models.FrameMode.Resize ? "Resize window" : "Outline area")}")).ToArray());
            CommandPaletteStatus = "Profiles are available to the optional ExactFrame Command Palette companion.";
        }
        catch (Exception ex) when (IsCatalogError(ex))
        {
            // An optional integration failure must never fail a normal settings save or change the main status.
            CommandPaletteStatus = $"ExactFrame saved your setup, but could not refresh Command Palette: {ex.Message}";
        }
    }

    private static bool IsCatalogError(Exception ex) => ex is IOException or UnauthorizedAccessException or
        InvalidOperationException or ArgumentException or System.Text.Json.JsonException;

    /// <returns>False when the main window should be shown to explain an unavailable or ambiguous action.</returns>
    public async Task<bool> ExecuteLaunchAsync(LaunchCommand command)
    {
        if (IsBusy) { SetStatus("ExactFrame is busy. Try the command again when the current action finishes.", warning: true); return false; }
        try
        {
            if (command.Profile is { } selector)
            {
                var byId = _settings.Profiles.Where(p => string.Equals(p.Id, selector, StringComparison.Ordinal)).ToArray();
                var matches = byId.Length > 0 ? byId : _settings.Profiles.Where(p => string.Equals(p.Name, selector, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0
                    ? $"The saved profile “{selector}” was not found. Choose a profile in ExactFrame."
                    : $"More than one profile is named “{selector}”. Use the profile's ID instead.");
                await ApplyProfileAsync(matches[0]);
                if (_statusWarning || (matches[0].Mode == Models.FrameMode.Resize && _activeFrame is null)) return false;
            }
            foreach (var action in command.Actions)
            {
                switch (action)
                {
                    case FrameAction.ShowOutline: ShowOutline(CurrentFrame, validate: true); break;
                    case FrameAction.HideOutline: HideOutline(); break;
                    case FrameAction.ToggleOutline: ToggleOutline(); break;
                    case FrameAction.ToggleClickThrough: IsClickThrough = !IsClickThrough; break;
                    case FrameAction.CopyBounds: await CopyBoundsAsync(); break;
                    case FrameAction.CaptureSnapStudio:
                        if (!IsOutlineMode || _snapStudioCapture is null) throw new InvalidOperationException("Choose Outline area before capturing in SnapStudio.");
                        _snapStudioFrameId = "main";
                        await RunBusyAsync(CaptureInSnapStudioAsync);
                        break;
                }
                if (action is not (FrameAction.OpenObs or FrameAction.OpenPowerToys) && _statusWarning) return false;
            }
            // Opening controls must also work when the user is trying to resolve an earlier warning.
            return command.Profile is null && command.Actions.All(action => action is FrameAction.OpenObs or FrameAction.OpenPowerToys) || !_statusWarning;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (IsExpected(ex)) { ReportError(ex); return false; }
    }

    public void ReportLaunchError(string message) => SetStatus(message, warning: true);

    public string WorkspaceArguments => ActiveProfile is { } profile
        ? $"--profile {LaunchCommand.QuoteArgument(profile.Id)} --show-outline --background --delay-ms 2000"
        : "Save or apply a profile first.";
}
