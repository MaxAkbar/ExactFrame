using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExactFrame.Core.Obs;

namespace ExactFrame.Core.Tests;

/// <summary>An actual loopback WebSocket peer, so framing, authentication and timeouts exercise the transport.</summary>
internal sealed class ScriptedObsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    public int Port { get; }
    public ScriptedObsServer(Func<WebSocket, CancellationToken, Task> script)
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _run = Task.Run(async () =>
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = client.GetStream();
                var header = new StringBuilder();
                byte[] one = new byte[1];
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(one, _stop.Token) == 0) return;
                    header.Append((char)one[0]);
                    if (header.Length > 8192) throw new InvalidOperationException("Oversized test handshake.");
                }
                string key = header.ToString().Split("\r\n").Single(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
                string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), _stop.Token);
                using var socket = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
                await script(socket, _stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException) { }
        });
    }
    public static async Task Send(WebSocket socket, object data, CancellationToken cancellation, bool fragmented = false)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data);
        int split = fragmented ? bytes.Length / 2 : bytes.Length;
        await socket.SendAsync(bytes.AsMemory(0, split), WebSocketMessageType.Text, !fragmented, cancellation);
        if (fragmented) await socket.SendAsync(bytes.AsMemory(split), WebSocketMessageType.Text, true, cancellation);
    }
    public static async Task<JsonElement> Read(WebSocket socket, CancellationToken cancellation)
    {
        using var buffer = new MemoryStream();
        byte[] bytes = new byte[8192];
        while (true)
        {
            var frame = await socket.ReceiveAsync(bytes.AsMemory(), cancellation);
            if (frame.MessageType == WebSocketMessageType.Close) throw new IOException("Peer closed.");
            buffer.Write(bytes, 0, frame.Count);
            if (!frame.EndOfMessage) continue;
            using var json = JsonDocument.Parse(buffer.ToArray());
            return json.RootElement.Clone();
        }
    }
    public static async Task Identify(WebSocket socket, CancellationToken cancellation)
    {
        await Send(socket, new { op = 0, d = new { rpcVersion = 1 } }, cancellation);
        var identify = await Read(socket, cancellation);
        Assert.Equal(1, identify.GetProperty("op").GetInt32());
        Assert.Equal(0, identify.GetProperty("d").GetProperty("eventSubscriptions").GetInt32());
        await Send(socket, new { op = 2, d = new { negotiatedRpcVersion = 1 } }, cancellation);
    }
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync(); _listener.Stop();
        await _run.WaitAsync(TimeSpan.FromSeconds(3));
        _stop.Dispose();
    }
}

public sealed class ObsWebSocketClientTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Authentication_matches_the_OBS_protocol_example_and_handles_fragmented_messages()
    {
        await using var server = new ScriptedObsServer(async (socket, cancellation) =>
        {
            await ScriptedObsServer.Send(socket, new { op = 0, d = new { rpcVersion = 1, authentication = new {
                salt = "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", challenge = "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=" } } }, cancellation, fragmented: true);
            var identify = await ScriptedObsServer.Read(socket, cancellation);
            Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=", identify.GetProperty("d").GetProperty("authentication").GetString());
            Assert.DoesNotContain("supersecretpassword", identify.GetRawText());
            await ScriptedObsServer.Send(socket, new { op = 2, d = new { negotiatedRpcVersion = 1 } }, cancellation);
            var request = await ScriptedObsServer.Read(socket, cancellation);
            string id = request.GetProperty("d").GetProperty("requestId").GetString()!;
            Assert.False(request.GetProperty("d").GetProperty("requestData").TryGetProperty("sceneUuid", out _));
            await ScriptedObsServer.Send(socket, new { op = 5, d = new { eventType = "Unrelated" } }, cancellation);
            await ScriptedObsServer.Send(socket, new { op = 7, d = new { requestId = "unrelated", requestStatus = new { result = true, code = 100 } } }, cancellation);
            await ScriptedObsServer.Send(socket, new { op = 7, d = new { requestId = id, requestStatus = new { result = true, code = 100 }, responseData = new { answer = 42 } } }, cancellation, fragmented: true);
            await Task.Delay(Timeout.Infinite, cancellation);
        });
        using var client = new ObsWebSocketClient();
        await client.ConnectAsync(server.Port, "supersecretpassword", Cancellation);
        var response = await client.RequestAsync("GetVersion", new { sceneName = "Scene", sceneUuid = (string?)null }, Cancellation);
        Assert.Equal(42, response.GetProperty("answer").GetInt32());
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task Authentication_failure_is_actionable_and_disconnects()
    {
        await using var server = new ScriptedObsServer(async (socket, cancellation) =>
        {
            await ScriptedObsServer.Send(socket, new { op = 0, d = new { rpcVersion = 1 } }, cancellation);
            await ScriptedObsServer.Read(socket, cancellation);
            await socket.CloseOutputAsync((WebSocketCloseStatus)4009, "Authentication failed", cancellation);
        });
        using var client = new ObsWebSocketClient();
        var error = await Assert.ThrowsAsync<ObsException>(() => client.ConnectAsync(server.Port, "incorrect", Cancellation));
        Assert.Contains("password", error.Message);
        Assert.DoesNotContain("incorrect", error.Message);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Timeout_disconnects_so_a_late_reply_cannot_be_used_for_the_next_request()
    {
        await using var server = new ScriptedObsServer(async (socket, cancellation) =>
        {
            await ScriptedObsServer.Identify(socket, cancellation);
            await ScriptedObsServer.Read(socket, cancellation);
            await Task.Delay(Timeout.Infinite, cancellation);
        });
        using var client = new ObsWebSocketClient(TimeSpan.FromMilliseconds(400));
        await client.ConnectAsync(server.Port, "", Cancellation);
        var error = await Assert.ThrowsAsync<ObsException>(() => client.RequestAsync("GetVersion", cancellation: Cancellation));
        Assert.Contains("in time", error.Message);
        Assert.False(client.IsConnected);
        await Assert.ThrowsAsync<ObsException>(() => client.RequestAsync("GetVersion", cancellation: Cancellation));
    }

    [Fact]
    public async Task OBS_request_failure_preserves_the_usable_connection()
    {
        await using var server = new ScriptedObsServer(async (socket, cancellation) =>
        {
            await ScriptedObsServer.Identify(socket, cancellation);
            var request = await ScriptedObsServer.Read(socket, cancellation);
            await ScriptedObsServer.Send(socket, new { op = 7, d = new { requestId = request.GetProperty("d").GetProperty("requestId").GetString(),
                requestStatus = new { result = false, code = 600 } } }, cancellation);
            await Task.Delay(Timeout.Infinite, cancellation);
        });
        using var client = new ObsWebSocketClient();
        await client.ConnectAsync(server.Port, "", Cancellation);
        var error = await Assert.ThrowsAsync<ObsException>(() => client.RequestAsync("GetSceneItemTransform", cancellation: Cancellation));
        Assert.Contains("600", error.Message);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task Binary_protocol_messages_are_rejected_and_disconnect()
    {
        await using var server = new ScriptedObsServer(async (socket, cancellation) =>
        {
            await ScriptedObsServer.Identify(socket, cancellation);
            await ScriptedObsServer.Read(socket, cancellation);
            await socket.SendAsync(new byte[] { 1, 2, 3 }.AsMemory(), WebSocketMessageType.Binary, true, cancellation);
            await Task.Delay(Timeout.Infinite, cancellation);
        });
        using var client = new ObsWebSocketClient();
        await client.ConnectAsync(server.Port, "", Cancellation);
        await Assert.ThrowsAsync<ObsException>(() => client.RequestAsync("GetVersion", cancellation: Cancellation));
        Assert.False(client.IsConnected);
    }
}
