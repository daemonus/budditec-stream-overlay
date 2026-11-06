using Budditec.Overlay.Core.Chat.Emotes;
using Budditec.Overlay.Core.Chat.Kick;
using Budditec.Overlay.Core.Chat.Simulation;
using Budditec.Overlay.Core.Chat.Twitch;
using Budditec.Overlay.Core.Chat.YouTube;
using Budditec.Overlay.Core.Diagnostics;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Core.Chat;

public static class ChatSources
{
    public static IReadOnlyList<ChatPlatform> Platforms { get; } = Enum.GetValues<ChatPlatform>();

    /// <summary>
    /// The source for one platform, or null when it has no channel set. With BUDDITEC_OVERLAY_SIMULATE_CHAT
    /// the simulator takes Twitch's place and the other platforms stay off.
    /// </summary>
    public static IChatSource? Create(ChatPlatform platform, OverlaySettings settings, HttpClient http, Action<string> log)
    {
        if (DevOptions.SimulatedChatRate is { } rate)
            return platform == ChatPlatform.Twitch ? new SimulatedChatSource(http, rate, TimeSpan.FromSeconds(20)) : null;

        var channel = settings.ChatChannel(platform);
        if (channel.Length == 0) return null;
        if (platform == ChatPlatform.YouTube) return new YouTubeChatClient(channel, http);

        var emotes = new ThirdPartyEmoteStore(http);
        emotes.Log += message => log($"{platform} emotes: {message}");
        return platform == ChatPlatform.Kick ? new KickChatClient(channel, http, emotes) : new TwitchChatClient(channel, emotes);
    }

    public static bool ChannelChanged(ChatPlatform platform, OverlaySettings old, OverlaySettings updated) =>
        !string.Equals(old.ChatChannel(platform), updated.ChatChannel(platform), StringComparison.OrdinalIgnoreCase);
}
