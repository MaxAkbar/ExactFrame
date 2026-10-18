using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExactFrame.Core.Obs;

/// <summary>OBS WebSocket 5 / RPC 1. Serial requests, bounded messages and timeouts; no event subscriptions.</summary>
public sealed class ObsWebSocketClient(TimeSpan? timeout = null) : IObsClient
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(8);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientWebSocket? _socket;
    private bool _identified;
    private bool _disposed;
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private sealed class RequestRejected(string message) : Exception(message);
    public bool IsConnected => _identified && _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(int port, string password, CancellationToken cancellation = default)
    {
        if (port is < 1 or > 65535) throw new ObsException("Choose an OBS server port between 1 and 65535.");
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Disconnect();
            using var limit = Limit(cancellation);
            var socket = new ClientWebSocket();
            _socket = socket;
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), limit.Token).ConfigureAwait(false);
            var hello = await ReceiveAsync(socket, limit.Token).ConfigureAwait(false);
            if (hello.GetProperty("op").GetInt32() != 0 || hello.GetProperty("d").Number("rpcVersion") < 1)
                throw new ObsException("This server is not compatible with OBS WebSocket 5. Use OBS Studio 28 or later.");
            var identify = new Dictionary<string, object> { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
            if (hello.GetProperty("d").TryGetProperty("authentication", out var auth))
                identify["authentication"] = Authentication(password, auth.GetProperty("salt").GetString()!, auth.GetProperty("challenge").GetString()!);
            await SendAsync(socket, new { op = 1, d = identify }, limit.Token).ConfigureAwait(false);
            var response = await ReceiveAsync(socket, limit.Token).ConfigureAwait(false);
            if (response.GetProperty("op").GetInt32() != 2 || response.GetProperty("d").Number("negotiatedRpcVersion") != 1)
                throw new ObsException("OBS did not accept the connection protocol.");
            _identified = true;
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Disconnect();
            if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
            throw Friendly(ex, connecting: true);
        }
        finally { _gate.Release(); }
    }

    public async Task<JsonElement> RequestAsync(string requestType, object? data = null, CancellationToken cancellation = default)
    {
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (!IsConnected) throw new ObsException("Connect to OBS first.");
            using var limit = Limit(cancellation);
            var socket = _socket!;
            string id = Guid.NewGuid().ToString("N");
            await SendAsync(socket, new { op = 6, d = new { requestType, requestId = id, requestData = data ?? new { } } }, limit.Token).ConfigureAwait(false);
            while (true)
            {
                var message = await ReceiveAsync(socket, limit.Token).ConfigureAwait(false);
                if (message.GetProperty("op").GetInt32() != 7) continue;
                var response = message.GetProperty("d");
                if (response.Text("requestId") != id) continue;
                var status = response.GetProperty("requestStatus");
                if (!status.Flag("result"))
                    throw new RequestRejected($"OBS could not complete {requestType} (code {status.Number("code"):0}). Refresh the preview and try again.");
                return response.TryGetProperty("responseData", out var result) ? result.Clone() : JsonSerializer.SerializeToElement(new { });
            }
        }
        catch (RequestRejected ex) { throw new ObsException(ex.Message); }
        catch (ObsException) { Disconnect(); throw; }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Disconnect();
            if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
            throw Friendly(ex, connecting: false);
        }
        finally { _gate.Release(); }
    }

    internal static string Authentication(string password, string salt, string challenge)
    {
        string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private CancellationTokenSource Limit(CancellationToken cancellation)
    {
        var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(_timeout);
        return limit;
    }

    private static async Task SendAsync(ClientWebSocket socket, object value, CancellationToken cancellation) =>
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions).AsMemory(), WebSocketMessageType.Text, true, cancellation).ConfigureAwait(false);

    private static async Task<JsonElement> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellation)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            var frame = await socket.ReceiveAsync(chunk.AsMemory(), cancellation).ConfigureAwait(false);
            if (frame.MessageType == WebSocketMessageType.Close)
                throw new ObsException((int?)socket.CloseStatus == 4009
                    ? "OBS rejected the password. Check the password in OBS's WebSocket Server Settings."
                    : "OBS closed the connection. Reconnect before making another change.");
            if (frame.MessageType != WebSocketMessageType.Text || buffer.Length + frame.Count > 1024 * 1024)
                throw new ObsException("OBS sent an unsupported or oversized response. Reconnect to OBS Studio.");
            buffer.Write(chunk, 0, frame.Count);
            if (!frame.EndOfMessage) continue;
            using var json = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
            return json.RootElement.Clone();
        }
    }

    private static ObsException Friendly(Exception error, bool connecting) => error as ObsException ?? new ObsException(
        error is OperationCanceledException ? "OBS did not respond in time. Reconnect and refresh before trying again."
        : error is WebSocketException && connecting ? "Could not reach OBS on this PC. Open OBS and enable its WebSocket server, then check the port."
        : "The OBS connection was interrupted or returned an invalid response. Reconnect and refresh the preview.");

    public void Disconnect()
    {
        _identified = false;
        var socket = Interlocked.Exchange(ref _socket, null);
        socket?.Abort();
        socket?.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        Disconnect();
    }
}
