using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Budditec.Overlay.Core.Obs;

/// <summary>
/// Minimal obs-websocket v5 client: identify (with optional auth), requests, and events.
/// </summary>
public sealed class ObsWebSocketClient : IAsyncDisposable
{
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoop;

    public event Action<string, JsonElement>? EventReceived;

    /// <summary>Raised once when the connection drops, with the reason.</summary>
    public event Action<string>? Closed;

    public bool IsOpen => _socket.State == WebSocketState.Open;

    public async Task ConnectAsync(Uri url, string? password, ObsEventSubscription subscriptions, CancellationToken ct)
    {
        _socket.Options.AddSubProtocol("obswebsocket.json");
        await _socket.ConnectAsync(url, ct);

        var hello = await ReceiveJsonAsync(ct) ?? throw new InvalidOperationException("OBS closed the connection.");
        var d = hello.GetProperty("d");

        var identify = new JsonObject
        {
            ["rpcVersion"] = 1,
            ["eventSubscriptions"] = (int)subscriptions,
        };
        if (d.TryGetProperty("authentication", out var auth))
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("OBS requires a WebSocket password.");
            identify["authentication"] = ComputeAuth(password, auth.GetProperty("salt").GetString()!, auth.GetProperty("challenge").GetString()!);
        }
        await SendAsync(new JsonObject { ["op"] = 1, ["d"] = identify }, ct);

        var identified = await ReceiveJsonAsync(ct);
        if (identified?.GetProperty("op").GetInt32() != 2)
            throw new InvalidOperationException(_socket.CloseStatusDescription ?? "OBS rejected the connection (wrong password?).");

        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>Sends a request and returns its responseData (an empty object when there is none).</summary>
    public async Task<JsonElement> RequestAsync(string requestType, object? requestData = null, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var d = new JsonObject { ["requestType"] = requestType, ["requestId"] = id };
        if (requestData is not null) d["requestData"] = JsonSerializer.SerializeToNode(requestData);
        await SendAsync(new JsonObject { ["op"] = 6, ["d"] = d }, ct);

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    internal static string ComputeAuth(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private async Task ReceiveLoopAsync()
    {
        var reason = "Disconnected";
        try
        {
            while (await ReceiveJsonAsync(_cts.Token) is { } message)
            {
                var d = message.GetProperty("d");
                switch (message.GetProperty("op").GetInt32())
                {
                    case 5:
                        EventReceived?.Invoke(
                            d.GetProperty("eventType").GetString()!,
                            d.TryGetProperty("eventData", out var data) ? data : default);
                        break;
                    case 7:
                        CompleteRequest(d);
                        break;
                }
            }
            reason = _socket.CloseStatusDescription is { Length: > 0 } desc ? desc : reason;
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException)
        {
            reason = ex.Message;
        }
        foreach (var tcs in _pending.Values) tcs.TrySetException(new InvalidOperationException(reason));
        Closed?.Invoke(reason);
    }

    private void CompleteRequest(JsonElement d)
    {
        if (!_pending.TryRemove(d.GetProperty("requestId").GetString()!, out var tcs)) return;
        var status = d.GetProperty("requestStatus");
        if (status.GetProperty("result").GetBoolean())
            tcs.TrySetResult(d.TryGetProperty("responseData", out var data) ? data : EmptyObject);
        else
            tcs.TrySetException(new ObsRequestException(
                d.GetProperty("requestType").GetString()!,
                status.GetProperty("code").GetInt32(),
                status.TryGetProperty("comment", out var comment) ? comment.GetString() : null));
    }

    private async Task<JsonElement?> ReceiveJsonAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        // Clone so the element outlives the document.
        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }

    private async Task SendAsync(JsonNode message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _sendLock.WaitAsync(ct);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) when (ex is WebSocketException or TimeoutException)
            {
            }
        }
        if (_receiveLoop is not null) await _receiveLoop.ConfigureAwait(false);
        _socket.Dispose();
        _cts.Dispose();
        _sendLock.Dispose();
    }
}

public sealed class ObsRequestException(string requestType, int code, string? comment)
    : Exception($"OBS request {requestType} failed ({code}){(comment is null ? "" : ": " + comment)}")
{
    public int Code { get; } = code;
}

[Flags]
public enum ObsEventSubscription
{
    None = 0,
    General = 1 << 0,
    Config = 1 << 1,
    Scenes = 1 << 2,
    Inputs = 1 << 3,
    Transitions = 1 << 4,
    Filters = 1 << 5,
    Outputs = 1 << 6,
    SceneItems = 1 << 7,
    MediaInputs = 1 << 8,
    Vendors = 1 << 9,
    Ui = 1 << 10,
    All = (1 << 11) - 1,

    /// <summary>High volume: audio levels for every active input, about every 50 ms.</summary>
    InputVolumeMeters = 1 << 16,
}
