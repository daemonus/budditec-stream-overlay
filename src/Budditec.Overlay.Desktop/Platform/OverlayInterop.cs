using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Budditec.Overlay.Desktop.Platform;

/// <summary>
/// Native window tweaks Avalonia doesn't expose: click-through and re-asserting always-on-top.
/// Windows only for now; macOS (NSWindow.ignoresMouseEvents) and X11 (input shape) come later.
/// </summary>
internal static partial class OverlayInterop
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExLayered = 0x80000;
    private const long WsExNoActivate = 0x08000000;
    private static readonly nint HwndTopmost = -1;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    /// <summary>
    /// When click-through, mouse input goes to whatever is underneath (e.g. the game).
    /// The window is also kept out of Alt+Tab and never takes focus.
    /// </summary>
    public static void SetClickThrough(Window window, bool clickThrough)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } hwnd) return;

        var style = (long)GetWindowLongPtrW(hwnd, GwlExStyle) | WsExLayered | WsExToolWindow | WsExNoActivate;
        style = clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtrW(hwnd, GwlExStyle, (nint)style);
    }

    /// <summary>Games and other topmost windows can push us down; call periodically.</summary>
    public static void ReassertTopmost(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } hwnd) return;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}
