using Budditec.Overlay.Core.Chat;

namespace Budditec.Overlay.Core.Settings;

/// <summary>
/// Renderer-agnostic chat styling. Each property maps one-to-one onto a CSS variable for a web renderer.
/// Colours are #RRGGBB or #AARRGGBB.
/// </summary>
public sealed class ChatStyle
{
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 18;
    public int FontWeight { get; set; } = 700;
    public string TextColor { get; set; } = "#FFFFFF";

    /// <summary>Colour usernames by platform; when false, use the user's own chat colour.</summary>
    public bool UsePlatformNameColors { get; set; } = true;

    public Dictionary<ChatPlatform, string> PlatformColors { get; set; } = new()
    {
        [ChatPlatform.Twitch] = "#BDA6FF",
        [ChatPlatform.Kick] = "#53FC18",
        [ChatPlatform.YouTube] = "#FF5C55",
    };

    public bool ShowPlatformIcon { get; set; } = true;

    public bool ShadowEnabled { get; set; } = true;
    public string ShadowColor { get; set; } = "#000000";
    public double ShadowOpacity { get; set; } = 1;
    public double ShadowBlur { get; set; } = 3;
    public double ShadowOffsetX { get; set; } = 1;
    public double ShadowOffsetY { get; set; } = 1;

    /// <summary>Emote height relative to the font size.</summary>
    public double EmoteScale { get; set; } = 1.6;

    public int MaxMessages { get; set; } = 50;

    /// <summary>Seconds before a message fades out; 0 keeps messages until pushed out.</summary>
    public double FadeAfterSeconds { get; set; }

    public string NameColorFor(ChatMessage message) =>
        UsePlatformNameColors && PlatformColors.TryGetValue(message.Platform, out var color)
            ? color
            : message.UserColor ?? TextColor;
}
