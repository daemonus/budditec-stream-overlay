using System.Text.Json;
using Budditec.Overlay.Core.Chat.Twitch;

namespace Budditec.Overlay.Core.Chat.Simulation;

/// <summary>
/// Generates busy-chat traffic for load and memory testing, without connecting to any real chat.
/// Uses real public emote images (7TV/BTTV global sets plus one large 7TV channel set) so emote
/// decoding and caching behave like a big channel: a few popular emotes and a long tail of rare ones.
/// </summary>
public sealed class SimulatedChatSource(HttpClient http, double messagesPerSecond, TimeSpan startDelay, int seed = 1234) : IChatSource
{
    /// <summary>Twitch user id whose public 7TV emote set provides the long tail (a large set).</summary>
    public const string DefaultSevenTvTwitchUserId = "641972806";

    private static readonly string[] Words =
        ["lol", "gg", "nice", "what", "no way", "true", "W", "L", "that was close", "chat is this real", "hello",
         "first time here", "let's go", "omg", "haha", "clip it", "bro", "same", "who", "why", "yes", "nope", "o7"];

    private static readonly (string Code, string Id)[] TwitchGlobals =
        [("Kappa", "25"), ("Kreygasm", "41"), ("BibleThump", "86"), ("4Head", "354"), ("Keepo", "1902"), ("LUL", "425618"), ("NotLikeThis", "58765")];

    private int _sent;

    public int Sent => Volatile.Read(ref _sent);

    public event Action<ChatEvent>? EventRaised;

    public async Task RunAsync(CancellationToken ct)
    {
        var emotes = await LoadEmotesAsync(ct);
        EventRaised?.Invoke(new ChatConnectionChanged(ChatPlatform.Twitch, true, $"Simulating {messagesPerSecond}/s with {emotes.Count} emotes"));
        await Task.Delay(startDelay, ct);

        var random = new Random(seed);
        var start = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            var due = (int)((DateTime.UtcNow - start).TotalSeconds * messagesPerSecond);
            while (_sent < due)
            {
                EventRaised?.Invoke(new ChatMessageReceived(CreateMessage(random, emotes, _sent)));
                Interlocked.Increment(ref _sent);
            }
            await Task.Delay(20, ct);
        }
    }

    private static ChatMessage CreateMessage(Random random, IReadOnlyList<EmoteFragment> emotes, int n)
    {
        var fragments = new List<ChatFragment>();
        var roll = random.NextDouble();
        if (roll < 0.1)
        {
            // Emote spam line: one emote repeated.
            var emote = Pick(random, emotes);
            for (var i = random.Next(3, 12); i > 0; i--)
            {
                fragments.Add(emote);
                fragments.Add(new TextFragment(" "));
            }
        }
        else
        {
            for (var i = random.Next(1, 8); i > 0; i--)
            {
                fragments.Add(roll < 0.5 && random.NextDouble() < 0.35 ? Pick(random, emotes) : new TextFragment(Words[random.Next(Words.Length)]));
                fragments.Add(new TextFragment(" "));
            }
        }

        var user = $"viewer{random.Next(5000)}";
        return new ChatMessage($"sim-{n}", ChatPlatform.Twitch, user, user, null, fragments, DateTimeOffset.UtcNow);
    }

    /// <summary>Skewed towards the front of the list so a few emotes are popular and most are rare.</summary>
    private static EmoteFragment Pick(Random random, IReadOnlyList<EmoteFragment> emotes) =>
        emotes[(int)(emotes.Count * Math.Pow(random.NextDouble(), 2.5))];

    private async Task<IReadOnlyList<EmoteFragment>> LoadEmotesAsync(CancellationToken ct)
    {
        var emotes = TwitchGlobals.Select(e => new EmoteFragment(e.Code, TwitchEmoteParser.NativeEmoteUrl(e.Id))).ToList();
        emotes.AddRange(await LoadSevenTvAsync("https://7tv.io/v3/emote-sets/global", root => root, ct));
        emotes.AddRange(await LoadSevenTvAsync($"https://7tv.io/v3/users/twitch/{DefaultSevenTvTwitchUserId}", root => root.GetProperty("emote_set"), ct));
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.betterttv.net/3/cached/emotes/global", ct));
            emotes.AddRange(doc.RootElement.EnumerateArray().Select(e => new EmoteFragment(
                e.GetProperty("code").GetString()!, new Uri($"https://cdn.betterttv.net/emote/{e.GetProperty("id").GetString()}/2x.webp"))));
        }
        catch (HttpRequestException)
        {
        }

        // Deterministic shuffle so popularity isn't tied to provider order.
        var random = new Random(seed);
        return emotes.OrderBy(_ => random.Next()).ToList();
    }

    private async Task<IEnumerable<EmoteFragment>> LoadSevenTvAsync(string url, Func<JsonElement, JsonElement> set, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            return set(doc.RootElement).GetProperty("emotes").EnumerateArray()
                .Select(e => new EmoteFragment(
                    e.GetProperty("name").GetString()!,
                    new Uri($"https:{e.GetProperty("data").GetProperty("host").GetProperty("url").GetString()}/2x.webp")))
                .ToList();
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }
}
