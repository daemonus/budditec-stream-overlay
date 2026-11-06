using System.Text.Json.Serialization;

namespace Budditec.Overlay.Core.Chat;

public enum ChatPlatform
{
    Twitch,
    Kick,
    YouTube,
}

/// <summary>
/// A chat message from any platform, already split into text and emote pieces so renderers only draw.
/// </summary>
public sealed record ChatMessage(
    string Id,
    ChatPlatform Platform,
    string UserLogin,
    string UserDisplayName,
    string? UserColor,
    IReadOnlyList<ChatFragment> Fragments,
    DateTimeOffset Timestamp,
    bool IsAction = false);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextFragment), "text")]
[JsonDerivedType(typeof(EmoteFragment), "emote")]
public abstract record ChatFragment;

public sealed record TextFragment(string Text) : ChatFragment;

public sealed record EmoteFragment(string Code, Uri Url) : ChatFragment;
