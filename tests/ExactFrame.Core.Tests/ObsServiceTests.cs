using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExactFrame.Core.Obs;

namespace ExactFrame.Core.Tests;

internal sealed class FakeObsClient : IObsClient
{
    public bool IsConnected { get; private set; } = true;
    public bool Recording, Streaming, Locked, Filtered, Hidden, RejectSet, IgnoreSet;
    public string Collection = "Tutorials", MonitorId = FakeDisplays.Primary.DeviceName, SourceUuid = "source-1";
    public string InputKind = "monitor_capture";
    public List<(string Type, JsonElement Data)> Calls { get; } = [];
    public JsonObject Transform { get; } = JsonNode.Parse("""
        {"sourceWidth":3840,"sourceHeight":2160,"positionX":77,"positionY":88,"rotation":15,
         "scaleX":-0.5,"scaleY":0.5,"alignment":0,"boundsType":"OBS_BOUNDS_NONE",
         "boundsWidth":0,"boundsHeight":0,"boundsAlignment":0,"cropLeft":3,"cropTop":4,"cropRight":5,"cropBottom":6,"cropToBounds":false}
        """)!.AsObject();
    public Task ConnectAsync(int port, string password, CancellationToken cancellation = default) { IsConnected = true; return Task.CompletedTask; }
    public void Disconnect() => IsConnected = false;
    public void Dispose() => Disconnect();
    public Task<JsonElement> RequestAsync(string requestType, object? data = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var request = JsonSerializer.SerializeToElement(data ?? new { });
        Calls.Add((requestType, request));
        object response = requestType switch
        {
            "GetVersion" => new { availableRequests = ObsService.RequiredRequests },
            "GetSceneList" => new { currentProgramSceneName = "Tutorial", scenes = new[] { new { sceneName = "Tutorial", sceneUuid = "scene-1" } } },
            "GetSceneCollectionList" => new { currentSceneCollectionName = Collection },
            "GetSceneItemList" => new { sceneItems = new[] { new { sceneItemId = 7, sourceName = "Desktop", sourceUuid = SourceUuid,
                inputKind = InputKind, isGroup = false, sceneItemEnabled = !Hidden, sceneItemLocked = Locked } } },
            "GetInputSettings" => new { inputKind = InputKind, inputSettings = new { monitor_id = MonitorId } },
            "GetSourceFilterList" => new { filters = Filtered ? new[] { new { filterEnabled = true } } : [] },
            "GetVideoSettings" => new { baseWidth = 1920, baseHeight = 1080, outputWidth = 1280, outputHeight = 720 },
            "GetSceneItemTransform" => new { sceneItemTransform = Transform },
            "GetRecordStatus" => new { outputActive = Recording },
            "GetStreamStatus" => new { outputActive = Streaming },
            "SetSceneItemTransform" => Apply(request),
            _ => throw new InvalidOperationException(requestType)
        };
        return Task.FromResult(JsonSerializer.SerializeToElement(response));
    }
    private object Apply(JsonElement request)
    {
        if (RejectSet) throw new ObsException("OBS rejected the transform.");
        if (!IgnoreSet)
            foreach (var field in request.GetProperty("sceneItemTransform").EnumerateObject())
                Transform[field.Name] = JsonNode.Parse(field.Value.GetRawText());
        return new { };
    }
}

public sealed class ObsServiceTests
{
    private readonly FakeObsClient _client = new();
    private readonly FakeDisplays _displays = new();
    private static readonly ObsScene Scene = new("Tutorial", "scene-1");
    private static readonly ObsSource Source = new(7, "Desktop", "source-1");
    private static readonly ObsRegion Region = new("main", "Main", new Rectangle(320, 240, 1280, 720));
    private ObsService Create() => new(_client, _displays);
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private Task<ObsPlan> Preview(ObsService service) => service.PreviewAsync(Scene, Source, Region, ObsPlacement.FitToCanvas, Cancellation);
    private void AssertNoMutation() => Assert.DoesNotContain(_client.Calls, c => c.Type.StartsWith("Set", StringComparison.Ordinal));

    [Fact]
    public async Task Connect_checks_capabilities_and_preview_never_writes()
    {
        using var service = Create();
        await service.ConnectAsync(4455, "not-persisted", Cancellation);
        Assert.Single((await service.GetScenesAsync(Cancellation)).Scenes);
        Assert.Single(await service.GetSourcesAsync(Scene, Cancellation));
        var plan = await Preview(service);
        Assert.Equal((320, 240, 2240, 1200), (plan.CropLeft, plan.CropTop, plan.CropRight, plan.CropBottom));
        AssertNoMutation();
    }

    [Fact]
    public async Task Apply_changes_only_the_chosen_item_and_undo_restores_its_original_transform()
    {
        using var service = Create();
        string original = _client.Transform.ToJsonString();
        await service.ApplyAsync(await Preview(service), Cancellation);
        Assert.True(service.CanUndo);
        var write = Assert.Single(_client.Calls, c => c.Type.StartsWith("Set", StringComparison.Ordinal));
        Assert.Equal("SetSceneItemTransform", write.Type);
        Assert.Equal(7, write.Data.GetProperty("sceneItemId").GetInt32());
        Assert.Equal("scene-1", write.Data.GetProperty("sceneUuid").GetString());
        Assert.Equal(1.5, _client.Transform["scaleX"]!.GetValue<double>());
        await service.UndoAsync(Cancellation);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), _client.Transform));
        Assert.False(service.CanUndo);
    }

    [Theory]
    [InlineData("transform")]
    [InlineData("monitor")]
    [InlineData("collection")]
    [InlineData("identity")]
    [InlineData("filter")]
    [InlineData("locked")]
    public async Task Changed_context_is_rejected_before_any_write(string change)
    {
        using var service = Create();
        var plan = await Preview(service);
        switch (change)
        {
            case "transform": _client.Transform["positionX"] = 900; break;
            case "monitor": _client.MonitorId = FakeDisplays.Secondary.DeviceName; break;
            case "collection": _client.Collection = "Different collection"; break;
            case "identity": _client.SourceUuid = "replacement-source"; break;
            case "filter": _client.Filtered = true; break;
            case "locked": _client.Locked = true; break;
        }
        await Assert.ThrowsAsync<ObsException>(() => service.ApplyAsync(plan, Cancellation));
        AssertNoMutation();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Active_output_prevents_applying(bool recording, bool streaming)
    {
        using var service = Create();
        var plan = await Preview(service);
        _client.Recording = recording; _client.Streaming = streaming;
        await Assert.ThrowsAsync<ObsException>(() => service.ApplyAsync(plan, Cancellation));
        AssertNoMutation();
    }

    [Fact]
    public async Task Undo_preserves_an_edit_made_in_OBS_after_apply()
    {
        using var service = Create();
        await service.ApplyAsync(await Preview(service), Cancellation);
        _client.Transform["positionY"] = 456;
        _client.Calls.Clear();
        await Assert.ThrowsAsync<ObsException>(() => service.UndoAsync(Cancellation));
        AssertNoMutation();
        Assert.Equal(456, _client.Transform["positionY"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("boundsWidth")]
    [InlineData("boundsAlignment")]
    public async Task Undo_also_preserves_edits_to_fields_that_ExactFrame_did_not_change(string field)
    {
        using var service = Create();
        await service.ApplyAsync(await Preview(service), Cancellation);
        _client.Transform[field] = 12;
        _client.Calls.Clear();
        await Assert.ThrowsAsync<ObsException>(() => service.UndoAsync(Cancellation));
        AssertNoMutation();
    }

    [Fact]
    public async Task Monitor_interface_path_identifies_same_size_displays_without_guessing()
    {
        var primary = FakeDisplays.Primary with { MonitorDevicePath = "monitor-A" };
        var secondary = primary with { DeviceName = "DISPLAY2", Number = 2, Bounds = new(-3840, 0, 3840, 2160), MonitorDevicePath = "monitor-B" };
        _displays.Displays.Clear(); _displays.Displays.AddRange([primary, secondary]);
        _client.MonitorId = "MONITOR-b";
        using var service = Create();
        var plan = await service.PreviewAsync(Scene, Source, new("main", "Main", new(-3500, 100, 1280, 720)), ObsPlacement.FitToCanvas, Cancellation);
        Assert.Equal(340, plan.CropLeft);
        Assert.Equal("Display 2", plan.Before.DisplayName);
    }

    [Fact]
    public async Task Missing_monitor_is_not_guessed_from_dimensions()
    {
        _client.MonitorId = "unknown-monitor";
        using var service = Create();
        await Assert.ThrowsAsync<ObsException>(() => Preview(service));
        AssertNoMutation();
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("filtered")]
    [InlineData("locked")]
    public async Task Unusable_source_is_rejected_during_preview(string condition)
    {
        _client.Hidden = condition == "hidden"; _client.Filtered = condition == "filtered"; _client.Locked = condition == "locked";
        using var service = Create();
        await Assert.ThrowsAsync<ObsException>(() => Preview(service));
        AssertNoMutation();
    }

    [Fact]
    public async Task Rejected_write_keeps_recovery_and_undo_recognizes_an_unchanged_item()
    {
        using var service = Create();
        var plan = await Preview(service);
        _client.RejectSet = true;
        await Assert.ThrowsAsync<ObsException>(() => service.ApplyAsync(plan, Cancellation));
        Assert.True(service.CanUndo);
        _client.Calls.Clear();
        await service.UndoAsync(Cancellation);
        AssertNoMutation();
        Assert.False(service.CanUndo);
    }

    [Fact]
    public async Task A_success_response_with_an_unchanged_transform_is_not_reported_as_verified()
    {
        using var service = Create();
        _client.IgnoreSet = true;
        var plan = await Preview(service);
        await Assert.ThrowsAsync<ObsException>(() => service.ApplyAsync(plan, Cancellation));
    }
}
