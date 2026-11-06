using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Chat.Simulation;
using Budditec.Overlay.Core.Chat.Twitch;
using Budditec.Overlay.Core.Obs;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Core;

/// <summary>
/// Owns the settings and the background services (chat sources, OBS). Every settings change goes through
/// here: Apply from the settings UI, or a reload when the file is edited from outside.
/// </summary>
/// <remarks>Events are raised on background threads.</remarks>
public sealed class OverlayRuntime : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Action<string> _log;
    private readonly Lock _lock = new();
    private readonly Dictionary<ChatPlatform, CancellationTokenSource> _chatCts = new();
    private CancellationTokenSource _obsCts = new();
    private ObsMonitor? _obs;
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private bool _disposed;

    public OverlayRuntime(SettingsStore store, Action<string>? log = null)
    {
        _store = store;
        _log = log ?? (_ => { });
        Settings = store.Load();
        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Budditec.Overlay/0.1");
    }

    /// <summary>The current settings. Don't modify them; Apply a changed SettingsStore.Clone instead.</summary>
    public OverlaySettings Settings { get; private set; }

    public HttpClient Http { get; }

    /// <summary>Set when BUDDITEC_OVERLAY_SIMULATE_CHAT replaces real chat.</summary>
    public SimulatedChatSource? Simulation { get; private set; }

    public ObsStatus ObsStatus => _obs?.Status ?? ObsStatus.Disconnected(Settings.Obs.Url.Length == 0 ? "No OBS URL set" : "Not started");

    public event Action<ChatEvent>? ChatEvent;
    public event Action<ObsStatus>? ObsStatusChanged;
    public event Action<IReadOnlyDictionary<string, AudioLevel>>? LevelsUpdated;

    /// <summary>Raised after settings are applied (channels, style, audio selection, widgets).</summary>
    public event Action<OverlaySettings>? SettingsChanged;

    /// <summary>Connects chat and OBS and starts watching the settings file for outside edits.</summary>
    public void Start()
    {
        lock (_lock)
        {
            foreach (var platform in ChatSources.Platforms) StartChat(platform);
            StartObs();
        }

        var directory = Path.GetDirectoryName(_store.Path)!;
        Directory.CreateDirectory(directory);
        // Editors and our own Save replace the file, so watch renames too; debounce bursts of events.
        var timer = _reloadTimer = new Timer(_ => ReloadFromFile());
        _watcher = new FileSystemWatcher(directory, Path.GetFileName(_store.Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };
        FileSystemEventHandler changed = (_, _) => timer.Change(300, Timeout.Infinite);
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Renamed += (_, _) => timer.Change(300, Timeout.Infinite);
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Saves and applies new settings, or returns why they can't be saved.</summary>
    public string? Apply(OverlaySettings settings)
    {
        settings.Normalize();
        if (settings.Validate() is { } error) return error;
        lock (_lock)
        {
            _store.Save(settings);
            Update(settings);
        }
        return null;
    }

    public void SaveWidgetBounds(string widgetId, WidgetBounds bounds)
    {
        lock (_lock)
        {
            var settings = SettingsStore.Clone(Settings);
            settings.Widgets[widgetId] = bounds;
            _store.Save(settings);
            Settings = settings;
        }
    }

    /// <summary>Sends a sample message with a native emote and a 7TV emote, for previewing the style.</summary>
    public void SendTestMessage() => ChatEvent?.Invoke(new ChatMessageReceived(new ChatMessage(
        Guid.NewGuid().ToString("N"), ChatPlatform.Twitch, "budditec", "Budditec", "#FF7F50",
        [
            new TextFragment("Style preview "),
            new EmoteFragment("Kappa", TwitchEmoteParser.NativeEmoteUrl("25")),
            new TextFragment(" animated: "),
            new EmoteFragment("RainTime", new Uri("https://cdn.7tv.app/emote/01FCY771D800007PQ2DF3GDTN6/2x.webp")),
            new TextFragment(" and a long line of text to check wrapping works nicely across lines."),
        ],
        DateTimeOffset.UtcNow)));

    private void ReloadFromFile()
    {
        lock (_lock)
        {
            if (_disposed) return;
            var loaded = _store.Load();
            // Our own saves land here too; skip them.
            if (SettingsStore.Serialize(loaded) == SettingsStore.Serialize(Settings)) return;
            Update(loaded);
            _log("Settings reloaded from file");
        }
    }

    private void Update(OverlaySettings settings)
    {
        var old = Settings;
        Settings = settings;
        foreach (var platform in ChatSources.Platforms)
            if (ChatSources.ChannelChanged(platform, old, settings)) StartChat(platform);
        if (old.Obs.Url != settings.Obs.Url || old.Obs.Password != settings.Obs.Password) StartObs();
        SettingsChanged?.Invoke(settings);
    }

    private void StartChat(ChatPlatform platform)
    {
        if (_chatCts.Remove(platform, out var previous)) previous.Cancel();
        var client = ChatSources.Create(platform, Settings, Http, _log);
        if (client is null) return;
        if (client is SimulatedChatSource simulation) Simulation = simulation;

        var cts = _chatCts[platform] = new CancellationTokenSource();
        var ct = cts.Token;
        client.EventRaised += e =>
        {
            if (!ct.IsCancellationRequested) ChatEvent?.Invoke(e);
        };
        _ = Task.Run(() => client.RunAsync(ct), ct);
    }

    private void StartObs()
    {
        _obsCts.Cancel();
        _obsCts = new CancellationTokenSource();
        _obs = null;
        if (!Uri.TryCreate(Settings.Obs.Url, UriKind.Absolute, out var url))
        {
            ObsStatusChanged?.Invoke(ObsStatus);
            return;
        }

        var obs = _obs = new ObsMonitor(url, Settings.Obs.Password);
        var ct = _obsCts.Token;
        obs.StatusChanged += status =>
        {
            if (!ct.IsCancellationRequested) ObsStatusChanged?.Invoke(status);
        };
        obs.LevelsUpdated += levels =>
        {
            if (!ct.IsCancellationRequested) LevelsUpdated?.Invoke(levels);
        };
        _ = Task.Run(() => obs.RunAsync(ct), ct);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _watcher?.Dispose();
            _reloadTimer?.Dispose();
            foreach (var cts in _chatCts.Values) cts.Cancel();
            _obsCts.Cancel();
        }
        Http.Dispose();
    }
}
