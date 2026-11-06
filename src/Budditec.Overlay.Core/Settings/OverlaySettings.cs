using Budditec.Overlay.Core.Chat;

namespace Budditec.Overlay.Core.Settings;

public sealed class OverlaySettings
{
    public ChatChannelSettings Twitch { get; set; } = new();
    public ChatChannelSettings Kick { get; set; } = new();

    /// <summary>Channel is a handle (@name), a channel id (UC...) or a channel/video URL.</summary>
    public ChatChannelSettings YouTube { get; set; } = new();

    public ObsSettings Obs { get; set; } = new();
    public ChatStyle ChatStyle { get; set; } = new();
    public AudioWidgetSettings Audio { get; set; } = new();

    /// <summary>Saved widget bounds in screen pixels, keyed by widget id.</summary>
    public Dictionary<string, WidgetBounds> Widgets { get; set; } = new();

    public string ChatChannel(ChatPlatform platform) => (platform switch
    {
        ChatPlatform.Twitch => Twitch,
        ChatPlatform.Kick => Kick,
        _ => YouTube,
    }).Channel.Trim();

    public bool HasAnyChatChannel() => Enum.GetValues<ChatPlatform>().Any(p => ChatChannel(p).Length > 0);
}

public sealed class ChatChannelSettings
{
    public string Channel { get; set; } = "";
}

public sealed class ObsSettings
{
    public string Url { get; set; } = "ws://127.0.0.1:4455";

    /// <summary>
    /// Stored in the user's settings file in plain text. Fine for a local prototype; move to the OS
    /// credential store before sharing builds.
    /// </summary>
    public string Password { get; set; } = "";
}

public sealed class AudioWidgetSettings
{
    /// <summary>Input names to show. Empty means every audio input in the current scene.</summary>
    public List<string> SelectedSources { get; set; } = [];

    public double SpeakingThresholdDb { get; set; } = -45;

    /// <summary>How long a source stays "active" after its level drops below the threshold.</summary>
    public int HoldMilliseconds { get; set; } = 250;
}

public sealed record WidgetBounds(int X, int Y, int Width, int Height);
