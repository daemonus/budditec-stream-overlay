using Budditec.Overlay.Core.Chat.Twitch;

namespace Budditec.Overlay.Core.Tests;

public class IrcMessageTests
{
    [Fact]
    public void ParsesTagsPrefixCommandAndTrailing()
    {
        var message = IrcMessage.Parse(
            @"@badge-info=;color=#FF7F50;display-name=Candi;emotes=25:0-4;id=abc;msg-id=x\sy\:z :candi!candi@candi.tmi.twitch.tv PRIVMSG #candigotchi :Kappa hello there")!;

        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("candi", message.Nick);
        Assert.Equal(["#candigotchi", "Kappa hello there"], message.Parameters);
        Assert.Equal("Kappa hello there", message.Trailing);
        Assert.Equal("#FF7F50", message.Tag("color"));
        Assert.Equal("", message.Tag("badge-info"));
        Assert.Equal("x y;z", message.Tag("msg-id"));
    }

    [Fact]
    public void ParsesPingWithoutTagsOrPrefix()
    {
        var message = IrcMessage.Parse("PING :tmi.twitch.tv")!;

        Assert.Equal("PING", message.Command);
        Assert.Equal("tmi.twitch.tv", message.Trailing);
        Assert.Empty(message.Tags);
    }

    [Fact]
    public void ClearChatDistinguishesUserFromWholeChannel()
    {
        Assert.Equal(2, IrcMessage.Parse(":tmi.twitch.tv CLEARCHAT #chan :someuser")!.Parameters.Count);
        Assert.Single(IrcMessage.Parse(":tmi.twitch.tv CLEARCHAT #chan")!.Parameters);
    }

    [Fact]
    public void ActionMessagesAreUnwrapped()
    {
        var irc = IrcMessage.Parse(":u!u@u PRIVMSG #c :\u0001ACTION waves\u0001")!;

        var message = TwitchChatClient.ToChatMessage(irc, Chat.Emotes.EmptyEmoteLookup.Instance);

        Assert.True(message.IsAction);
        Assert.Equal("waves", Assert.IsType<Chat.TextFragment>(Assert.Single(message.Fragments)).Text);
    }
}
