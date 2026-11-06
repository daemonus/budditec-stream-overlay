using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Budditec.Overlay.Core.Chat.Emotes;

namespace Budditec.Overlay.Core.Chat.Kick;

/// <summary>
/// Reads a Kick channel's chat as a guest, the way kick.com's own chat does: the chatroom id comes from the
/// website API, and messages from Kick's Centrifugo server (it replaced Pusher in October 2026).
/// </summary>
/// <remarks>
/// None of this is Kick's documented API. The WebSocket handshake is refused unless Origin is kick.com,
/// guest tokens last 45 minutes and are refreshed on the open connection, and each publication carries
/// the same "App\Events\..." payload Pusher used to.
/// </remarks>
public sealed partial class KickChatClient(string channel, HttpClient http, ThirdPartyEmoteStore emotes) : IChatSource
{
    private const string Origin = "https://kick.com";
    private const string RealtimeApi = "https://web.kick.com/api/v1/realtime";
    private static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(5);

    private readonly string _slug = ParseSlug(channel);
    private (long ChatroomId, long UserId)? _room;

    public event Action<ChatEvent>? EventRaised;

    public Task RunAsync(CancellationToken ct) => ReconnectLoop.RunAsync(ChatPlatform.Kick, RunOnceAsync, Raise, ct);

    private async Task RunOnceAsync(CancellationToken ct)
    {
        if (_room is null)
        {
            _room = await LookupChannelAsync(ct);
            _ = emotes.LoadKickChannelAsync(_room.Value.UserId, ct);
        }

        var clientId = Guid.NewGuid().ToString();
        var server = await GetServerAsync(clientId, ct);
        var (token, refreshAt) = await GetTokenAsync(clientId, ct);

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", Origin);
        await socket.ConnectAsync(server, ct);

        var feed = $"chatrooms.{_room.Value.ChatroomId}.v2";
        await SendAsync(socket, new JsonObject { ["id"] = 1, ["connect"] = new JsonObject { ["token"] = token, ["name"] = "js" } }, ct);
        await SendAsync(socket, new JsonObject { ["id"] = 2, ["subscribe"] = new JsonObject { ["channel"] = feed } }, ct);
        var nextId = 3;

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
            // Centrifugo batches replies a line each.
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.EnumerateObject().Any())
                {
                    // Server ping.
                    await socket.SendAsync("{}"u8.ToArray(), WebSocketMessageType.Text, true, ct);
                    continue;
                }
                if (root.TryGetProperty("error", out var error))
                    throw new InvalidOperationException($"Kick: {error.GetProperty("message").GetString()}");
                if (root.TryGetProperty("id", out var id) && id.GetInt32() == 2 && root.TryGetProperty("subscribe", out _))
                    Raise(new ChatConnectionChanged(ChatPlatform.Kick, true, $"Joined kick.com/{_slug}"));
                if (!root.TryGetProperty("push", out var push)) continue;
                if (push.TryGetProperty("disconnect", out _)) return;
                if (push.TryGetProperty("pub", out var pub) && pub.TryGetProperty("data", out var data)
                    && ToChatEvent(data, emotes) is { } chatEvent)
                    Raise(chatEvent);
            }

            if (DateTimeOffset.UtcNow >= refreshAt)
            {
                (token, refreshAt) = await GetTokenAsync(clientId, ct);
                await SendAsync(socket, new JsonObject { ["id"] = nextId++, ["refresh"] = new JsonObject { ["token"] = token } }, ct);
            }
        }
        Raise(new ChatConnectionChanged(ChatPlatform.Kick, false, "Disconnected"));
    }

    /// <summary>Turns a Kick envelope ({ event, data }) into a chat event, or null for events we don't show.</summary>
    internal static ChatEvent? ToChatEvent(JsonElement envelope, IEmoteLookup thirdParty)
    {
        var name = envelope.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (!envelope.TryGetProperty("data", out var data)) return null;
        // The payload is usually a JSON string inside the JSON, occasionally an object already.
        using var inner = data.ValueKind == JsonValueKind.String ? JsonDocument.Parse(data.GetString()!) : null;
        var d = inner?.RootElement ?? data;

        switch (name)
        {
            case @"App\Events\ChatMessageEvent":
                var sender = d.GetProperty("sender");
                var slug = Str(sender, "slug");
                var color = sender.TryGetProperty("identity", out var identity) ? Str(identity, "color") : "";
                return new ChatMessageReceived(new ChatMessage(
                    Id: Str(d, "id"),
                    Platform: ChatPlatform.Kick,
                    UserLogin: slug,
                    UserDisplayName: Str(sender, "username") is { Length: > 0 } username ? username : slug,
                    UserColor: color.Length > 0 ? color : null,
                    Fragments: BuildFragments(Str(d, "content"), thirdParty),
                    Timestamp: DateTimeOffset.TryParse(Str(d, "created_at"), out var at) ? at : DateTimeOffset.UtcNow));
            case @"App\Events\MessageDeletedEvent":
                return new ChatMessageDeleted(ChatPlatform.Kick, Str(d.GetProperty("message"), "id"));
            case @"App\Events\UserBannedEvent":
                return new ChatUserCleared(ChatPlatform.Kick, Str(d.GetProperty("user"), "slug"));
            case @"App\Events\ChatroomClearEvent":
                return new ChatCleared(ChatPlatform.Kick);
            default:
                return null;
        }
    }

    /// <summary>Kick puts emotes inline as [emote:id:name].</summary>
    internal static IReadOnlyList<ChatFragment> BuildFragments(string content, IEmoteLookup thirdParty)
    {
        var fragments = new List<ChatFragment>();
        var cursor = 0;
        foreach (Match match in EmoteToken().Matches(content))
        {
            FragmentBuilder.AppendText(fragments, content[cursor..match.Index], thirdParty);
            fragments.Add(new EmoteFragment(match.Groups[2].Value, new Uri($"https://files.kick.com/emotes/{match.Groups[1].Value}/fullsize")));
            cursor = match.Index + match.Length;
        }
        FragmentBuilder.AppendText(fragments, content[cursor..], thirdParty);
        return fragments;
    }

    /// <summary>Accepts "name", "@name" or a kick.com URL (with or without https://).</summary>
    internal static string ParseSlug(string channel)
    {
        var s = channel.Trim();
        if (s.Contains('/') && !s.Contains("://")) s = "https://" + s;
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Host.EndsWith("kick.com", StringComparison.OrdinalIgnoreCase))
            s = uri.AbsolutePath.Trim('/').Split('/')[0];
        return s.TrimStart('@').ToLowerInvariant();
    }

    private async Task<(long, long)> LookupChannelAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync($"https://kick.com/api/v2/channels/{Uri.EscapeDataString(_slug)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException($"Kick channel '{_slug}' not found");
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        return (root.GetProperty("chatroom").GetProperty("id").GetInt64(), root.GetProperty("user_id").GetInt64());
    }

    private async Task<Uri> GetServerAsync(string clientId, CancellationToken ct)
    {
        using var doc = await PostAsync($"{RealtimeApi}/connection", new
        {
            client = new { id = clientId, type = "web" },
            capabilities = new { accepted_providers = new[] { new { provider = "centrifugo" } } },
        }, ct);
        foreach (var connection in doc.RootElement.GetProperty("data").GetProperty("connections").EnumerateArray())
            if (Str(connection, "provider") == "centrifugo")
                return new Uri(Str(connection.GetProperty("credentials"), "url"));
        throw new InvalidOperationException("Kick offered no Centrifugo server");
    }

    private async Task<(string Token, DateTimeOffset RefreshAt)> GetTokenAsync(string clientId, CancellationToken ct)
    {
        using var doc = await PostAsync($"{RealtimeApi}/auth/connection", new { client_id = clientId }, ct);
        return (Str(doc.RootElement.GetProperty("data"), "token"), DateTimeOffset.UtcNow + TimeSpan.FromMinutes(45) - RefreshBefore);
    }

    private async Task<JsonDocument> PostAsync(string url, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", Origin);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private static string Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static Task SendAsync(ClientWebSocket socket, JsonObject command, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(command.ToJsonString()), WebSocketMessageType.Text, true, ct);

    private void Raise(ChatEvent e) => EventRaised?.Invoke(e);

    [GeneratedRegex(@"\[emote:(\d+):([^\]]*)\]")]
    private static partial Regex EmoteToken();
}
