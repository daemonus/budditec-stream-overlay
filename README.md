# budditec-stream-overlay
An overlay tool for showing common streaming indicators, chat etc

Shows on your own screen, on top of (borderless-windowed) games: combined chat, a recording/live indicator,
and activity for the OBS audio sources in your current scene. Nothing here appears on stream.

Built with .NET and Avalonia (`src/Budditec.Overlay.Desktop`) over a UI-free core
(`src/Budditec.Overlay.Core`). It only reads from OBS (via obs-websocket); it never changes anything there.
Windows first; macOS and Linux later.

An earlier Tauri (web view) prototype was compared and dropped; it's kept at the `tauri-prototype` git tag.

## Use

- Enable OBS's WebSocket server (Tools > WebSocket Server Settings). Default URL `ws://127.0.0.1:4455`.
- Open settings (tray icon > Settings, or `--settings`; it also opens on first run) and fill in any of the
  channels, then Apply. No accounts or API keys needed; chat is read
  anonymously, so it works for any channel, not just your own.
  - Twitch: channel name, `#name`, or a `twitch.tv/name` URL.
  - Kick: channel name, `@name`, or a `kick.com/name` URL.
  - YouTube: an @handle, a channel id (`UC...`) or a video URL. It picks up the stream when the channel
    goes live.
- Turn widgets (stream status, chat) on or off under Widgets in settings.
- `dotnet run --project src/Budditec.Overlay.Desktop`
- **Ctrl+Alt+O** toggles edit mode: drag widgets to move them, resize from the corner grip, press again to
  lock them and make them click-through.
- Settings are saved to `%AppData%/Budditec.Overlay/settings.json`; hand edits are picked up live.
