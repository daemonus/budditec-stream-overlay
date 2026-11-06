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

Both read `%AppData%/Budditec.Overlay/settings.json`. The Avalonia app has a settings window (tray icon or
`--settings`); the Tauri prototype picks up changes to the same file live.

## Use

- Enable OBS's WebSocket server (Tools > WebSocket Server Settings). Default URL `ws://127.0.0.1:4455`.
- Set any of the Twitch, Kick and YouTube channels in settings (no accounts or API keys needed). YouTube takes
  an @handle, a channel id or a video URL, and picks up the stream when the channel goes live.
- `dotnet run --project src/Budditec.Overlay.Desktop`
- **Ctrl+Alt+O** toggles edit mode: drag widgets to move them (Avalonia: resize from the corner grip),
  press again to lock them and make them click-through.
