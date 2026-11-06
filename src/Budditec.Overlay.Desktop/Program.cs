using Avalonia;

namespace Budditec.Overlay.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        // Dev switch for comparing GPU (default) and CPU rendering cost.
        if (Environment.GetEnvironmentVariable("BUDDITEC_OVERLAY_SOFTWARE_RENDERING") == "1")
            builder = builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        return builder;
    }
}
