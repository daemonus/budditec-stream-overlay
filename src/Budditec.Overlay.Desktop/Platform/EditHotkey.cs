using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;

namespace Budditec.Overlay.Desktop.Platform;

/// <summary>Global Ctrl+Alt+O to toggle layout editing, even while a game has focus.</summary>
internal sealed class EditHotkey : IDisposable
{
    public const string Description = "Ctrl+Alt+O";

    private readonly TaskPoolGlobalHook _hook = new();

    public EditHotkey(Action pressed)
    {
        _hook.KeyPressed += (_, e) =>
        {
            if (e.Data.KeyCode == KeyCode.VcO && e.RawEvent.Mask.HasCtrl() && e.RawEvent.Mask.HasAlt())
                Dispatcher.UIThread.Post(pressed);
        };
        _ = _hook.RunAsync();
    }

    public void Dispose() => _hook.Dispose();
}
