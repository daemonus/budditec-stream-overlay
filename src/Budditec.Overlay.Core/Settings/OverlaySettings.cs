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
    public LiveWidgetSettings Live { get; set; } = new();

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
        Audio.HiddenSources = Audio.HiddenSources.Distinct().ToList();
    }

    /// <summary>
    /// Brings settings from older versions forward: the "status" widget (REC, LIVE and microphones together) is
    /// now three widgets, laid out where its parts used to be.
    /// </summary>
    public void Upgrade()
    {
        const string legacyStatus = "status";
        if (Widgets.Remove(legacyStatus, out var s))
        {
            Widgets.TryAdd(OverlayWidgets.Recording, s with { Width = 110, Height = 36 });
            Widgets.TryAdd(OverlayWidgets.Live, s with { X = s.X + 110, Width = Math.Max(s.Width - 110, 120), Height = 36 });
            Widgets.TryAdd(OverlayWidgets.Microphones, s with { Y = s.Y + 40, Height = Math.Max(s.Height - 40, 60) });
        }
        if (DisabledWidgets.Remove(legacyStatus))
            DisabledWidgets.AddRange([OverlayWidgets.Recording, OverlayWidgets.Live, OverlayWidgets.Microphones]);
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

/// <summary>The overlay widgets; ids key bounds and the on/off list.</summary>
public static class OverlayWidgets
{
    public const string Recording = "recording";
    public const string Live = "live";
    public const string Microphones = "microphones";
    public const string Chat = "chat";
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
    /// <summary>
    /// Input names the microphones widget leaves out, in every scene. A hide list rather than a show list, so
    /// new sources and sources from other scenes show by default, and switching scenes doesn't lose the choice.
    /// </summary>
    public List<string> HiddenSources { get; set; } = [];

    public bool IsSourceShown(string name) => !HiddenSources.Contains(name);

    public MicrophoneDisplay Display { get; set; } = MicrophoneDisplay.Full;

    /// <summary>What goes under each dot in the minimal display.</summary>
    public MicrophoneLabel MinimalLabel { get; set; } = MicrophoneLabel.Number;

    public double SpeakingThresholdDb { get; set; } = -45;

    /// <summary>How long a source stays "active" after its level drops below the threshold.</summary>
    public int HoldMilliseconds { get; set; } = 250;
}

public enum MicrophoneDisplay
{
    /// <summary>Icon, name and level meter per source.</summary>
    Full,

    /// <summary>A dot per source, coloured by level while it's active.</summary>
    Minimal,
}

public enum MicrophoneLabel
{
    /// <summary>1, 2, 3... in the order OBS lists the sources.</summary>
    Number,
    ShortName,
    None,
}

public sealed class LiveWidgetSettings
{
    /// <summary>Show the current scene, or why OBS isn't connected, next to LIVE.</summary>
    public bool ShowObsDetail { get; set; } = true;
}

public sealed record WidgetBounds(int X, int Y, int Width, int Height);
