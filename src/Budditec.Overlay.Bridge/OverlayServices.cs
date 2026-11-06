using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Chat.Simulation;
using Budditec.Overlay.Core.Chat.Twitch;
using Budditec.Overlay.Core.Diagnostics;
using Budditec.Overlay.Core.Obs;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Bridge;

/// <summary>
/// Runs the same Core services as the desktop app and forwards their events to the hub. Re-reads the
/// shared settings file when it changes, so the Avalonia settings window (or a hand edit) drives both.
/// </summary>
public sealed class OverlayServices(BridgeHub hub, SettingsStore store, ILogger<OverlayServices> log) : BackgroundService
{
    private readonly HttpClient _http = CreateHttp();
    private readonly Dictionary<ChatPlatform, CancellationTokenSource> _chatCts = new();
    private CancellationTokenSource _obsCts = new();
    private ObsMonitor? _obs;
    private OverlaySettings _settings = store.Load();

    /// <summary>Set when BUDDITEC_OVERLAY_SIMULATE_CHAT replaces real chat.</summary>
    public SimulatedChatSource? Simulation { get; private set; }

    public object SettingsMessage => new { type = "settings", chatStyle = _settings.ChatStyle, audio = _settings.Audio };

    public object ObsMessage => new { type = "obs", status = _obs?.Status ?? ObsStatus.Disconnected("Not started") };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        foreach (var platform in ChatSources.Platforms) StartChat(platform, ct);
        StartObs(ct);
        if (DevOptions.StatsPath is { } statsPath)
            _ = DevOptions.WriteStatsAsync(statsPath, () => [("messages", Simulation?.Sent ?? 0)], ct);

        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(store.Path)!, Path.GetFileName(store.Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        var changed = 0;
        watcher.Changed += (_, _) => Interlocked.Exchange(ref changed, 1);
        watcher.Renamed += (_, _) => Interlocked.Exchange(ref changed, 1);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(500, ct).ContinueWith(_ => { });
            if (Interlocked.Exchange(ref changed, 0) == 0) continue;

            var old = _settings;
            _settings = store.Load();
            foreach (var platform in ChatSources.Platforms)
                if (ChatSources.ChannelChanged(platform, old, _settings)) StartChat(platform, ct);
            if (old.Obs.Url != _settings.Obs.Url || old.Obs.Password != _settings.Obs.Password) StartObs(ct);
            hub.Broadcast(SettingsMessage);
            log.LogInformation("Settings reloaded");
        }
    }

    public void SendTestMessage() => hub.Broadcast(ChatMessageEvent(new ChatMessage(
        Guid.NewGuid().ToString("N"), ChatPlatform.Twitch, "budditec", "Budditec", "#FF7F50",
        [
            new TextFragment("Style preview "),
            new EmoteFragment("Kappa", TwitchEmoteParser.NativeEmoteUrl("25")),
            new TextFragment(" animated: "),
            new EmoteFragment("RainTime", new Uri("https://cdn.7tv.app/emote/01FCY771D800007PQ2DF3GDTN6/2x.webp")),
            new TextFragment(" and a long line of text to check wrapping works nicely across lines."),
        ],
        DateTimeOffset.UtcNow)));

    private void StartChat(ChatPlatform platform, CancellationToken stopping)
    {
        if (_chatCts.Remove(platform, out var previous)) previous.Cancel();
        var client = ChatSources.Create(platform, _settings, _http, message => log.LogInformation("{Message}", message));
        if (client is null) return;
        if (client is SimulatedChatSource simulation) Simulation = simulation;

        var cts = _chatCts[platform] = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var ct = cts.Token;
        client.EventRaised += e =>
        {
            if (ct.IsCancellationRequested) return;
            hub.Broadcast(e switch
            {
                ChatMessageReceived received => ChatMessageEvent(received.Message),
                ChatMessageDeleted deleted => new { type = "chat", @event = "deleted", platform = deleted.Platform, messageId = deleted.MessageId },
                ChatUserCleared cleared => new { type = "chat", @event = "userCleared", platform = cleared.Platform, userLogin = cleared.UserLogin },
                ChatCleared cleared => new { type = "chat", @event = "cleared", platform = cleared.Platform },
                ChatConnectionChanged status => new { type = "chat", @event = "connection", platform = status.Platform, connected = status.Connected, detail = status.Detail },
                _ => new { type = "unknown" },
            });
        };
        _ = Task.Run(() => client.RunAsync(ct), ct);
    }

    private void StartObs(CancellationToken stopping)
    {
        _obsCts.Cancel();
        _obsCts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        if (!Uri.TryCreate(_settings.Obs.Url, UriKind.Absolute, out var url)) return;

        var obs = _obs = new ObsMonitor(url, _settings.Obs.Password);
        var ct = _obsCts.Token;
        obs.StatusChanged += status =>
        {
            if (!ct.IsCancellationRequested) hub.Broadcast(new { type = "obs", status });
        };
        obs.LevelsUpdated += levels =>
        {
            if (ct.IsCancellationRequested) return;
            // JSON has no -Infinity; silence is sent as -100 dB.
            hub.Broadcast(new
            {
                type = "levels",
                levels = levels.ToDictionary(l => l.Key, l => double.IsFinite(l.Value.PeakDb) ? Math.Max(-100, l.Value.PeakDb) : -100),
            });
        };
        _ = Task.Run(() => obs.RunAsync(ct), ct);
    }

    private static object ChatMessageEvent(ChatMessage message) => new { type = "chat", @event = "message", message };

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Budditec.Overlay/0.1");
        return http;
    }

    public override void Dispose()
    {
        foreach (var cts in _chatCts.Values) cts.Cancel();
        _obsCts.Cancel();
        _http.Dispose();
        base.Dispose();
    }
}
