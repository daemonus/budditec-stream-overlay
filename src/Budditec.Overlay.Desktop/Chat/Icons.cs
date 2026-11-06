using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Budditec.Overlay.Core.Chat;

namespace Budditec.Overlay.Desktop.Chat;

public static class Icons
{
    // Simplified platform glyphs drawn on a 20x20 grid.
    private static readonly Geometry Twitch = Geometry.Parse(
        "M3,1 L1,5 V17 H5 V20 H8 L11,17 H14 L19,12 V1 Z M17,11 L14,14 H10 L7,17 V14 H4 V3 H17 Z M14,5.5 H12 V10.5 H14 Z M9.5,5.5 H7.5 V10.5 H9.5 Z");
    private static readonly Geometry Kick = Geometry.Parse(
        "M2,2 H7 V8 H9 V6 H11 V4 H13 V2 H18 V7 H16 V9 H14 V11 H16 V13 H18 V18 H13 V16 H11 V14 H9 V12 H7 V18 H2 Z");
    private static readonly Geometry YouTube = Geometry.Parse(
        "M4,4 H16 A3,3 0 0 1 19,7 V13 A3,3 0 0 1 16,16 H4 A3,3 0 0 1 1,13 V7 A3,3 0 0 1 4,4 Z M8,7 V13 L13.5,10 Z");

    public static Control Platform(ChatPlatform platform, double size)
    {
        var (geometry, color) = platform switch
        {
            ChatPlatform.Twitch => (Twitch, "#9146FF"),
            ChatPlatform.Kick => (Kick, "#53FC18"),
            _ => (YouTube, "#FF0000"),
        };
        return new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(Color.Parse(color)),
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            Margin = new Thickness(0, 0, size * 0.3, 0),
        };
    }
}
