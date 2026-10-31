using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;

namespace ExactFrame.Core.Services;

/// <summary>SnapStudio external-capture protocol v1. No shared document storage or screen pixels cross this pipe.</summary>
public static class SnapStudioCaptureConnection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static string DefaultPipeName => Environment.GetEnvironmentVariable("SNAPSTUDIO_INTEGRATION_PIPE")
        ?? $"SnapStudio.ExternalCapture.v1.Session{(OperatingSystem.IsWindows() ? Process.GetCurrentProcess().SessionId : 0)}";
    public static void ValidateRegion(Rectangle region)
    {
        if (region.Width < 4 || region.Height < 4 || region.Width > 20000 || region.Height > 20000
            || (long)region.Width * region.Height > 40000000 || (long)region.X + region.Width > int.MaxValue || (long)region.Y + region.Height > int.MaxValue)
            throw new InvalidOperationException("Choose a frame between 4 and 20,000 pixels per side and at most 40 million pixels.");
    }
    public static void ValidateApplication(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !string.Equals(Path.GetFileName(path), "SnapStudio.App.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose SnapStudio.App.exe from its extracted portable folder.");
        string marker = Path.Combine(Path.GetDirectoryName(path)!, "SnapStudio.integration.json");
        try
        {
            if (!File.Exists(marker) || new FileInfo(marker).Length > 4096) throw new InvalidDataException();
            using var document = JsonDocument.Parse(File.ReadAllText(marker));
            if (document.RootElement.GetProperty("externalCaptureProtocol").GetInt32() != 1) throw new InvalidDataException();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new InvalidOperationException("This SnapStudio copy does not support ExactFrame capture. Choose SnapStudio 1.2.0 or newer from its extracted portable folder.", error); }
    }
    public static async Task RequestAsync(Stream pipe, Rectangle region, string name, CancellationToken cancellation)
    {
        ValidateRegion(region);
        Guid id = Guid.NewGuid();
        var request = new { protocolVersion = 1, requestId = id, x = region.X, y = region.Y, width = region.Width, height = region.Height, name };
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions) + "\n"), cancellation).ConfigureAwait(false);
        using var bytes = new MemoryStream(); var next = new byte[1];
        while (bytes.Length < 8192)
        {
            if (await pipe.ReadAsync(next, cancellation).ConfigureAwait(false) == 0) throw new IOException("SnapStudio closed the capture connection.");
            if (next[0] == (byte)'\n')
            {
                JsonDocument response;
                try { response = JsonDocument.Parse(bytes.ToArray()); }
                catch (JsonException error) { throw new InvalidDataException("SnapStudio returned an invalid capture response.", error); }
                using var responseLifetime = response;
                var root = response.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("protocolVersion", out var version)
                    || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int protocol) || protocol != 1
                    || !root.TryGetProperty("requestId", out var correlation) || correlation.ValueKind != JsonValueKind.String || !correlation.TryGetGuid(out Guid receivedId) || receivedId != id
                    || !root.TryGetProperty("succeeded", out var succeeded) || succeeded.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("SnapStudio returned an unexpected capture response.");
                if (!succeeded.GetBoolean()) throw new InvalidOperationException(root.TryGetProperty("error", out var message) && message.ValueKind == JsonValueKind.String
                    ? message.GetString() : "SnapStudio could not capture the frame.");
                return;
            }
            bytes.WriteByte(next[0]);
        }
        throw new InvalidDataException("SnapStudio returned an oversized capture response.");
    }
}
