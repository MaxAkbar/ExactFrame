namespace ExactFrame.Core.Integration;

public sealed record PowerToysCapabilities(bool Installed, bool FancyZones, bool Awake);
public sealed record PowerToysLayout(string Id, string Name);

/// <summary>Discovery and process launches happen only when the optional tools are deliberately opened or used.</summary>
public interface IPowerToysService
{
    Task<PowerToysCapabilities> DiscoverAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PowerToysLayout>> GetLayoutsAsync(CancellationToken cancellationToken);
    Task ApplyLayoutAsync(string layoutId, CancellationToken cancellationToken);
    Task OpenLayoutEditorAsync(CancellationToken cancellationToken);
    Task StartAwakeAsync(int minutes, CancellationToken cancellationToken);
    void StopAwake();
}
