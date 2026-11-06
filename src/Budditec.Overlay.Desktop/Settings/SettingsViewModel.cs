using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Obs;
using Budditec.Overlay.Core.Settings;
using Budditec.Overlay.Desktop.Services;

namespace Budditec.Overlay.Desktop.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly OverlayController _controller;

    // Hidden source names: the saved list plus unapplied ticks, so they survive scene changes while this is open.
    private readonly HashSet<string> _hidden;

    public SettingsViewModel(OverlayController controller)
    {
        _controller = controller;
        var s = controller.Settings;
        TwitchChannel = s.Twitch.Channel;
        KickChannel = s.Kick.Channel;
        YouTubeChannel = s.YouTube.Channel;
        ObsUrl = s.Obs.Url;
        ObsPassword = s.Obs.Password;

        var style = s.ChatStyle;
        FontFamily = style.FontFamily;
        FontSize = style.FontSize;
        FontWeight = style.FontWeight;
        TextColor = ParseColor(style.TextColor, Colors.White);
        UsePlatformNameColors = style.UsePlatformNameColors;
        TwitchColor = ParseColor(style.PlatformColors.GetValueOrDefault(ChatPlatform.Twitch), Color.Parse("#BDA6FF"));
        KickColor = ParseColor(style.PlatformColors.GetValueOrDefault(ChatPlatform.Kick), Color.Parse("#53FC18"));
        YouTubeColor = ParseColor(style.PlatformColors.GetValueOrDefault(ChatPlatform.YouTube), Color.Parse("#FF5C55"));
        ShowPlatformIcon = style.ShowPlatformIcon;
        ShadowEnabled = style.ShadowEnabled;
        ShadowColor = ParseColor(style.ShadowColor, Colors.Black);
        ShadowBlur = style.ShadowBlur;
        ShadowOffset = style.ShadowOffsetX;
        EmoteScale = style.EmoteScale;
        MaxMessages = style.MaxMessages;
        FadeAfterSeconds = style.FadeAfterSeconds;

        ChatEnabled = s.IsWidgetEnabled(OverlayWidgets.Chat);
        LiveEnabled = s.IsWidgetEnabled(OverlayWidgets.Live);
        MicrophonesEnabled = s.IsWidgetEnabled(OverlayWidgets.Microphones);
        RecordingEnabled = s.IsWidgetEnabled(OverlayWidgets.Recording);
        ShowObsDetail = s.Live.ShowObsDetail;

        MicrophoneDisplay = MicrophoneDisplays.First(o => o.Value == s.Audio.Display);
        MinimalLabel = MinimalLabels.First(o => o.Value == s.Audio.MinimalLabel);
        SpeakingThresholdDb = s.Audio.SpeakingThresholdDb;
        _hidden = [.. s.Audio.HiddenSources];
        LoadSources(controller.ObsStatus);
        controller.ObsStatusChanged += OnObsStatusChanged;
    }

    public IReadOnlyList<string> FontFamilies { get; } =
        FontManager.Current.SystemFonts.Select(f => f.Name).Order().ToList();

    public IReadOnlyList<int> FontWeights { get; } = [300, 400, 500, 600, 700, 800, 900];

    public IReadOnlyList<Option<MicrophoneDisplay>> MicrophoneDisplays { get; } =
    [
        new(Core.Settings.MicrophoneDisplay.Full, "Full: icon, name and level meter"),
        new(Core.Settings.MicrophoneDisplay.Minimal, "Minimal: a dot per source"),
    ];

    public IReadOnlyList<Option<MicrophoneLabel>> MinimalLabels { get; } =
    [
        new(MicrophoneLabel.Number, "Number (order in OBS)"),
        new(MicrophoneLabel.ShortName, "Short name"),
        new(MicrophoneLabel.None, "Nothing"),
    ];

    [ObservableProperty] public partial string TwitchChannel { get; set; }
    [ObservableProperty] public partial string KickChannel { get; set; }
    [ObservableProperty] public partial string YouTubeChannel { get; set; }
    [ObservableProperty] public partial string ObsUrl { get; set; }
    [ObservableProperty] public partial string ObsPassword { get; set; }
    [ObservableProperty] public partial bool ChatEnabled { get; set; }
    [ObservableProperty] public partial bool LiveEnabled { get; set; }
    [ObservableProperty] public partial bool MicrophonesEnabled { get; set; }
    [ObservableProperty] public partial bool RecordingEnabled { get; set; }
    [ObservableProperty] public partial bool ShowObsDetail { get; set; }
    [ObservableProperty] public partial string FontFamily { get; set; }
    [ObservableProperty] public partial double FontSize { get; set; }
    [ObservableProperty] public partial int FontWeight { get; set; }
    [ObservableProperty] public partial Color TextColor { get; set; }
    [ObservableProperty] public partial bool UsePlatformNameColors { get; set; }
    [ObservableProperty] public partial Color TwitchColor { get; set; }
    [ObservableProperty] public partial Color KickColor { get; set; }
    [ObservableProperty] public partial Color YouTubeColor { get; set; }
    [ObservableProperty] public partial bool ShowPlatformIcon { get; set; }
    [ObservableProperty] public partial bool ShadowEnabled { get; set; }
    [ObservableProperty] public partial Color ShadowColor { get; set; }
    [ObservableProperty] public partial double ShadowBlur { get; set; }
    [ObservableProperty] public partial double ShadowOffset { get; set; }
    [ObservableProperty] public partial double EmoteScale { get; set; }
    [ObservableProperty] public partial int MaxMessages { get; set; }
    [ObservableProperty] public partial double FadeAfterSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMinimalDisplay))]
    public partial Option<MicrophoneDisplay> MicrophoneDisplay { get; set; }

    public bool IsMinimalDisplay => MicrophoneDisplay.Value == Core.Settings.MicrophoneDisplay.Minimal;

    [ObservableProperty] public partial Option<MicrophoneLabel> MinimalLabel { get; set; }
    [ObservableProperty] public partial double SpeakingThresholdDb { get; set; }
    [ObservableProperty] public partial string SceneName { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }

    public ObservableCollection<SelectableSource> Sources { get; } = [];

    [RelayCommand]
    private void Apply()
    {
        var settings = SettingsStore.Clone(_controller.Settings);
        settings.Twitch.Channel = TwitchChannel;
        settings.Kick.Channel = KickChannel;
        settings.YouTube.Channel = YouTubeChannel;
        settings.Obs.Url = ObsUrl;
        settings.Obs.Password = ObsPassword;
        settings.DisabledWidgets = new[]
            {
                (OverlayWidgets.Chat, ChatEnabled),
                (OverlayWidgets.Live, LiveEnabled),
                (OverlayWidgets.Microphones, MicrophonesEnabled),
                (OverlayWidgets.Recording, RecordingEnabled),
            }
            .Where(w => !w.Item2).Select(w => w.Item1).ToList();
        settings.Live.ShowObsDetail = ShowObsDetail;
        settings.ChatStyle = BuildChatStyle();
        settings.Audio.Display = MicrophoneDisplay.Value;
        settings.Audio.MinimalLabel = MinimalLabel.Value;
        settings.Audio.SpeakingThresholdDb = SpeakingThresholdDb;
        RememberTicks();
        settings.Audio.HiddenSources = [.. _hidden];
        Error = _controller.Apply(settings);
    }

    /// <summary>The chat style as currently edited (not yet applied), also used for the preview.</summary>
    public ChatStyle BuildChatStyle() => new()
    {
        FontFamily = FontFamily,
        FontSize = FontSize,
        FontWeight = FontWeight,
        TextColor = ToHex(TextColor),
        UsePlatformNameColors = UsePlatformNameColors,
        PlatformColors = new()
        {
            [ChatPlatform.Twitch] = ToHex(TwitchColor),
            [ChatPlatform.Kick] = ToHex(KickColor),
            [ChatPlatform.YouTube] = ToHex(YouTubeColor),
        },
        ShowPlatformIcon = ShowPlatformIcon,
        ShadowEnabled = ShadowEnabled,
        ShadowColor = ToHex(ShadowColor),
        ShadowOpacity = _controller.Settings.ChatStyle.ShadowOpacity,
        ShadowBlur = ShadowBlur,
        ShadowOffsetX = ShadowOffset,
        ShadowOffsetY = ShadowOffset,
        EmoteScale = EmoteScale,
        MaxMessages = MaxMessages,
        FadeAfterSeconds = FadeAfterSeconds,
    };

    private static Color ParseColor(string? value, Color fallback) => Color.TryParse(value, out var color) ? color : fallback;

    private static string ToHex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    [RelayCommand]
    private void TestMessage() => _controller.SendTestMessage();

    public void Detach() => _controller.ObsStatusChanged -= OnObsStatusChanged;

    private void OnObsStatusChanged(ObsStatus status)
    {
        RememberTicks();
        LoadSources(status);
    }

    private void RememberTicks()
    {
        foreach (var source in Sources)
            if (source.IsSelected) _hidden.Remove(source.Name);
            else _hidden.Add(source.Name);
    }

    /// <summary>Lists the current scene's audio sources, then hidden ones from elsewhere so they can be shown again.</summary>
    private void LoadSources(ObsStatus status)
    {
        SceneName = status.Connected
            ? $"Untick a source to leave it out of the widget. Choices apply in every scene. Audio sources in \"{status.CurrentScene}\":"
            : $"OBS not connected: {status.Detail}";
        var inScene = status.AudioSources.Select(s => s.Name).ToList();
        var sources = inScene.Concat(_hidden.Except(inScene))
            .Select(name => new SelectableSource(name, inScene.Contains(name)) { IsSelected = !_hidden.Contains(name) })
            .ToList();
        if (Sources.Select(s => s.Label).SequenceEqual(sources.Select(s => s.Label))) return;
        Sources.Clear();
        foreach (var source in sources) Sources.Add(source);
    }
}

public sealed record Option<T>(T Value, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class SelectableSource(string name, bool inScene) : ObservableObject
{
    public string Name { get; } = name;

    public string Label { get; } = inScene ? name : $"{name} (not in this scene)";

    [ObservableProperty] public partial bool IsSelected { get; set; }
}
