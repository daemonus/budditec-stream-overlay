using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Budditec.Overlay.Desktop.Platform;

/// <summary>
/// Sends NSWindow messages through the Objective-C runtime: ignoresMouseEvents for click-through, the
/// status-bar window level to sit above normal and fullscreen windows, and a collection behaviour that
/// shows the window on every Space, including fullscreen game Spaces, and keeps it out of Cmd+Tab and
/// Mission Control.
/// </summary>
/// <remarks>
/// Games that capture the display directly (old-style exclusive fullscreen) still cover it. Must be called
/// on the main thread, which is Avalonia's UI thread on macOS.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static partial class MacOverlay
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    // NSStatusWindowLevel: above normal windows, fullscreen apps and the Dock.
    private const nint StatusWindowLevel = 25;

    // NSWindowCollectionBehavior: CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary.
    private const nuint Behavior = (1 << 0) | (1 << 4) | (1 << 6) | (1 << 8);

    private static readonly nint SetIgnoresMouseEvents = sel_registerName("setIgnoresMouseEvents:");
    private static readonly nint SetLevel = sel_registerName("setLevel:");
    private static readonly nint SetCollectionBehavior = sel_registerName("setCollectionBehavior:");
    private static readonly nint SetHidesOnDeactivate = sel_registerName("setHidesOnDeactivate:");

    public static void SetClickThrough(nint nsWindow, bool clickThrough)
    {
        if (nsWindow == 0) return;
        SendBool(nsWindow, SetIgnoresMouseEvents, clickThrough);
        SendNUInt(nsWindow, SetCollectionBehavior, Behavior);
        SendBool(nsWindow, SetHidesOnDeactivate, false);
        SendNInt(nsWindow, SetLevel, StatusWindowLevel);
    }

    /// <summary>Avalonia puts Topmost windows at the floating level; move them back up.</summary>
    public static void ReassertTopmost(nint nsWindow)
    {
        if (nsWindow != 0) SendNInt(nsWindow, SetLevel, StatusWindowLevel);
    }

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    // objc_msgSend must be called through a prototype matching each method's real signature.
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNInt(nint receiver, nint selector, nint value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNUInt(nint receiver, nint selector, nuint value);
}
