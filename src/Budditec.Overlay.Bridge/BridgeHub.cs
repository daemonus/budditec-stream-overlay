using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Budditec.Overlay.Bridge;

/// <summary>Broadcasts JSON messages to every connected overlay page.</summary>
public sealed class BridgeHub
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();

    public void Broadcast(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        foreach (var client in _clients.Values) client.Writer.TryWrite(json);
    }

    /// <summary>Sends the initial snapshot, then everything broadcast until the page disconnects.</summary>
    public async Task RunClientAsync(WebSocket socket, IEnumerable<object> snapshot, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        // Levels arrive ~20 times a second; drop the oldest if a page falls behind.
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
        foreach (var message in snapshot) channel.Writer.TryWrite(JsonSerializer.Serialize(message, JsonOptions));
        _clients[id] = channel;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var receive = DrainAsync(socket, cts);
        try
        {
            await foreach (var json in channel.Reader.ReadAllAsync(cts.Token))
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        finally
        {
            _clients.TryRemove(id, out _);
            await cts.CancelAsync();
            await receive;
        }
    }

    private static async Task DrainAsync(WebSocket socket, CancellationTokenSource cts)
    {
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
                if ((await socket.ReceiveAsync(buffer, cts.Token)).MessageType == WebSocketMessageType.Close)
                    break;
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        await cts.CancelAsync();
    }
}
