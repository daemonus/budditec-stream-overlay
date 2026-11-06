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
        TextColor = style.TextColor;
        UsePlatformNameColors = style.UsePlatformNameColors;
        TwitchColor = style.PlatformColors.GetValueOrDefault(ChatPlatform.Twitch, "#BDA6FF");
        KickColor = style.PlatformColors.GetValueOrDefault(ChatPlatform.Kick, "#53FC18");
        YouTubeColor = style.PlatformColors.GetValueOrDefault(ChatPlatform.YouTube, "#FF5C55");
        ShowPlatformIcon = style.ShowPlatformIcon;
        ShadowEnabled = style.ShadowEnabled;
        ShadowColor = style.ShadowColor;
        ShadowBlur = style.ShadowBlur;
        ShadowOffset = style.ShadowOffsetX;
        EmoteScale = style.EmoteScale;
        MaxMessages = style.MaxMessages;
        FadeAfterSeconds = style.FadeAfterSeconds;

        foreach (var (id, name) in OverlayWidgets.All)
            Widgets.Add(new SelectableWidget(id, name) { IsEnabled = s.IsWidgetEnabled(id) });

        SpeakingThresholdDb = s.Audio.SpeakingThresholdDb;
        LoadSources(controller.ObsStatus, s.Audio.SelectedSources);
        controller.ObsStatusChanged += OnObsStatusChanged;
    }

    public IReadOnlyList<string> FontFamilies { get; } =
        FontManager.Current.SystemFonts.Select(f => f.Name).Order().ToList();

    public IReadOnlyList<int> FontWeights { get; } = [300, 400, 500, 600, 700, 800, 900];

    [ObservableProperty] public partial string TwitchChannel { get; set; }
    [ObservableProperty] public partial string KickChannel { get; set; }
    [ObservableProperty] public partial string YouTubeChannel { get; set; }
    [ObservableProperty] public partial string ObsUrl { get; set; }
    [ObservableProperty] public partial string ObsPassword { get; set; }
    [ObservableProperty] public partial string FontFamily { get; set; }
    [ObservableProperty] public partial double FontSize { get; set; }
    [ObservableProperty] public partial int FontWeight { get; set; }
    [ObservableProperty] public partial string TextColor { get; set; }
    [ObservableProperty] public partial bool UsePlatformNameColors { get; set; }
    [ObservableProperty] public partial string TwitchColor { get; set; }
    [ObservableProperty] public partial string KickColor { get; set; }
    [ObservableProperty] public partial string YouTubeColor { get; set; }
    [ObservableProperty] public partial bool ShowPlatformIcon { get; set; }
    [ObservableProperty] public partial bool ShadowEnabled { get; set; }
    [ObservableProperty] public partial string ShadowColor { get; set; }
    [ObservableProperty] public partial double ShadowBlur { get; set; }
    [ObservableProperty] public partial double ShadowOffset { get; set; }
    [ObservableProperty] public partial double EmoteScale { get; set; }
    [ObservableProperty] public partial int MaxMessages { get; set; }
    [ObservableProperty] public partial double FadeAfterSeconds { get; set; }
    [ObservableProperty] public partial double SpeakingThresholdDb { get; set; }
    [ObservableProperty] public partial string SceneName { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }

    public ObservableCollection<SelectableSource> Sources { get; } = [];

    public ObservableCollection<SelectableWidget> Widgets { get; } = [];

    [RelayCommand]
    private void Apply()
    {
        var settings = SettingsStore.Clone(_controller.Settings);
        settings.Twitch.Channel = TwitchChannel;
        settings.Kick.Channel = KickChannel;
        settings.YouTube.Channel = YouTubeChannel;
        settings.Obs.Url = ObsUrl;
        settings.Obs.Password = ObsPassword;
        settings.DisabledWidgets = Widgets.Where(w => !w.IsEnabled).Select(w => w.Id).ToList();
        settings.ChatStyle = new ChatStyle
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
            TextColor = TextColor,
            UsePlatformNameColors = UsePlatformNameColors,
            PlatformColors = new()
            {
                [ChatPlatform.Twitch] = TwitchColor,
                [ChatPlatform.Kick] = KickColor,
                [ChatPlatform.YouTube] = YouTubeColor,
            },
            ShowPlatformIcon = ShowPlatformIcon,
            ShadowEnabled = ShadowEnabled,
            ShadowColor = ShadowColor,
            ShadowBlur = ShadowBlur,
            ShadowOffsetX = ShadowOffset,
            ShadowOffsetY = ShadowOffset,
            EmoteScale = EmoteScale,
            MaxMessages = MaxMessages,
            FadeAfterSeconds = FadeAfterSeconds,
        };
        settings.Audio.SpeakingThresholdDb = SpeakingThresholdDb;
        // Ticking every source is the same as "show all", which also picks up sources added later.
        // With OBS disconnected there's nothing listed, so keep the saved selection.
        if (Sources.Count > 0)
            settings.Audio.SelectedSources = Sources.All(s => s.IsSelected) ? [] : Sources.Where(s => s.IsSelected).Select(s => s.Name).ToList();
        Error = _controller.Apply(settings);
    }

    [RelayCommand]
    private void TestMessage() => _controller.SendTestMessage();

    public void Detach() => _controller.ObsStatusChanged -= OnObsStatusChanged;

    private void OnObsStatusChanged(ObsStatus status)
    {
        var selected = Sources.Where(s => s.IsSelected).Select(s => s.Name).ToList();
        LoadSources(status, Sources.Count == 0 || Sources.All(s => s.IsSelected) ? [] : selected);
    }

    private void LoadSources(ObsStatus status, IReadOnlyList<string> selected)
    {
        SceneName = status.Connected ? $"Audio sources in \"{status.CurrentScene}\"" : $"OBS not connected: {status.Detail}";
        if (Sources.Select(s => s.Name).SequenceEqual(status.AudioSources.Select(s => s.Name))) return;
        Sources.Clear();
        foreach (var source in status.AudioSources)
            Sources.Add(new SelectableSource(source.Name) { IsSelected = selected.Count == 0 || selected.Contains(source.Name) });
    }
}

public sealed partial class SelectableSource(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public sealed partial class SelectableWidget(string id, string name) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;

    [ObservableProperty] public partial bool IsEnabled { get; set; }
}
