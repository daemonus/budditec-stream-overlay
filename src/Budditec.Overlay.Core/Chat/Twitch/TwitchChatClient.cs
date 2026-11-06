using System.Net.WebSockets;
using System.Text;
using Budditec.Overlay.Core.Chat.Emotes;

namespace Budditec.Overlay.Core.Chat.Twitch;

/// <summary>
/// Reads a channel's chat anonymously over Twitch IRC (WebSocket) and loads its 7TV/BTTV/FFZ emotes.
/// </summary>
public sealed class TwitchChatClient(string channel, ThirdPartyEmoteStore emotes) : IChatSource
{
    private static readonly Uri Endpoint = new("wss://irc-ws.chat.twitch.tv:443");

    private readonly string _channel = ParseChannel(channel);
    private string? _loadedRoomId;

    public event Action<ChatEvent>? EventRaised;

    public Task RunAsync(CancellationToken ct)
    {
        _ = emotes.LoadGlobalAsync(ct);
        return ReconnectLoop.RunAsync(ChatPlatform.Twitch, RunOnceAsync, Raise, ct);
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(Endpoint, ct);

        await SendAsync(socket, "CAP REQ :twitch.tv/tags twitch.tv/commands", ct);
        await SendAsync(socket, "PASS SCHMOOPIIE", ct);
        await SendAsync(socket, $"NICK justinfan{Random.Shared.Next(10000, 99999)}", ct);
        await SendAsync(socket, $"JOIN #{_channel}", ct);

        var buffer = new byte[16 * 1024];
        using var pending = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) break;
            pending.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var text = Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length);
            pending.SetLength(0);
            foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                if (IrcMessage.Parse(line) is { } message && !await HandleAsync(socket, message, ct))
                    return;
        }
        Raise(new ChatConnectionChanged(ChatPlatform.Twitch, false, "Disconnected"));
    }

    /// <returns>False when the server asked us to reconnect.</returns>
    private async Task<bool> HandleAsync(ClientWebSocket socket, IrcMessage message, CancellationToken ct)
    {
        switch (message.Command)
        {
            case "PING":
                await SendAsync(socket, $"PONG :{message.Trailing}", ct);
                break;
            case "RECONNECT":
                return false;
            case "ROOMSTATE":
                var roomId = message.Tag("room-id");
                if (roomId.Length > 0 && roomId != _loadedRoomId)
                {
                    _loadedRoomId = roomId;
                    _ = emotes.LoadTwitchChannelAsync(roomId, ct);
                }
                Raise(new ChatConnectionChanged(ChatPlatform.Twitch, true, $"Joined #{_channel}"));
                break;
            case "PRIVMSG":
                Raise(new ChatMessageReceived(ToChatMessage(message, emotes)));
                break;
            case "CLEARMSG":
                Raise(new ChatMessageDeleted(ChatPlatform.Twitch, message.Tag("target-msg-id")));
                break;
            case "CLEARCHAT":
                Raise(message.Parameters.Count > 1
                    ? new ChatUserCleared(ChatPlatform.Twitch, message.Trailing.ToLowerInvariant())
                    : new ChatCleared(ChatPlatform.Twitch));
                break;
        }
        return true;
    }

    internal static ChatMessage ToChatMessage(IrcMessage message, IEmoteLookup thirdParty)
    {
        var text = message.Trailing;
        var isAction = text.StartsWith("\u0001ACTION ") && text.EndsWith('\u0001');
        if (isAction) text = text[8..^1];

        var login = message.Nick;
        var displayName = message.Tag("display-name");
        var color = message.Tag("color");
        return new ChatMessage(
            Id: message.Tag("id") is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N"),
            Platform: ChatPlatform.Twitch,
            UserLogin: login,
            UserDisplayName: displayName.Length > 0 ? displayName : login,
            UserColor: color.Length > 0 ? color : null,
            Fragments: TwitchEmoteParser.BuildFragments(text, message.Tag("emotes"), thirdParty),
            Timestamp: long.TryParse(message.Tag("tmi-sent-ts"), out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : DateTimeOffset.UtcNow,
            IsAction: isAction);
    }

    /// <summary>Accepts "name", "#name", "@name" or a twitch.tv URL (with or without https://).</summary>
    internal static string ParseChannel(string channel)
    {
        var s = channel.Trim();
        if (s.Contains('/') && !s.Contains("://")) s = "https://" + s;
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Host.EndsWith("twitch.tv", StringComparison.OrdinalIgnoreCase))
            s = uri.AbsolutePath.Trim('/').Split('/')[0];
        return s.TrimStart('#', '@').ToLowerInvariant();
    }

    private void Raise(ChatEvent e) => EventRaised?.Invoke(e);

    private static Task SendAsync(ClientWebSocket socket, string line, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(line + "\r\n"), WebSocketMessageType.Text, true, ct);
}
