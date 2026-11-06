using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;

namespace Budditec.Overlay.Desktop.Platform;

/// <summary>Global Ctrl+Alt+O (Control+Option+O on macOS) to toggle layout editing, even while a game has focus.</summary>
/// <remarks>
/// macOS only delivers global keys once the app is allowed under Privacy &amp; Security > Accessibility (and
/// Input Monitoring). On Linux it works under X11/XWayland, but not while a native Wayland window has focus.
/// The tray menu's Edit layout item works either way.
/// </remarks>
internal sealed class EditHotkey : IDisposable
{
    public static string Description { get; } = OperatingSystem.IsMacOS() ? "Ctrl+Option+O" : "Ctrl+Alt+O";

    private readonly TaskPoolGlobalHook _hook = new();

    public EditHotkey(Action pressed)
    {
        _hook.KeyPressed += (_, e) =>
        {
            if (e.Data.KeyCode == KeyCode.VcO && e.RawEvent.Mask.HasCtrl() && e.RawEvent.Mask.HasAlt())
                Dispatcher.UIThread.Post(pressed);
        };
        _hook.RunAsync().ContinueWith(
            t => System.Diagnostics.Trace.WriteLine($"[overlay] Global hotkey unavailable, use the tray menu: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Dispose() => _hook.Dispose();
}
