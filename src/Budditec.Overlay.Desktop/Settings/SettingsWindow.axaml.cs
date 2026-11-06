using Avalonia.Controls;

namespace Budditec.Overlay.Desktop.Settings;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel vm) : this()
    {
        DataContext = vm;
        Closed += (_, _) => vm.Detach();
    }
}
