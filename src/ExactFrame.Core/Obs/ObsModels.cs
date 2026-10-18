using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExactFrame.Core.Obs;

public sealed record ObsScene(string Name, string? Uuid = null);

public sealed record ObsSource(int ItemId, string Name, string? Uuid = null);

public sealed record ObsSceneList(IReadOnlyList<ObsScene> Scenes, string? CurrentSceneName);

public sealed record ObsRegion(string Key, string Name, Rectangle Bounds);

public enum ObsPlacement { FitToCanvas, ActualPixels }

public sealed record ObsVideoSize(int CanvasWidth, int CanvasHeight, int OutputWidth, int OutputHeight);

/// <summary>A read-only snapshot. Applying it requires a fresh comparison with OBS.</summary>
public sealed record ObsSnapshot(
    string Collection, ObsScene Scene, ObsSource Source, string MonitorId, Rectangle DisplayBounds,
    string DisplayName, ObsVideoSize Video, JsonElement Transform);

public sealed record ObsPlan(ObsSnapshot Before, ObsRegion Region, ObsPlacement Placement,
    int CropLeft, int CropTop, int CropRight, int CropBottom, double Scale, double X, double Y)
{
    public double Width => Region.Bounds.Width * Scale;
    public double Height => Region.Bounds.Height * Scale;

    public JsonElement Transform => JsonSerializer.SerializeToElement(new
    {
        cropLeft = CropLeft, cropTop = CropTop, cropRight = CropRight, cropBottom = CropBottom,
        scaleX = Scale, scaleY = Scale, positionX = X, positionY = Y,
        rotation = 0, alignment = 5, boundsType = "OBS_BOUNDS_NONE"
    });

    internal JsonElement ExpectedTransform
    {
        get
        {
            var expected = JsonNode.Parse(Before.Transform.GetRawText())!.AsObject();
            foreach (var property in Transform.EnumerateObject()) expected[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            expected.Remove("width"); expected.Remove("height"); // Derived from the new scale by OBS.
            return JsonSerializer.SerializeToElement(expected);
        }
    }
}

public sealed class ObsException(string message) : InvalidOperationException(message);

internal static class ObsJson
{
    internal static string? Text(this JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    internal static double Number(this JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.TryGetDouble(out double number) && double.IsFinite(number)
            ? number : throw new ObsException($"OBS did not provide a usable {name}. Refresh the connection.");

    internal static bool Flag(this JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.True;

    // Read-only dimensions are not accepted by SetSceneItemTransform. Zero bounds dimensions are also invalid.
    internal static JsonElement WritableTransform(JsonElement transform)
    {
        string[] fields = ["positionX", "positionY", "rotation", "scaleX", "scaleY", "alignment", "boundsType",
            "boundsAlignment", "boundsWidth", "boundsHeight", "cropLeft", "cropTop", "cropRight", "cropBottom", "cropToBounds"];
        var result = new JsonObject();
        foreach (string field in fields)
        {
            if (!transform.TryGetProperty(field, out var value)) continue;
            if (field is "boundsWidth" or "boundsHeight" && value.GetDouble() < 1) continue;
            result[field] = JsonNode.Parse(value.GetRawText());
        }
        return JsonSerializer.SerializeToElement(result);
    }

    internal static bool Matches(JsonElement actual, JsonElement expected) => expected.EnumerateObject().All(field =>
        actual.TryGetProperty(field.Name, out var value) &&
        (field.Value.ValueKind == JsonValueKind.Number && value.ValueKind == JsonValueKind.Number
            ? Math.Abs(field.Value.GetDouble() - value.GetDouble()) <= 0.001
            : JsonElement.DeepEquals(value, field.Value)));
}
