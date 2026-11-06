using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Budditec.Overlay.Desktop.Platform;

/// <summary>
/// Click-through on X11 (and XWayland) by giving the window an empty input shape (XShape extension), so the
/// X server delivers clicks to whatever is underneath. Avalonia's Topmost and ShowInTaskbar already set
/// _NET_WM_STATE_ABOVE and _NET_WM_STATE_SKIP_TASKBAR.
/// </summary>
/// <remarks>
/// Window managers draw a frame around the window; KWin, Mutter (GNOME) and most compositing WMs copy the
/// client's input shape to it, but a WM that doesn't will swallow clicks. Most WMs stack a focused
/// fullscreen window above "always on top" ones, so games must run borderless windowed. Native Wayland
/// windows can't be placed or kept on top at all; this only works because Avalonia runs through XWayland.
/// </remarks>
[SupportedOSPlatform("linux")]
internal static partial class X11Overlay
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";
    private const int ShapeInput = 2, ShapeSet = 0, Unsorted = 0;

    // Our own connection: the shape lives on the server, so it doesn't have to be Avalonia's.
    private static nint _display;
    private static bool _unavailable;

    public static void SetClickThrough(nint window, bool clickThrough)
    {
        if (_unavailable || window == 0) return;
        try
        {
            if (_display == 0 && (_display = XOpenDisplay(0)) == 0)
            {
                Fail("XOpenDisplay failed");
                return;
            }
            if (clickThrough) XShapeCombineRectangles(_display, (nuint)window, ShapeInput, 0, 0, 0, 0, ShapeSet, Unsorted);
            else XShapeCombineMask(_display, (nuint)window, ShapeInput, 0, 0, 0, ShapeSet); // no mask = default shape
            XFlush(_display);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Fail($"{ex.Message} (install libxext6 / libXext)");
        }
    }

    private static void Fail(string reason)
    {
        _unavailable = true;
        System.Diagnostics.Trace.WriteLine($"[overlay] X11 click-through unavailable: {reason}");
    }

    [LibraryImport(LibX11)]
    private static partial nint XOpenDisplay(nint name);

    [LibraryImport(LibX11)]
    private static partial int XFlush(nint display);

    [LibraryImport(LibXext)]
    private static partial void XShapeCombineRectangles(nint display, nuint window, int destKind, int xOff, int yOff,
        nint rectangles, int count, int op, int ordering);

    [LibraryImport(LibXext)]
    private static partial void XShapeCombineMask(nint display, nuint window, int destKind, int xOff, int yOff,
        nuint pixmap, int op);
}
