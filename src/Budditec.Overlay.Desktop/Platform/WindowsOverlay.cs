using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Budditec.Overlay.Desktop.Platform;

/// <summary>
/// Click-through via WS_EX_LAYERED | WS_EX_TRANSPARENT, hidden from Alt+Tab (WS_EX_TOOLWINDOW), never
/// focused (WS_EX_NOACTIVATE). Can't draw over exclusive-fullscreen games; borderless windowed works.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsOverlay
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExLayered = 0x80000;
    private const long WsExNoActivate = 0x08000000;
    private static readonly nint HwndTopmost = -1;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    public static void SetClickThrough(nint hwnd, bool clickThrough)
    {
        var style = (long)GetWindowLongPtrW(hwnd, GwlExStyle) | WsExLayered | WsExToolWindow | WsExNoActivate;
        style = clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtrW(hwnd, GwlExStyle, (nint)style);
    }

    public static void ReassertTopmost(nint hwnd) =>
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}
