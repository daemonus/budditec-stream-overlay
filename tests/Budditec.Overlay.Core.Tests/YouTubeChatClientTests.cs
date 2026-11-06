using System.Text.Json;
using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Chat.YouTube;

namespace Budditec.Overlay.Core.Tests;

public class YouTubeChatClientTests
{
    // Trimmed from a real get_live_chat response.
    private const string Response = """
        {
          "continuations": [{ "invalidationContinuationData": { "timeoutMs": 10000, "continuation": "next-token" } }],
          "actions": [
            { "addChatItemAction": { "item": { "liveChatTextMessageRenderer": {
                "id": "msg1",
                "message": { "runs": [
                  { "text": "hello " },
                  { "emoji": { "emojiId": "🥀", "shortcuts": [":wilted_flower:"], "image": { "thumbnails": [{ "url": "https://fonts.gstatic.com/x.png" }] } } },
                  { "text": " and " },
                  { "emoji": { "emojiId": "UCabc/xyz", "shortcuts": [":yt:"], "isCustomEmoji": true,
                      "image": { "thumbnails": [{ "url": "https://yt3.ggpht.com/small" }, { "url": "https://yt3.ggpht.com/big" }] } } }
                ] },
                "authorName": { "simpleText": "@sun_sawariya_a" },
                "authorExternalChannelId": "UC1LkBqVQfx2Jin1n0zZSW_A",
                "timestampUsec": "1793870446515832"
            } } } },
            { "addChatItemAction": { "item": { "liveChatPaidMessageRenderer": {
                "id": "msg2", "purchaseAmountText": { "simpleText": "$5.00" },
                "message": { "runs": [{ "text": "thanks" }] },
                "authorName": { "simpleText": "@donor" }, "authorExternalChannelId": "UCdonor"
            } } } },
            { "addChatItemAction": { "item": { "liveChatViewerEngagementMessageRenderer": { "id": "ignored" } } } },
            { "removeChatItemAction": { "targetItemId": "msg0" } },
            { "markChatItemsByAuthorAsDeletedAction": { "externalChannelId": "UCspammer" } }
          ]
        }
        """;

    [Fact]
    public void ParsesMessagesEmojiAndModeration()
    {
        using var doc = JsonDocument.Parse(Response);

        var events = YouTubeChatClient.ParseActions(doc.RootElement).ToList();

        Assert.Equal(4, events.Count);
        var message = Assert.IsType<ChatMessageReceived>(events[0]).Message;
        Assert.Equal(("msg1", ChatPlatform.YouTube, "UC1LkBqVQfx2Jin1n0zZSW_A", "sun_sawariya_a"),
            (message.Id, message.Platform, message.UserLogin, message.UserDisplayName));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793870446515), message.Timestamp);
        Assert.Collection(message.Fragments,
            f => Assert.Equal("hello 🥀 and ", Assert.IsType<TextFragment>(f).Text),
            f => Assert.Equal(("yt", "https://yt3.ggpht.com/big"), (Assert.IsType<EmoteFragment>(f).Code.Trim(':'), ((EmoteFragment)f).Url.ToString())));

        var paid = Assert.IsType<ChatMessageReceived>(events[1]).Message;
        Assert.Equal("[$5.00] thanks", Assert.IsType<TextFragment>(Assert.Single(paid.Fragments)).Text);

        Assert.Equal("msg0", Assert.IsType<ChatMessageDeleted>(events[2]).MessageId);
        Assert.Equal("UCspammer", Assert.IsType<ChatUserCleared>(events[3]).UserLogin);
    }

    [Fact]
    public void ReadsNextContinuation()
    {
        using var doc = JsonDocument.Parse(Response);

        Assert.Equal(("next-token", 10000d), YouTubeChatClient.NextContinuation(doc.RootElement));
    }

    [Fact]
    public void PrefersLiveChatOverTopChat()
    {
        using var doc = JsonDocument.Parse("""
            { "contents": { "liveChatRenderer": {
                "continuations": [{ "invalidationContinuationData": { "continuation": "top" } }],
                "header": { "liveChatHeaderRenderer": { "viewSelector": { "sortFilterSubMenuRenderer": { "subMenuItems": [
                  { "title": "Top chat", "continuation": { "reloadContinuationData": { "continuation": "top-reload" } } },
                  { "title": "Live chat", "continuation": { "reloadContinuationData": { "continuation": "all-reload" } } }
                ] } } } }
            } } }
            """);

        Assert.Equal("all-reload", YouTubeChatClient.FindContinuation(doc.RootElement));
    }

    [Theory]
    [InlineData("@LofiGirl", null, "https://www.youtube.com/@LofiGirl/live")]
    [InlineData("LofiGirl", null, "https://www.youtube.com/@LofiGirl/live")]
    [InlineData("UCSJ4gkVC6NrvII8umztf0Ow", null, "https://www.youtube.com/channel/UCSJ4gkVC6NrvII8umztf0Ow/live")]
    [InlineData("https://www.youtube.com/@LofiGirl/streams", null, "https://www.youtube.com/@LofiGirl/live")]
    [InlineData("https://www.youtube.com/channel/UCSJ4gkVC6NrvII8umztf0Ow", null, "https://www.youtube.com/channel/UCSJ4gkVC6NrvII8umztf0Ow/live")]
    [InlineData("https://www.youtube.com/watch?v=4t4AQ15yj1M&t=1", "4t4AQ15yj1M", null)]
    [InlineData("https://youtu.be/4t4AQ15yj1M", "4t4AQ15yj1M", null)]
    [InlineData("https://www.youtube.com/live/4t4AQ15yj1M", "4t4AQ15yj1M", null)]
    public void ParsesChannelInput(string input, string? videoId, string? livePage) =>
        Assert.Equal((videoId, livePage), YouTubeChatClient.ParseTarget(input));
}
