using Budditec.Overlay.Core.Settings;
using Budditec.Overlay.Desktop.Services;

namespace Budditec.Overlay.Desktop.Widgets;

/// <summary>Recording/live state and activity for the selected OBS audio sources.</summary>
public sealed class StatusWindow : OverlayWindow
{
    public const string Id = OverlayWidgets.Status;

    public StatusWindow(OverlayController controller)
        : base(Id, "Status", new WidgetBounds(40, 40, 320, 220), controller.Settings.Widgets.GetValueOrDefault(Id))
    {
        var vm = new StatusViewModel();
        vm.UpdateSettings(controller.Settings.Audio);
        vm.UpdateStatus(controller.ObsStatus);
        Body = new StatusView { DataContext = vm };

        controller.ObsStatusChanged += vm.UpdateStatus;
        controller.LevelsUpdated += vm.UpdateLevels;
        controller.SettingsChanged += settings => vm.UpdateSettings(settings.Audio);
    }
}
