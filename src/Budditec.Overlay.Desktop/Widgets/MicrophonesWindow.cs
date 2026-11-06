using Budditec.Overlay.Core.Settings;
using Budditec.Overlay.Desktop.Services;

namespace Budditec.Overlay.Desktop.Widgets;

/// <summary>Mute state and activity for the OBS audio sources in the current scene that aren't hidden in settings.</summary>
public sealed class MicrophonesWindow : OverlayWindow
{
    public const string Id = OverlayWidgets.Microphones;

    public MicrophonesWindow(OverlayController controller)
        : base(Id, "Microphones", new WidgetBounds(40, 90, 260, 170), controller.Settings.Widgets.GetValueOrDefault(Id))
    {
        var vm = new MicrophonesViewModel();
        vm.UpdateSettings(controller.Settings.Audio);
        vm.UpdateStatus(controller.ObsStatus);
        Body = new MicrophonesView { DataContext = vm };

        controller.ObsStatusChanged += vm.UpdateStatus;
        controller.LevelsUpdated += vm.UpdateLevels;
        controller.SettingsChanged += settings => vm.UpdateSettings(settings.Audio);
    }
}
