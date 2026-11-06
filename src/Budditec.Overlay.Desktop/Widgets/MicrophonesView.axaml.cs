using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Budditec.Overlay.Desktop.Widgets;

public partial class MicrophonesView : UserControl
{
    public static readonly IValueConverter LevelToWidth =
        new FuncValueConverter<double, double>(level => Math.Round(level * 120));

    public MicrophonesView()
    {
        InitializeComponent();
    }
}
