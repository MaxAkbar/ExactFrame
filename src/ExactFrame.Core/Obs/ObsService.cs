using System.Text.Json;
using ExactFrame.Core.Services;

namespace ExactFrame.Core.Obs;

/// <summary>Changes one top-level Display Capture scene item; never edits inputs, video settings or output state.</summary>
public sealed class ObsService(IObsClient client, IDisplayService displays) : IObsService
{
    private ObsPlan? _undo;
    internal static readonly string[] RequiredRequests = ["GetVersion", "GetSceneList", "GetSceneCollectionList",
        "GetSceneItemList", "GetInputSettings", "GetSourceFilterList", "GetSceneItemTransform", "GetVideoSettings",
        "SetSceneItemTransform", "GetRecordStatus", "GetStreamStatus"];
    public bool IsConnected => client.IsConnected;
    public bool CanUndo => IsConnected && _undo is not null;

    public async Task ConnectAsync(int port, string password, CancellationToken cancellation = default)
    {
        _undo = null;
        try
        {
            await client.ConnectAsync(port, password, cancellation);
            var version = await client.RequestAsync("GetVersion", cancellation: cancellation);
            var available = version.GetProperty("availableRequests").EnumerateArray().Select(x => x.GetString()).ToHashSet();
            if (RequiredRequests.Any(x => !available.Contains(x)))
                throw new ObsException("This OBS server is missing requests needed for framing. Update OBS Studio and reconnect.");
        }
        catch { client.Disconnect(); throw; }
    }

    public void Disconnect() { _undo = null; client.Disconnect(); }
    public void Dispose() => Disconnect();

    public async Task<ObsSceneList> GetScenesAsync(CancellationToken cancellation = default)
    {
        var data = await client.RequestAsync("GetSceneList", cancellation: cancellation);
        return new ObsSceneList(data.GetProperty("scenes").EnumerateArray()
            .Select(s => new ObsScene(s.GetProperty("sceneName").GetString()!, s.Text("sceneUuid"))).ToArray(),
            data.Text("currentProgramSceneName"));
    }

    public async Task<IReadOnlyList<ObsSource>> GetSourcesAsync(ObsScene scene, CancellationToken cancellation = default)
    {
        var data = await client.RequestAsync("GetSceneItemList", SceneData(scene), cancellation);
        return data.GetProperty("sceneItems").EnumerateArray()
            .Where(s => s.Text("inputKind") == "monitor_capture" && !s.Flag("isGroup"))
            .Select(s => new ObsSource(s.GetProperty("sceneItemId").GetInt32(), s.GetProperty("sourceName").GetString()!, s.Text("sourceUuid")))
            .ToArray();
    }

    public async Task<ObsPlan> PreviewAsync(ObsScene scene, ObsSource source, ObsRegion region, ObsPlacement placement,
        CancellationToken cancellation = default) =>
        ObsFrameMapper.Create(await SnapshotAsync(scene, source, cancellation), region, placement);

    public async Task ApplyAsync(ObsPlan plan, CancellationToken cancellation = default)
    {
        var current = await SnapshotAsync(plan.Before.Scene, plan.Before.Source, cancellation);
        if (!SameContext(current, plan.Before) || !ObsJson.Matches(current.Transform, plan.Before.Transform))
            throw new ObsException("The source, display, scene collection or OBS layout changed after this preview. Preview the change again.");
        // Recompute rather than trusting a caller's transform, and check outputs immediately before writing.
        var checkedPlan = ObsFrameMapper.Create(current, plan.Region, plan.Placement);
        await RequireIdleAsync(cancellation);
        _undo = checkedPlan; // Keep recovery information even if the request's response is lost.
        await SetTransformAsync(current, checkedPlan.Transform, cancellation);
        var actual = await ReadTransformAsync(current.Scene, current.Source, cancellation);
        if (!ObsJson.Matches(actual, checkedPlan.Transform))
            throw new ObsException("OBS did not retain the proposed transform. Check the item in OBS; Undo last apply is available if the change can be verified.");
    }

    public async Task UndoAsync(CancellationToken cancellation = default)
    {
        var plan = _undo ?? throw new ObsException("There is no OBS change to undo in this connection.");
        var current = await SnapshotAsync(plan.Before.Scene, plan.Before.Source, cancellation);
        if (!SameContext(current, plan.Before))
            throw new ObsException("The OBS source or scene collection changed. The previous layout cannot be restored safely.");
        var original = ObsJson.WritableTransform(plan.Before.Transform);
        if (ObsJson.Matches(current.Transform, original)) { _undo = null; return; }
        if (!ObsJson.Matches(current.Transform, plan.ExpectedTransform))
            throw new ObsException("This item was edited in OBS after applying the frame. Undo would overwrite that edit; restore it in OBS instead.");
        await RequireIdleAsync(cancellation);
        await SetTransformAsync(current, original, cancellation);
        var actual = await ReadTransformAsync(current.Scene, current.Source, cancellation);
        if (!ObsJson.Matches(actual, original)) throw new ObsException("OBS did not restore the previous layout. Check the item in OBS.");
        _undo = null;
    }

    private async Task<ObsSnapshot> SnapshotAsync(ObsScene scene, ObsSource source, CancellationToken cancellation)
    {
        var collection = await client.RequestAsync("GetSceneCollectionList", cancellation: cancellation);
        var items = await client.RequestAsync("GetSceneItemList", SceneData(scene), cancellation);
        var candidates = items.GetProperty("sceneItems").EnumerateArray().Where(i =>
            i.GetProperty("sceneItemId").GetInt32() == source.ItemId && i.Text("sourceName") == source.Name &&
            i.Text("sourceUuid") == source.Uuid).ToArray();
        if (candidates.Length != 1 || candidates[0].Text("inputKind") != "monitor_capture" || candidates[0].Flag("isGroup"))
            throw new ObsException("This Display Capture item is no longer available. Refresh the OBS scenes and sources.");
        if (candidates[0].Flag("sceneItemLocked")) throw new ObsException("Unlock this scene item in OBS before applying a frame.");
        if (!candidates[0].Flag("sceneItemEnabled")) throw new ObsException("Make this Display Capture item visible in OBS before previewing it.");

        var input = await client.RequestAsync("GetInputSettings", InputData(source), cancellation);
        if (input.Text("inputKind") != "monitor_capture") throw new ObsException("Choose a Windows Display Capture source.");
        string? monitorId = input.GetProperty("inputSettings").Text("monitor_id");
        var matchingDisplays = displays.GetDisplays().Where(d => !string.IsNullOrWhiteSpace(monitorId) &&
            (string.Equals(d.MonitorDevicePath, monitorId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(d.DeviceName, monitorId, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matchingDisplays.Length != 1)
            throw new ObsException("The display captured by this OBS source could not be identified on this PC. Choose its display in OBS source properties, then refresh.");
        var display = matchingDisplays[0];

        var filters = await client.RequestAsync("GetSourceFilterList", new { sourceName = source.Name, sourceUuid = source.Uuid }, cancellation);
        if (filters.GetProperty("filters").EnumerateArray().Any(f => f.Flag("filterEnabled")))
            throw new ObsException("This source has enabled filters, which can change its pixel coordinates. Use an unfiltered Display Capture source for ExactFrame.");
        var video = await client.RequestAsync("GetVideoSettings", cancellation: cancellation);
        var size = new ObsVideoSize((int)video.Number("baseWidth"), (int)video.Number("baseHeight"),
            (int)video.Number("outputWidth"), (int)video.Number("outputHeight"));
        return new ObsSnapshot(collection.GetProperty("currentSceneCollectionName").GetString()!, scene, source, monitorId!,
            display.Bounds, display.Title, size, await ReadTransformAsync(scene, source, cancellation));
    }

    private async Task RequireIdleAsync(CancellationToken cancellation)
    {
        var recording = await client.RequestAsync("GetRecordStatus", cancellation: cancellation);
        var streaming = await client.RequestAsync("GetStreamStatus", cancellation: cancellation);
        if (recording.Flag("outputActive") || streaming.Flag("outputActive"))
            throw new ObsException("Stop recording and streaming in OBS before changing the capture layout.");
    }

    private async Task<JsonElement> ReadTransformAsync(ObsScene scene, ObsSource source, CancellationToken cancellation) =>
        (await client.RequestAsync("GetSceneItemTransform", ItemData(scene, source), cancellation)).GetProperty("sceneItemTransform").Clone();

    private Task<JsonElement> SetTransformAsync(ObsSnapshot snapshot, JsonElement transform, CancellationToken cancellation) =>
        client.RequestAsync("SetSceneItemTransform", new { sceneName = snapshot.Scene.Name, sceneUuid = snapshot.Scene.Uuid,
            sceneItemId = snapshot.Source.ItemId, sceneItemTransform = transform }, cancellation);

    private static object SceneData(ObsScene scene) => new { sceneName = scene.Name, sceneUuid = scene.Uuid };
    private static object InputData(ObsSource source) => new { inputName = source.Name, inputUuid = source.Uuid };
    private static object ItemData(ObsScene scene, ObsSource source) => new { sceneName = scene.Name, sceneUuid = scene.Uuid, sceneItemId = source.ItemId };
    private static bool SameContext(ObsSnapshot a, ObsSnapshot b) => a.Collection == b.Collection && a.Scene == b.Scene &&
        a.Source == b.Source && a.MonitorId == b.MonitorId && a.DisplayBounds == b.DisplayBounds && a.Video == b.Video;
}
