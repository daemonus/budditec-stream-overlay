using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Budditec.Overlay.Core.Chat.Emotes;

namespace Budditec.Overlay.Core.Chat.YouTube;

/// <summary>
/// Reads a YouTube live chat without an API key, by polling the same endpoint youtube.com's chat popout uses.
/// When the channel isn't live it checks again every minute.
/// </summary>
/// <remarks>
/// The Data API needs a key and its quota runs out within hours of chat polling, so this uses the
/// undocumented web endpoint instead (as most chat tools do). It can change without notice.
/// </remarks>
public sealed partial class YouTubeChatClient(string channel, HttpClient http) : IChatSource
{
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
    private static readonly TimeSpan NotLiveRetry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MinPoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxPoll = TimeSpan.FromSeconds(3);

    private readonly (string? VideoId, string? LivePage) _target = ParseTarget(channel);

    public event Action<ChatEvent>? EventRaised;

    public Task RunAsync(CancellationToken ct) => ReconnectLoop.RunAsync(ChatPlatform.YouTube, RunOnceAsync, Raise, ct);

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var videoId = _target.VideoId ?? await FindLiveVideoAsync(_target.LivePage!, ct);
        var chat = videoId is null ? null : await OpenChatAsync(videoId, ct);
        if (chat is not { } opened)
        {
            Raise(new ChatConnectionChanged(ChatPlatform.YouTube, false, videoId is null ? "Not live" : "Live chat unavailable"));
            await Task.Delay(NotLiveRetry, ct);
            return;
        }

        Raise(new ChatConnectionChanged(ChatPlatform.YouTube, true, $"Watching youtu.be/{videoId}"));
        var (clientVersion, continuation) = opened;
        // The "Live chat" continuation is a reload: its first answer repeats recent history, which we skip.
        var first = true;
        while (true)
        {
            using var request = Request(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?prettyPrint=false");
            request.Content = JsonContent.Create(new
            {
                context = new { client = new { clientName = "WEB", clientVersion, hl = "en" } },
                continuation,
            });
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("continuationContents", out var contents)
                || !contents.TryGetProperty("liveChatContinuation", out var live))
                break;
            if (!first)
                foreach (var chatEvent in ParseActions(live))
                    Raise(chatEvent);
            first = false;

            if (NextContinuation(live) is not { } next) break;
            continuation = next.Continuation;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(next.TimeoutMs, MinPoll.TotalMilliseconds, MaxPoll.TotalMilliseconds)), ct);
        }
        Raise(new ChatConnectionChanged(ChatPlatform.YouTube, false, "Stream ended"));
    }

    /// <summary>The video the channel's /live page points at, or null when nothing is live or scheduled.</summary>
    private async Task<string?> FindLiveVideoAsync(string livePage, CancellationToken ct)
    {
        var html = await GetStringAsync(livePage, ct);
        var canonical = CanonicalWatchUrl().Match(html);
        return canonical.Success ? canonical.Groups[1].Value : null;
    }

    /// <summary>The web client version and the "Live chat" (not "Top chat") continuation, or null without chat.</summary>
    private async Task<(string ClientVersion, string Continuation)?> OpenChatAsync(string videoId, CancellationToken ct)
    {
        var html = await GetStringAsync($"https://www.youtube.com/live_chat?is_popout=1&v={videoId}", ct);
        var data = InitialData().Match(html);
        var version = ClientVersion().Match(html);
        if (!data.Success || !version.Success) return null;

        using var doc = JsonDocument.Parse(data.Groups[1].Value);
        return FindContinuation(doc.RootElement) is { } continuation ? (version.Groups[1].Value, continuation) : null;
    }

    internal static string? FindContinuation(JsonElement initialData)
    {
        if (!initialData.TryGetProperty("contents", out var contents) || !contents.TryGetProperty("liveChatRenderer", out var renderer))
            return null;
        if (renderer.TryGetPath(out var items, "header", "liveChatHeaderRenderer", "viewSelector", "sortFilterSubMenuRenderer", "subMenuItems")
            && items.GetArrayLength() > 1
            && items[items.GetArrayLength() - 1].TryGetPath(out var reload, "continuation", "reloadContinuationData", "continuation"))
            return reload.GetString();
        return NextContinuation(renderer)?.Continuation;
    }

    internal static (string Continuation, double TimeoutMs)? NextContinuation(JsonElement liveChat)
    {
        if (!liveChat.TryGetProperty("continuations", out var list)) return null;
        foreach (var item in list.EnumerateArray())
            foreach (var kind in item.EnumerateObject())
                if (kind.Value.ValueKind == JsonValueKind.Object && kind.Value.TryGetProperty("continuation", out var c))
                    return (c.GetString()!, kind.Value.TryGetProperty("timeoutMs", out var t) ? t.GetDouble() : MaxPoll.TotalMilliseconds);
        return null;
    }

    internal static IEnumerable<ChatEvent> ParseActions(JsonElement liveChat)
    {
        if (!liveChat.TryGetProperty("actions", out var actions)) yield break;
        foreach (var action in actions.EnumerateArray())
        {
            if (action.TryGetPath(out var item, "addChatItemAction", "item"))
            {
                if (item.TryGetProperty("liveChatTextMessageRenderer", out var text))
                    yield return new ChatMessageReceived(ToChatMessage(text, null));
                else if (item.TryGetProperty("liveChatPaidMessageRenderer", out var paid))
                    yield return new ChatMessageReceived(ToChatMessage(paid, paid.TryGetPath(out var amount, "purchaseAmountText", "simpleText") ? amount.GetString() : null));
            }
            else if (action.TryGetPath(out var target, "removeChatItemAction", "targetItemId")
                     || action.TryGetPath(out target, "markChatItemAsDeletedAction", "targetItemId"))
            {
                yield return new ChatMessageDeleted(ChatPlatform.YouTube, target.GetString()!);
            }
            else if (action.TryGetPath(out var author, "removeChatItemByAuthorAction", "externalChannelId")
                     || action.TryGetPath(out author, "markChatItemsByAuthorAsDeletedAction", "externalChannelId"))
            {
                yield return new ChatUserCleared(ChatPlatform.YouTube, author.GetString()!);
            }
        }
    }

    private static ChatMessage ToChatMessage(JsonElement renderer, string? paidAmount)
    {
        var fragments = new List<ChatFragment>();
        if (paidAmount is not null) fragments.Add(new TextFragment($"[{paidAmount}] "));
        if (renderer.TryGetPath(out var runs, "message", "runs"))
            foreach (var run in runs.EnumerateArray())
            {
                if (run.TryGetProperty("text", out var t))
                    FragmentBuilder.AppendText(fragments, t.GetString()!, EmptyEmoteLookup.Instance);
                else if (run.TryGetProperty("emoji", out var emoji))
                    AppendEmoji(fragments, emoji);
            }

        var channelId = renderer.TryGetProperty("authorExternalChannelId", out var a) ? a.GetString()! : "";
        var name = renderer.TryGetPath(out var n, "authorName", "simpleText") ? n.GetString()!.TrimStart('@') : channelId;
        return new ChatMessage(
            Id: renderer.GetProperty("id").GetString()!,
            Platform: ChatPlatform.YouTube,
            UserLogin: channelId,
            UserDisplayName: name,
            UserColor: null,
            Fragments: fragments,
            Timestamp: renderer.TryGetProperty("timestampUsec", out var ts) && long.TryParse(ts.GetString(), out var usec)
                ? DateTimeOffset.FromUnixTimeMilliseconds(usec / 1000)
                : DateTimeOffset.UtcNow);
    }

    /// <summary>Channel emotes become images; standard emoji stay text (their id is the emoji itself).</summary>
    private static void AppendEmoji(List<ChatFragment> fragments, JsonElement emoji)
    {
        var id = emoji.TryGetProperty("emojiId", out var e) ? e.GetString() ?? "" : "";
        var isCustom = emoji.TryGetProperty("isCustomEmoji", out var custom) && custom.GetBoolean();
        if (isCustom && emoji.TryGetPath(out var thumbnails, "image", "thumbnails") && thumbnails.GetArrayLength() > 0
            && Uri.TryCreate(thumbnails[thumbnails.GetArrayLength() - 1].GetProperty("url").GetString(), UriKind.Absolute, out var url))
        {
            var code = emoji.TryGetProperty("shortcuts", out var shortcuts) && shortcuts.GetArrayLength() > 0 ? shortcuts[0].GetString()! : id;
            fragments.Add(new EmoteFragment(code, url));
        }
        else
        {
            FragmentBuilder.AppendText(fragments, id, EmptyEmoteLookup.Instance);
        }
    }

    /// <summary>
    /// A video (watch/youtu.be/live URL) is used as is; anything else names a channel (@handle, UC... id or
    /// channel URL) whose /live page is checked for a stream.
    /// </summary>
    internal static (string? VideoId, string? LivePage) ParseTarget(string channel)
    {
        var s = channel.Trim();
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Host.Contains("youtu", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase) && segments.Length > 0) return (segments[0], null);
            if (segments is ["watch"] && QueryValue(uri.Query, "v") is { } v) return (v, null);
            if (segments is ["live", var liveId]) return (liveId, null);
            if (segments is ["channel" or "c" or "user", var name, ..]) return (null, $"https://www.youtube.com/{segments[0]}/{name}/live");
            if (segments is [var handle, ..] && handle.StartsWith('@')) return (null, $"https://www.youtube.com/{handle}/live");
        }
        if (ChannelId().IsMatch(s)) return (null, $"https://www.youtube.com/channel/{s}/live");
        return (null, $"https://www.youtube.com/@{Uri.EscapeDataString(s.TrimStart('@'))}/live");
    }

    private static string? QueryValue(string query, string key) =>
        query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p[0] == key && p.Length == 2)?[1];

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>A browser-like request; the consent cookie skips the EU cookie wall.</summary>
    private static HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en");
        request.Headers.TryAddWithoutValidation("Cookie", "SOCS=CAI; CONSENT=YES+1");
        return request;
    }

    private void Raise(ChatEvent e) => EventRaised?.Invoke(e);

    [GeneratedRegex(@"<link rel=""canonical"" href=""https://www\.youtube\.com/watch\?v=([\w-]{11})""")]
    private static partial Regex CanonicalWatchUrl();

    [GeneratedRegex(@"(?:window\[""ytInitialData""\]|var ytInitialData)\s*=\s*(\{.+?\});\s*</script>", RegexOptions.Singleline)]
    private static partial Regex InitialData();

    [GeneratedRegex(@"""clientVersion"":""([^""]+)""")]
    private static partial Regex ClientVersion();

    [GeneratedRegex(@"^UC[\w-]{22}$")]
    private static partial Regex ChannelId();
}

internal static class JsonPathExtensions
{
    public static bool TryGetPath(this JsonElement element, out JsonElement value, params string[] path)
    {
        value = element;
        foreach (var name in path)
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out value))
                return false;
        return true;
    }
}
