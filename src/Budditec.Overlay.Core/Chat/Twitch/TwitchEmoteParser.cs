using System.Text;
using Budditec.Overlay.Core.Chat.Emotes;

namespace Budditec.Overlay.Core.Chat.Twitch;

public static class TwitchEmoteParser
{
    public static Uri NativeEmoteUrl(string emoteId) =>
        new($"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(emoteId)}/default/dark/2.0");

    /// <summary>
    /// Builds fragments from a PRIVMSG body and its "emotes" tag ("id:start-end,start-end/id:...").
    /// </summary>
    /// <remarks>
    /// Emote ids are opaque strings (newer ones look like "emotesv2_..."), and the positions count
    /// Unicode code points, not UTF-16 chars, so messages with emoji before an emote still line up.
    /// </remarks>
    public static IReadOnlyList<ChatFragment> BuildFragments(string text, string emotesTag, IEmoteLookup thirdParty)
    {
        var ranges = ParseRanges(emotesTag);
        var fragments = new List<ChatFragment>();
        if (ranges.Count == 0)
        {
            FragmentBuilder.AppendText(fragments, text, thirdParty);
            return fragments;
        }

        var offsets = CodePointOffsets(text);
        var codePoints = offsets.Length - 1;
        var cursor = 0;
        foreach (var (id, start, end) in ranges)
        {
            if (start < 0 || end < start || end >= codePoints) continue;
            var from = offsets[start];
            var to = offsets[end + 1];
            if (from < cursor) continue;

            FragmentBuilder.AppendText(fragments, text[cursor..from], thirdParty);
            fragments.Add(new EmoteFragment(text[from..to], NativeEmoteUrl(id)));
            cursor = to;
        }
        FragmentBuilder.AppendText(fragments, text[cursor..], thirdParty);
        return fragments;
    }

    private static List<(string Id, int Start, int End)> ParseRanges(string emotesTag)
    {
        var result = new List<(string, int, int)>();
        if (string.IsNullOrEmpty(emotesTag)) return result;

        foreach (var entry in emotesTag.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = entry.LastIndexOf(':');
            if (colon <= 0) continue;
            var id = entry[..colon];
            foreach (var range in entry[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var dash = range.IndexOf('-');
                if (dash > 0 && int.TryParse(range[..dash], out var start) && int.TryParse(range[(dash + 1)..], out var end))
                    result.Add((id, start, end));
            }
        }
        result.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return result;
    }

    /// <summary>UTF-16 offset of each code point, plus a final entry for the string length.</summary>
    private static int[] CodePointOffsets(string text)
    {
        var offsets = new List<int>(text.Length + 1);
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            offsets.Add(index);
            index += rune.Utf16SequenceLength;
        }
        offsets.Add(index);
        return offsets.ToArray();
    }
}
