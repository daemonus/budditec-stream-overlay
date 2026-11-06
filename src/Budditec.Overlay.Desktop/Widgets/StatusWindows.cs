using Budditec.Overlay.Core.Settings;
using Budditec.Overlay.Desktop.Services;

namespace Budditec.Overlay.Desktop.Widgets;

/// <summary>REC, blinking while recording and yellow while paused.</summary>
public sealed class RecordingWindow : OverlayWindow
{
    public const string Id = OverlayWidgets.Recording;

    public RecordingWindow(OverlayController controller)
        : base(Id, "Recording", new WidgetBounds(40, 40, 110, 36), controller.Settings.Widgets.GetValueOrDefault(Id))
    {
        Body = new RecordingView { DataContext = StatusViewModel.Create(controller) };
    }
}

/// <summary>LIVE, red while streaming, plus the current scene (or why OBS isn't connected).</summary>
public sealed class LiveWindow : OverlayWindow
{
    public const string Id = OverlayWidgets.Live;

    public LiveWindow(OverlayController controller)
        : base(Id, "Live", new WidgetBounds(150, 40, 260, 36), controller.Settings.Widgets.GetValueOrDefault(Id))
    {
        Body = new LiveView { DataContext = StatusViewModel.Create(controller) };
    }
}
