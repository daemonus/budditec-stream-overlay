using System.Text.Json;
using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Chat.Emotes;
using Budditec.Overlay.Core.Chat.Kick;

namespace Budditec.Overlay.Core.Tests;

public class KickChatClientTests
{
    private static ChatEvent? Parse(string eventName, object data, bool dataAsString = true)
    {
        var json = JsonSerializer.Serialize(new { @event = eventName, data = dataAsString ? (object)JsonSerializer.Serialize(data) : data });
        using var doc = JsonDocument.Parse(json);
        return KickChatClient.ToChatEvent(doc.RootElement, EmptyEmoteLookup.Instance);
    }

    [Fact]
    public void ChatMessageMapsSenderAndEmotes()
    {
        var e = Parse(@"App\Events\ChatMessageEvent", new
        {
            id = "a1b2",
            content = "hi [emote:37226:KEKW] there",
            created_at = "2026-10-13T12:00:00+00:00",
            sender = new { username = "Candi_G", slug = "candi-g", identity = new { color = "#75FD46" } },
        });

        var message = Assert.IsType<ChatMessageReceived>(e).Message;
        Assert.Equal(("a1b2", ChatPlatform.Kick, "candi-g", "Candi_G", "#75FD46"),
            (message.Id, message.Platform, message.UserLogin, message.UserDisplayName, message.UserColor));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), message.Timestamp);
        Assert.Collection(message.Fragments,
            f => Assert.Equal("hi ", Assert.IsType<TextFragment>(f).Text),
            f =>
            {
                var emote = Assert.IsType<EmoteFragment>(f);
                Assert.Equal("KEKW", emote.Code);
                Assert.Equal("https://files.kick.com/emotes/37226/fullsize", emote.Url.ToString());
            },
            f => Assert.Equal(" there", Assert.IsType<TextFragment>(f).Text));
    }

    [Fact]
    public void DataMayAlreadyBeAnObject()
    {
        var e = Parse(@"App\Events\MessageDeletedEvent", new { id = "x", message = new { id = "a1b2" } }, dataAsString: false);

        Assert.Equal("a1b2", Assert.IsType<ChatMessageDeleted>(e).MessageId);
    }

    [Fact]
    public void BansAndClearsRemoveMessages()
    {
        Assert.Equal("candi-g", Assert.IsType<ChatUserCleared>(
            Parse(@"App\Events\UserBannedEvent", new { user = new { slug = "candi-g", username = "Candi_G" } })).UserLogin);
        Assert.IsType<ChatCleared>(Parse(@"App\Events\ChatroomClearEvent", new { id = "1" }));
        Assert.Null(Parse(@"App\Events\PollUpdateEvent", new { poll = new { } }));
    }

    [Theory]
    [InlineData("xQc", "xqc")]
    [InlineData("@xqc", "xqc")]
    [InlineData("https://kick.com/xqc/videos", "xqc")]
    public void ParsesChannelInput(string input, string slug) => Assert.Equal(slug, KickChatClient.ParseSlug(input));
}
