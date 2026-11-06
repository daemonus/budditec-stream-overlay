# budditec-stream-overlay
An overlay tool for showing common streaming indicators, chat etc

Shows on your own screen, on top of (borderless-windowed) games: combined chat, a recording/live indicator,
and activity for the OBS audio sources in your current scene. Nothing here appears on stream.

Built with .NET and Avalonia (`src/Budditec.Overlay.Desktop`) over a UI-free core
(`src/Budditec.Overlay.Core`). It only reads from OBS (via obs-websocket); it never changes anything there.
Windows first; macOS and Linux later.

An earlier Tauri (web view) prototype was compared and dropped; it's kept at the `tauri-prototype` git tag.

## Use

1. Enable OBS's WebSocket server (Tools > WebSocket Server Settings). Default URL `ws://127.0.0.1:4455`.
2. Run `dotnet run --project src/Budditec.Overlay.Desktop`. Settings open on first run; later, use the tray
   icon > Settings, or pass `-- --settings`.
3. Fill in any of the channels and click Apply. No accounts or API keys are needed: chat is read anonymously,
   so it works for any channel, not just your own.
   - Twitch: channel name, `#name`, or a `twitch.tv/name` URL.
   - Kick: channel name, `@name`, or a `kick.com/name` URL.
   - YouTube: an @handle, a channel id (`UC...`) or a video URL. It picks up the stream when the channel
     goes live.
4. Press **Ctrl+Alt+O** to toggle edit mode: drag widgets to move them, resize from the corner grip, and press
   again to lock them and make them click-through.

Widgets (stream status, chat) can be turned on or off under Widgets in settings. Settings are saved to
`%AppData%/Budditec.Overlay/settings.json`, and hand edits to that file are picked up live.
