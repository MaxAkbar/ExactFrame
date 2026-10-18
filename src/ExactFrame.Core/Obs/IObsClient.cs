using System.Text.Json;

namespace ExactFrame.Core.Obs;

public interface IObsClient : IDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(int port, string password, CancellationToken cancellation = default);
    Task<JsonElement> RequestAsync(string requestType, object? data = null, CancellationToken cancellation = default);
    void Disconnect();
}

public interface IObsService : IDisposable
{
    bool IsConnected { get; }
    bool CanUndo { get; }
    Task ConnectAsync(int port, string password, CancellationToken cancellation = default);
    void Disconnect();
    Task<ObsSceneList> GetScenesAsync(CancellationToken cancellation = default);
    Task<IReadOnlyList<ObsSource>> GetSourcesAsync(ObsScene scene, CancellationToken cancellation = default);
    Task<ObsPlan> PreviewAsync(ObsScene scene, ObsSource source, ObsRegion region, ObsPlacement placement,
        CancellationToken cancellation = default);
    Task ApplyAsync(ObsPlan plan, CancellationToken cancellation = default);
    Task UndoAsync(CancellationToken cancellation = default);
}
