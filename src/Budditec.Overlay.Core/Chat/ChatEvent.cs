namespace Budditec.Overlay.Core.Chat;

public abstract record ChatEvent(ChatPlatform Platform);

public sealed record ChatMessageReceived(ChatMessage Message) : ChatEvent(Message.Platform);

public sealed record ChatMessageDeleted(ChatPlatform Platform, string MessageId) : ChatEvent(Platform);

/// <summary>A user was banned or timed out: remove their messages.</summary>
public sealed record ChatUserCleared(ChatPlatform Platform, string UserLogin) : ChatEvent(Platform);

public sealed record ChatCleared(ChatPlatform Platform) : ChatEvent(Platform);

public sealed record ChatConnectionChanged(ChatPlatform Platform, bool Connected, string Detail) : ChatEvent(Platform);

public interface IChatSource
{
    event Action<ChatEvent>? EventRaised;

    /// <summary>Connects and keeps reconnecting until cancelled.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
