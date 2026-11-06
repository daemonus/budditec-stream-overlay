# budditec-stream-overlay
An overlay tool for showing common streaming indicators, chat etc

Shows on your own screen, on top of (borderless-windowed) games: combined chat, a recording/live indicator,
and activity for the OBS audio sources in your current scene. Nothing here appears on stream.

## Prototypes

Two front ends over the same .NET core, to compare:

| | Avalonia (`src/Budditec.Overlay.Desktop`) | Tauri (`tauri/src-tauri` + `src/Budditec.Overlay.Bridge`) |
|---|---|---|
| Rendering | Native (Skia) | HTML/CSS in the OS webview |
| Language | C# only | C# bridge + HTML/CSS/JS + a little Rust |

Both run the same `OverlayRuntime` from Core, which owns `%AppData%/Budditec.Overlay/settings.json`
(validation, saving, restarting chat/OBS when channels change, and reloading when the file is edited from
outside). Settings UIs:

- Avalonia: native settings window (tray icon > Settings, or `--settings`).
- Tauri: tray > Settings opens `settings.html` from the bridge (`cargo run -- --settings` opens it at start).
  The same page works in a browser at `http://127.0.0.1:47820/settings.html` while the bridge is running.

Changes made in either UI reach the other app live if both are running.

## Use

- Enable OBS's WebSocket server (Tools > WebSocket Server Settings). Default URL `ws://127.0.0.1:4455`.
- Open settings and fill in any of the channels, then Apply. No accounts or API keys needed; chat is read
  anonymously, so it works for any channel, not just your own.
  - Twitch: channel name, `#name`, or a `twitch.tv/name` URL.
  - Kick: channel name, `@name`, or a `kick.com/name` URL.
  - YouTube: an @handle, a channel id (`UC...`) or a video URL. It picks up the stream when the channel
    goes live.
- Turn widgets (stream status, chat) on or off under Widgets in settings.
- `dotnet run --project src/Budditec.Overlay.Desktop`
- **Ctrl+Alt+O** toggles edit mode: drag widgets to move them (Avalonia: resize from the corner grip),
  press again to lock them and make them click-through.
