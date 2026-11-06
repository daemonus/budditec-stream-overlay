using CommunityToolkit.Mvvm.ComponentModel;
using Budditec.Overlay.Core.Obs;
using Budditec.Overlay.Desktop.Services;

namespace Budditec.Overlay.Desktop.Widgets;

/// <summary>OBS recording/streaming state for the recording and live widgets.</summary>
public sealed partial class StatusViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ObsDetail { get; set; } = "Connecting to OBS";

    [ObservableProperty]
    public partial bool ShowObsDetail { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecording), nameof(IsPaused), nameof(RecordLabel))]
    public partial RecordState Record { get; set; }

    [ObservableProperty]
    public partial bool Streaming { get; set; }

    public bool IsRecording => Record == RecordState.Recording;
    public bool IsPaused => Record == RecordState.Paused;
    public string RecordLabel => Record == RecordState.Paused ? "PAUSED" : "REC";

    public static StatusViewModel Create(OverlayController controller)
    {
        var vm = new StatusViewModel { ShowObsDetail = controller.Settings.Live.ShowObsDetail };
        vm.UpdateStatus(controller.ObsStatus);
        controller.ObsStatusChanged += vm.UpdateStatus;
        controller.SettingsChanged += settings => vm.ShowObsDetail = settings.Live.ShowObsDetail;
        return vm;
    }

    public void UpdateStatus(ObsStatus status)
    {
        ObsDetail = status.Connected ? status.CurrentScene ?? "" : $"OBS: {status.Detail}";
        Record = status.Record;
        Streaming = status.Streaming;
    }
}
