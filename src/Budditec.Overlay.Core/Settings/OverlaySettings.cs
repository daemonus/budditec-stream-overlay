using System.Text.RegularExpressions;
using Budditec.Overlay.Core.Chat;

namespace Budditec.Overlay.Core.Settings;

public sealed partial class OverlaySettings
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

    /// <summary>Ids of widgets the user has turned off (see OverlayWidgets).</summary>
    public List<string> DisabledWidgets { get; set; } = [];

    public bool IsWidgetEnabled(string widgetId) => !DisabledWidgets.Contains(widgetId);

    public string ChatChannel(ChatPlatform platform) => (platform switch
    {
        ChatPlatform.Twitch => Twitch,
        ChatPlatform.Kick => Kick,
        _ => YouTube,
    }).Channel.Trim();

    public bool HasAnyChatChannel() => Enum.GetValues<ChatPlatform>().Any(p => ChatChannel(p).Length > 0);

    /// <summary>Trims free-text fields in place. Run before Validate.</summary>
    public void Normalize()
    {
        foreach (var channel in new[] { Twitch, Kick, YouTube }) channel.Channel = channel.Channel.Trim();
        Obs.Url = Obs.Url.Trim();
        ChatStyle.FontFamily = ChatStyle.FontFamily.Trim();
        DisabledWidgets = DisabledWidgets.Distinct().ToList();
    }

    /// <summary>The first problem with these settings, or null when they're fine to save.</summary>
    public string? Validate()
    {
        var s = ChatStyle;
        var colors = new[] { ("Text colour", s.TextColor), ("Shadow colour", s.ShadowColor) }
            .Concat(s.PlatformColors.Select(c => ($"{c.Key} colour", c.Value)));
        foreach (var (name, value) in colors)
            if (!HexColor().IsMatch(value ?? ""))
                return $"{name} '{value}' isn't a colour. Use #RRGGBB or #AARRGGBB.";

        if (Obs.Url.Length > 0 && !(Uri.TryCreate(Obs.Url, UriKind.Absolute, out var url) && url.Scheme is "ws" or "wss"))
            return $"OBS URL '{Obs.Url}' should look like ws://127.0.0.1:4455.";
        if (s.FontFamily.Length == 0) return "Pick a font.";
        if (s.FontSize is < 6 or > 200) return "Font size should be between 6 and 200.";
        if (s.FontWeight is < 100 or > 1000) return "Font weight should be between 100 and 1000.";
        if (s.EmoteScale is < 0.25 or > 8) return "Emote size should be between 0.25x and 8x.";
        if (s.MaxMessages is < 1 or > 1000) return "Max messages should be between 1 and 1000.";
        if (s.FadeAfterSeconds < 0) return "Fade time can't be negative.";
        if (s.ShadowOpacity is < 0 or > 1) return "Shadow opacity should be between 0 and 1.";
        return null;
    }

    [GeneratedRegex("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();
}

/// <summary>The overlay widgets both front ends provide; ids key bounds and the on/off list.</summary>
public static class OverlayWidgets
{
    public const string Status = "status";
    public const string Chat = "chat";

    public static IReadOnlyList<(string Id, string Name)> All { get; } =
    [
        (Status, "Stream status (REC / LIVE / microphones)"),
        (Chat, "Chat"),
    ];
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
