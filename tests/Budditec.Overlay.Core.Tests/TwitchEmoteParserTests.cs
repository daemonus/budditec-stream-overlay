using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Chat.Emotes;
using Budditec.Overlay.Core.Chat.Twitch;

namespace Budditec.Overlay.Core.Tests;

public class TwitchEmoteParserTests
{
    private sealed class FakeLookup(params string[] codes) : IEmoteLookup
    {
        public Uri? Find(string code) => codes.Contains(code) ? new Uri($"https://example.test/{code}") : null;
    }

    [Fact]
    public void NewStyleEmoteIdsAreRendered()
    {
        // The romestylez overlay dropped these because it only accepted numeric ids.
        var fragments = TwitchEmoteParser.BuildFragments(
            "candigoBiiiigyawn", "emotesv2_b15b8ce38408423b8c97005b975ba2c1:0-16", EmptyEmoteLookup.Instance);

        var emote = Assert.IsType<EmoteFragment>(Assert.Single(fragments));
        Assert.Equal("candigoBiiiigyawn", emote.Code);
        Assert.Equal(
            "https://static-cdn.jtvnw.net/emoticons/v2/emotesv2_b15b8ce38408423b8c97005b975ba2c1/default/dark/2.0",
            emote.Url.ToString());
    }

    [Fact]
    public void SplitsTextAroundMultipleEmotes()
    {
        var fragments = TwitchEmoteParser.BuildFragments("Kappa hi Kappa bye", "25:0-4,9-13", EmptyEmoteLookup.Instance);

        Assert.Collection(fragments,
            f => Assert.Equal("Kappa", Assert.IsType<EmoteFragment>(f).Code),
            f => Assert.Equal(" hi ", Assert.IsType<TextFragment>(f).Text),
            f => Assert.Equal("Kappa", Assert.IsType<EmoteFragment>(f).Code),
            f => Assert.Equal(" bye", Assert.IsType<TextFragment>(f).Text));
    }

    [Fact]
    public void PositionsCountCodePointsNotUtf16Chars()
    {
        // The emoji is one code point but two UTF-16 chars; Twitch says Kappa starts at 2.
        var fragments = TwitchEmoteParser.BuildFragments("😀 Kappa", "25:2-6", EmptyEmoteLookup.Instance);

        Assert.Collection(fragments,
            f => Assert.Equal("😀 ", Assert.IsType<TextFragment>(f).Text),
            f => Assert.Equal("Kappa", Assert.IsType<EmoteFragment>(f).Code));
    }

    [Fact]
    public void OutOfRangePositionsAreIgnored()
    {
        var fragments = TwitchEmoteParser.BuildFragments("hi", "25:0-10", EmptyEmoteLookup.Instance);

        Assert.Equal("hi", Assert.IsType<TextFragment>(Assert.Single(fragments)).Text);
    }

    [Fact]
    public void ThirdPartyEmotesAreMatchedByWholeWord()
    {
        var fragments = TwitchEmoteParser.BuildFragments("so KEKW indeed KEKWW", "", new FakeLookup("KEKW"));

        Assert.Collection(fragments,
            f => Assert.Equal("so ", Assert.IsType<TextFragment>(f).Text),
            f => Assert.Equal("KEKW", Assert.IsType<EmoteFragment>(f).Code),
            f => Assert.Equal(" indeed KEKWW", Assert.IsType<TextFragment>(f).Text));
    }

    [Fact]
    public void ThirdPartyEmotesWorkBetweenNativeOnes()
    {
        var fragments = TwitchEmoteParser.BuildFragments("Kappa KEKW", "25:0-4", new FakeLookup("KEKW"));

        Assert.Collection(fragments,
            f => Assert.IsType<EmoteFragment>(f),
            f => Assert.Equal(" ", Assert.IsType<TextFragment>(f).Text),
            f => Assert.Equal("KEKW", Assert.IsType<EmoteFragment>(f).Code));
    }
}
