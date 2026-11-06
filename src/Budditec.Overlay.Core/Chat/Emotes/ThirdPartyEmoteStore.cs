using System.Net;
using System.Text.Json;

namespace Budditec.Overlay.Core.Chat.Emotes;

/// <summary>
/// 7TV, BTTV and FFZ emotes for one Twitch (or Kick) channel. Lookup order: channel sets (7TV, BTTV, FFZ),
/// then global sets in the same order.
/// </summary>
public sealed class ThirdPartyEmoteStore(HttpClient http) : IEmoteLookup
{
    private enum Set { SevenTvChannel, BttvChannel, FfzChannel, SevenTvGlobal, BttvGlobal, FfzGlobal }

    private readonly Dictionary<string, Uri>[] _sets =
        Enumerable.Range(0, Enum.GetValues<Set>().Length).Select(_ => new Dictionary<string, Uri>()).ToArray();

    public event Action<string>? Log;

    public Uri? Find(string code)
    {
        foreach (var set in Volatile.Read(ref _snapshot))
            if (set.TryGetValue(code, out var url)) return url;
        return null;
    }

    private Dictionary<string, Uri>[] _snapshot = [];

    public Task LoadGlobalAsync(CancellationToken ct) => Task.WhenAll(
        Load(Set.SevenTvGlobal, "https://7tv.io/v3/emote-sets/global", ParseSevenTvSet, ct),
        Load(Set.BttvGlobal, "https://api.betterttv.net/3/cached/emotes/global", ParseBttvList, ct),
        Load(Set.FfzGlobal, "https://api.frankerfacez.com/v1/set/global", ParseFfzGlobal, ct));

    public Task LoadTwitchChannelAsync(string twitchUserId, CancellationToken ct) => Task.WhenAll(
        Load(Set.SevenTvChannel, $"https://7tv.io/v3/users/twitch/{twitchUserId}",
            json => json.TryGetProperty("emote_set", out var set) && set.ValueKind == JsonValueKind.Object ? ParseSevenTvSet(set) : [], ct),
        Load(Set.BttvChannel, $"https://api.betterttv.net/3/cached/users/twitch/{twitchUserId}", ParseBttvUser, ct),
        Load(Set.FfzChannel, $"https://api.frankerfacez.com/v1/room/id/{twitchUserId}", ParseFfzSets, ct));

    /// <summary>Kick only has 7TV (no BTTV/FFZ), keyed by the Kick user id.</summary>
    public Task LoadKickChannelAsync(long kickUserId, CancellationToken ct) => Task.WhenAll(
        Load(Set.SevenTvGlobal, "https://7tv.io/v3/emote-sets/global", ParseSevenTvSet, ct),
        Load(Set.SevenTvChannel, $"https://7tv.io/v3/users/kick/{kickUserId}",
            json => json.TryGetProperty("emote_set", out var set) && set.ValueKind == JsonValueKind.Object ? ParseSevenTvSet(set) : [], ct));

    private async Task Load(Set set, string url, Func<JsonElement, IEnumerable<KeyValuePair<string, Uri>>> parse, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Log?.Invoke($"{set}: none for this channel");
                return;
            }
            response.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var emotes = new Dictionary<string, Uri>();
            foreach (var (code, uri) in parse(doc.RootElement)) emotes.TryAdd(code, uri);

            lock (_sets)
            {
                _sets[(int)set] = emotes;
                Volatile.Write(ref _snapshot, _sets.ToArray());
            }
            Log?.Invoke($"{set}: {emotes.Count} emotes");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log?.Invoke($"{set}: failed ({ex.Message})");
        }
    }

    private static IEnumerable<KeyValuePair<string, Uri>> ParseSevenTvSet(JsonElement set)
    {
        if (!set.TryGetProperty("emotes", out var emotes) || emotes.ValueKind != JsonValueKind.Array) yield break;
        foreach (var emote in emotes.EnumerateArray())
        {
            var name = emote.GetProperty("name").GetString();
            var host = emote.TryGetProperty("data", out var data) && data.TryGetProperty("host", out var h)
                ? h.GetProperty("url").GetString()
                : null;
            if (name is not null && host is not null)
                yield return new(name, new Uri($"https:{host}/2x.webp"));
        }
    }

    private static IEnumerable<KeyValuePair<string, Uri>> ParseBttvList(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array) yield break;
        foreach (var emote in list.EnumerateArray())
        {
            var code = emote.GetProperty("code").GetString();
            var id = emote.GetProperty("id").GetString();
            if (code is not null && id is not null)
                yield return new(code, new Uri($"https://cdn.betterttv.net/emote/{id}/2x.webp"));
        }
    }

    private static IEnumerable<KeyValuePair<string, Uri>> ParseBttvUser(JsonElement user)
    {
        foreach (var key in new[] { "channelEmotes", "sharedEmotes" })
            if (user.TryGetProperty(key, out var list))
                foreach (var emote in ParseBttvList(list)) yield return emote;
    }

    private static IEnumerable<KeyValuePair<string, Uri>> ParseFfzGlobal(JsonElement root)
    {
        var defaults = root.TryGetProperty("default_sets", out var d)
            ? d.EnumerateArray().Select(x => x.ToString()).ToHashSet()
            : null;
        return ParseFfzSets(root, defaults);
    }

    private static IEnumerable<KeyValuePair<string, Uri>> ParseFfzSets(JsonElement root) => ParseFfzSets(root, null);

    private static IEnumerable<KeyValuePair<string, Uri>> ParseFfzSets(JsonElement root, HashSet<string>? only)
    {
        if (!root.TryGetProperty("sets", out var sets)) yield break;
        foreach (var set in sets.EnumerateObject())
        {
            if (only is not null && !only.Contains(set.Name)) continue;
            if (!set.Value.TryGetProperty("emoticons", out var emoticons)) continue;
            foreach (var emote in emoticons.EnumerateArray())
            {
                var name = emote.GetProperty("name").GetString();
                var urls = emote.TryGetProperty("animated", out var animated) && animated.ValueKind == JsonValueKind.Object
                    ? animated
                    : emote.GetProperty("urls");
                var url = new[] { "2", "4", "1" }
                    .Select(size => urls.TryGetProperty(size, out var u) ? u.GetString() : null)
                    .FirstOrDefault(u => u is not null);
                if (name is not null && url is not null)
                    yield return new(name, new Uri(url.StartsWith("//") ? "https:" + url : url));
            }
        }
    }
}
