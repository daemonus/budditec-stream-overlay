using Avalonia;

namespace Budditec.Overlay.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        // A menu bar (tray) app on macOS: no Dock icon or app menu.
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()
            .With(new MacOSPlatformOptions { ShowInDock = false });
        // Dev switch for comparing GPU (default) and CPU rendering cost.
        if (Environment.GetEnvironmentVariable("BUDDITEC_OVERLAY_SOFTWARE_RENDERING") == "1")
            builder = builder
                .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] });
        return builder;
    }
}
