using Budditec.Overlay.Core;
using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Diagnostics;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Bridge;

/// <summary>
/// Starts the shared OverlayRuntime (the same one the desktop app uses) and forwards its events to the hub.
/// </summary>
public sealed class OverlayServices(BridgeHub hub, OverlayRuntime runtime) : BackgroundService
{
    public object SettingsMessage => SettingsEvent(runtime.Settings);

    public object ObsMessage => new { type = "obs", status = runtime.ObsStatus };

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        runtime.ChatEvent += e => hub.Broadcast(ChatEventMessage(e));
        runtime.ObsStatusChanged += status => hub.Broadcast(new { type = "obs", status });
        // JSON has no -Infinity; silence is sent as -100 dB.
        runtime.LevelsUpdated += levels => hub.Broadcast(new
        {
            type = "levels",
            levels = levels.ToDictionary(l => l.Key, l => double.IsFinite(l.Value.PeakDb) ? Math.Max(-100, l.Value.PeakDb) : -100),
        });
        runtime.SettingsChanged += settings => hub.Broadcast(SettingsEvent(settings));
        runtime.Start();

        return DevOptions.StatsPath is { } statsPath
            ? DevOptions.WriteStatsAsync(statsPath, () => [("messages", runtime.Simulation?.Sent ?? 0)], ct)
            : Task.CompletedTask;
    }

    private static object SettingsEvent(OverlaySettings s) =>
        new { type = "settings", chatStyle = s.ChatStyle, audio = s.Audio, disabledWidgets = s.DisabledWidgets };

    private static object ChatEventMessage(ChatEvent e) => e switch
    {
        ChatMessageReceived received => new { type = "chat", @event = "message", message = received.Message },
        ChatMessageDeleted deleted => new { type = "chat", @event = "deleted", platform = deleted.Platform, messageId = deleted.MessageId },
        ChatUserCleared cleared => new { type = "chat", @event = "userCleared", platform = cleared.Platform, userLogin = cleared.UserLogin },
        ChatCleared cleared => new { type = "chat", @event = "cleared", platform = cleared.Platform },
        ChatConnectionChanged status => new { type = "chat", @event = "connection", platform = status.Platform, connected = status.Connected, detail = status.Detail },
        _ => new { type = "unknown" },
    };
}
