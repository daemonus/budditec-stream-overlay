using Avalonia.Threading;
using Budditec.Overlay.Core;
using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Chat.Simulation;
using Budditec.Overlay.Core.Obs;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Desktop.Services;

/// <summary>
/// Wraps the shared OverlayRuntime (settings, chat sources, OBS) and re-raises its events on the UI thread.
/// </summary>
public sealed class OverlayController : IDisposable
{
    private readonly OverlayRuntime _runtime;

    public OverlayController(SettingsStore store)
    {
        _runtime = new OverlayRuntime(store, message => System.Diagnostics.Trace.WriteLine($"[overlay] {message}"));
        _runtime.ChatEvent += e => Dispatcher.UIThread.Post(() => ChatEvent?.Invoke(e));
        _runtime.ObsStatusChanged += status => Dispatcher.UIThread.Post(() => ObsStatusChanged?.Invoke(status));
        _runtime.LevelsUpdated += levels => Dispatcher.UIThread.Post(() => LevelsUpdated?.Invoke(levels), DispatcherPriority.Render);
        _runtime.SettingsChanged += settings => Dispatcher.UIThread.Post(() => SettingsChanged?.Invoke(settings));
    }

    public OverlaySettings Settings => _runtime.Settings;

    public HttpClient Http => _runtime.Http;

    /// <summary>Set when BUDDITEC_OVERLAY_SIMULATE_CHAT replaces real chat.</summary>
    public SimulatedChatSource? Simulation => _runtime.Simulation;

    public ObsStatus ObsStatus => _runtime.ObsStatus;

    public event Action<ChatEvent>? ChatEvent;
    public event Action<ObsStatus>? ObsStatusChanged;
    public event Action<IReadOnlyDictionary<string, AudioLevel>>? LevelsUpdated;

    /// <summary>Raised after settings are applied, including hand edits to the settings file.</summary>
    public event Action<OverlaySettings>? SettingsChanged;

    public void Start() => _runtime.Start();

    /// <returns>Why the settings couldn't be saved, or null.</returns>
    public string? Apply(OverlaySettings settings) => _runtime.Apply(settings);

    public void SaveWidgetBounds(string widgetId, WidgetBounds bounds) => _runtime.SaveWidgetBounds(widgetId, bounds);

    public void SendTestMessage() => _runtime.SendTestMessage();

    public void Dispose() => _runtime.Dispose();
}
