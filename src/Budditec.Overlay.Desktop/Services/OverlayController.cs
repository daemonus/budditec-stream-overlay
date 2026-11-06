using System.Text.Json;
using Avalonia.Threading;
using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Chat.Simulation;
using Budditec.Overlay.Core.Chat.Twitch;
using Budditec.Overlay.Core.Obs;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Desktop.Services;

/// <summary>
/// Owns settings and the background services (chat sources, OBS), and re-raises their events on the UI thread.
/// </summary>
public sealed class OverlayController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<ChatPlatform, CancellationTokenSource> _chatCts = new();
    private CancellationTokenSource _obsCts = new();
    private ObsMonitor? _obs;

    public OverlayController(SettingsStore store)
    {
        _store = store;
        Settings = store.Load();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Budditec.Overlay/0.1");
    }

    public OverlaySettings Settings { get; private set; }

    public HttpClient Http => _http;

    /// <summary>Set when BUDDITEC_OVERLAY_SIMULATE_CHAT replaces real chat.</summary>
    public SimulatedChatSource? Simulation { get; private set; }

    public ObsStatus ObsStatus => _obs?.Status ?? ObsStatus.Disconnected("Not started");

    public event Action<ChatEvent>? ChatEvent;
    public event Action<ObsStatus>? ObsStatusChanged;
    public event Action<IReadOnlyDictionary<string, AudioLevel>>? LevelsUpdated;

    /// <summary>Raised after settings are applied (style, audio selection, layout).</summary>
    public event Action<OverlaySettings>? SettingsChanged;

    public void Start()
    {
        foreach (var platform in ChatSources.Platforms) StartChat(platform);
        StartObs();
    }

    public void Apply(OverlaySettings settings)
    {
        var old = Settings;
        Settings = settings;
        _store.Save(settings);

        foreach (var platform in ChatSources.Platforms)
            if (ChatSources.ChannelChanged(platform, old, settings)) StartChat(platform);
        if (old.Obs.Url != settings.Obs.Url || old.Obs.Password != settings.Obs.Password) StartObs();
        SettingsChanged?.Invoke(settings);
    }

    public void SaveWidgetBounds(string widgetId, WidgetBounds bounds)
    {
        Settings.Widgets[widgetId] = bounds;
        _store.Save(Settings);
    }

    /// <summary>A sample message with a native emote and a 7TV emote, for previewing the style.</summary>
    public void SendTestMessage()
    {
        var message = new ChatMessage(
            Guid.NewGuid().ToString("N"), ChatPlatform.Twitch, "budditec", "Budditec", "#FF7F50",
            [
                new TextFragment("Style preview "),
                new EmoteFragment("Kappa", TwitchEmoteParser.NativeEmoteUrl("25")),
                new TextFragment(" animated: "),
                new EmoteFragment("RainTime", new Uri("https://cdn.7tv.app/emote/01FCY771D800007PQ2DF3GDTN6/2x.webp")),
                new TextFragment(" and a long line of text to check wrapping works nicely across lines."),
            ],
            DateTimeOffset.UtcNow);
        ChatEvent?.Invoke(new ChatMessageReceived(message));
    }

    private void StartChat(ChatPlatform platform)
    {
        if (_chatCts.Remove(platform, out var previous)) previous.Cancel();
        var client = ChatSources.Create(platform, Settings, _http, message => System.Diagnostics.Trace.WriteLine($"[chat] {message}"));
        if (client is null) return;
        if (client is SimulatedChatSource simulation) Simulation = simulation;

        var cts = _chatCts[platform] = new CancellationTokenSource();
        var ct = cts.Token;
        client.EventRaised += e =>
        {
            if (!ct.IsCancellationRequested) Dispatcher.UIThread.Post(() => ChatEvent?.Invoke(e));
        };
        _ = Task.Run(() => client.RunAsync(ct), ct);
    }

    private void StartObs()
    {
        _obsCts.Cancel();
        _obsCts = new CancellationTokenSource();
        if (!Uri.TryCreate(Settings.Obs.Url, UriKind.Absolute, out var url)) return;

        var obs = _obs = new ObsMonitor(url, Settings.Obs.Password);
        var ct = _obsCts.Token;
        obs.StatusChanged += status =>
        {
            if (!ct.IsCancellationRequested) Dispatcher.UIThread.Post(() => ObsStatusChanged?.Invoke(status));
        };
        obs.LevelsUpdated += levels =>
        {
            if (!ct.IsCancellationRequested) Dispatcher.UIThread.Post(() => LevelsUpdated?.Invoke(levels), DispatcherPriority.Render);
        };
        _ = Task.Run(() => obs.RunAsync(ct), ct);
    }

    public static OverlaySettings Clone(OverlaySettings settings) =>
        JsonSerializer.Deserialize<OverlaySettings>(JsonSerializer.Serialize(settings))!;

    public void Dispose()
    {
        foreach (var cts in _chatCts.Values) cts.Cancel();
        _obsCts.Cancel();
        _http.Dispose();
    }
}
