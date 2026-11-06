using Avalonia.Controls;
using Avalonia.Platform;

namespace Budditec.Overlay.Desktop.Platform;

/// <summary>
/// Native window tweaks Avalonia doesn't expose: click-through, keeping widgets out of the task switcher, and
/// staying on top. Picks the platform implementation at run time; each file documents its own limits.
/// </summary>
internal static class OverlayInterop
{
    /// <summary>
    /// Applies the overlay window style. When click-through, mouse input goes to whatever is underneath
    /// (e.g. the game). Call once the window is open, and again whenever edit mode changes.
    /// </summary>
    public static void SetClickThrough(Window window, bool clickThrough)
    {
        switch (window.TryGetPlatformHandle())
        {
            case { } h when OperatingSystem.IsWindows():
                WindowsOverlay.SetClickThrough(h.Handle, clickThrough);
                break;
            case IMacOSTopLevelPlatformHandle mac when OperatingSystem.IsMacOS():
                MacOverlay.SetClickThrough(mac.NSWindow, clickThrough);
                break;
            case { HandleDescriptor: "XID" } x11 when OperatingSystem.IsLinux():
                X11Overlay.SetClickThrough(x11.Handle, clickThrough);
                break;
        }
    }

    /// <summary>Games and other topmost windows can push us down; call periodically while not editing.</summary>
    public static void ReassertTopmost(Window window)
    {
        switch (window.TryGetPlatformHandle())
        {
            case { } h when OperatingSystem.IsWindows():
                WindowsOverlay.ReassertTopmost(h.Handle);
                break;
            case IMacOSTopLevelPlatformHandle mac when OperatingSystem.IsMacOS():
                MacOverlay.ReassertTopmost(mac.NSWindow);
                break;
            case { HandleDescriptor: "XID" } x11 when OperatingSystem.IsLinux():
                // Some WMs (e.g. Openbox) frame the window after it opens and miss the first shape change.
                X11Overlay.SetClickThrough(x11.Handle, clickThrough: true);
                break;
        }
    }
}
