# budditec-stream-overlay
An overlay tool for showing common streaming indicators, chat etc

Shows on your own screen, on top of (borderless-windowed) games: combined chat, a recording/live indicator,
and activity for the OBS audio sources in your current scene. Nothing here appears on stream.

Built with .NET and Avalonia (`src/Budditec.Overlay.Desktop`) over a UI-free core
(`src/Budditec.Overlay.Core`). It only reads from OBS (via obs-websocket); it never changes anything there.
Runs on Windows, macOS and Linux (X11/XWayland); see [Platform support](#platform-support) for the limits.

An earlier Tauri (web view) prototype was compared and dropped; it's kept at the `tauri-prototype` git tag.

## Use

1. Enable OBS's WebSocket server (Tools > WebSocket Server Settings). Default URL `ws://127.0.0.1:4455`.
2. Run `dotnet run --project src/Budditec.Overlay.Desktop`. Settings open on first run; later, use the tray
   (menu bar on macOS) icon > Settings, or pass `-- --settings`.
3. Fill in any of the channels and click Apply. No accounts or API keys are needed: chat is read anonymously,
   so it works for any channel, not just your own.
   - Twitch: channel name, `#name`, or a `twitch.tv/name` URL.
   - Kick: channel name, `@name`, or a `kick.com/name` URL.
   - YouTube: an @handle, a channel id (`UC...`) or a video URL. It picks up the stream when the channel
     goes live.
4. Press **Ctrl+Alt+O** (**Ctrl+Option+O** on macOS) or use the tray menu's Edit layout to toggle edit mode:
   drag widgets to move them, resize from the corner grip, and toggle again to lock them and make them
   click-through.

To build a standalone copy (no .NET install needed), run
`dotnet publish src/Budditec.Overlay.Desktop -c Release -r win-x64 --self-contained -o publish` and start
`publish/Budditec.Overlay.Desktop.exe`. Use `-r osx-arm64` (Apple Silicon), `osx-x64` or `linux-x64` for the
other platforms; these can be built from any OS.

Widgets (stream status, chat) can be turned on or off under Widgets in settings. Settings are saved to
`settings.json` in `%AppData%\Budditec.Overlay` (Windows), `~/Library/Application Support/Budditec.Overlay`
(macOS) or `~/.config/Budditec.Overlay` (Linux), and hand edits to that file are picked up live.

## Platform support

Games must run **borderless windowed** on every platform; exclusive fullscreen covers any overlay.

**Windows** is the main platform and is tested.

**macOS** builds, but hasn't been run on a Mac yet.
- Widgets show on every Space, including fullscreen game Spaces, above the Dock, and are hidden from
  Cmd+Tab and Mission Control. The app lives in the menu bar with no Dock icon.
- The global hotkey needs permission under System Settings > Privacy & Security > Accessibility (and Input
  Monitoring). Without it, use the menu bar icon's Edit layout.
- Games that take over the display directly (old-style exclusive fullscreen) still cover the widgets.
- Distributing it needs an `.app` bundle, signed and notarized with an Apple Developer account, or
  Gatekeeper blocks it. Running the published folder locally works without that.

**Linux** works on X11, and under Wayland through XWayland (Avalonia uses X11). Click-through, edit mode and the
hotkey are covered by an automated test under Xvfb with no window manager, Openbox and Xfwm4
(`tests/x11-smoke`); it hasn't been tried on a real KDE or GNOME desktop yet.
- Transparent widgets need a compositor (every mainstream desktop has one); without one they're black boxes.
- The tray icon needs a StatusNotifier host: KDE has one; stock GNOME needs the AppIndicator extension
  (Ubuntu ships it). Without a tray, open settings with `--settings` and quit by ending the process.
- Most window managers put a focused fullscreen window above "always on top" windows, which is another reason
  to play borderless windowed.
- On Wayland the hotkey only works while an X11/XWayland window has focus, and the widgets may not stay above
  native Wayland fullscreen windows. Steam Deck Game Mode (gamescope) doesn't allow overlays at all.
- Needs `libX11` and `libXext` (installed with any X11 desktop).
