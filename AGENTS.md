# Budditec Stream Overlay - Agent Notes

Desktop overlay (not an OBS source) that shows chat and stream status on top of games, for the streamer
only. It only reads from OBS. Windows first; macOS/Linux later. Built with Avalonia; a Tauri prototype was
compared and dropped (kept at the `tauri-prototype` git tag).

## Layout

- `src/Budditec.Overlay.Core` - UI-free: chat models (`ChatMessage` with text/emote fragments), `ChatStyle`,
  settings store, Twitch IRC / Kick / YouTube chat clients (created by `ChatSources`) + 7TV/BTTV/FFZ emotes,
  obs-websocket v5 client and `ObsMonitor`. `OverlayRuntime` owns settings (`Apply` validates via
  `OverlaySettings.Validate`, saves, restarts changed services; outside edits to the file are reloaded) and
  raises chat/OBS/settings events on background threads.
- `src/Budditec.Overlay.Desktop` - the Avalonia app. `OverlayController` just marshals `OverlayRuntime`
  events to the UI thread.
- `tests/Budditec.Overlay.Core.Tests` - xUnit v3 tests for Core.

## Build & test

- Build: `dotnet build`
- Test: `dotnet test --project tests/Budditec.Overlay.Core.Tests` (Microsoft.Testing.Platform, set in
  `global.json`)
- Run: `dotnet run --project src/Budditec.Overlay.Desktop` (`-- --settings` opens settings)
- Publish: `dotnet publish src/Budditec.Overlay.Desktop -c Release -r win-x64 --self-contained -o publish`
  (about 200 MB, needs no .NET install). Smoke-test the exe with `BUDDITEC_OVERLAY_SETTINGS` (temp file),
  `BUDDITEC_OVERLAY_SIMULATE_CHAT=5` and `BUDDITEC_OVERLAY_STATS`; the simulator waits ~20 s before sending.

App icon: `assets/icon/icon-512.png` is the master (made with IconKitchen; `icon-1024-square.png` and
`icon-monochrome.png` are spares). `powershell -ExecutionPolicy Bypass -File assets/icon/make-ico.ps1`
rebuilds `src/Budditec.Overlay.Desktop/Assets/icon.ico`, which is the exe, tray and settings-window icon.

Package versions are central in `Directory.Packages.props`. Prefer versions published at least 7 days ago.

## Things that are easy to get wrong

- Twitch emote ids are opaque strings (`emotesv2_...`), and emote positions count Unicode code points,
  not UTF-16 chars (see `TwitchEmoteParser`).
- Kick chat is unofficial: chatroom id from `kick.com/api/v2/channels/{slug}`, then Kick's Centrifugo server
  (Pusher stopped working in October 2026). The WebSocket needs `Origin: https://kick.com`; guest tokens last
  45 minutes and are refreshed in place. 7TV's Kick lookup takes the Kick user id, not the channel id.
- YouTube chat polls the web client's `youtubei/v1/live_chat/get_live_chat` (no key; the Data API quota is too
  small). Use the "Live chat" continuation, not the default "Top chat", and skip the first (history) batch.
- obs-websocket: inputs without audio reject `GetInputMute`; `ObsMonitor` uses that as the "has audio"
  check. `InputVolumeMeters` is opt-in and fires ~every 50 ms.
- Click-through on Windows is `WS_EX_LAYERED | WS_EX_TRANSPARENT` (`Platform/OverlayInterop.cs`); it works
  with Avalonia 12's transparent windows without `SetLayeredWindowAttributes`. macOS/Linux versions of
  click-through and topmost aren't written yet.
- Settings live in `%AppData%/Budditec.Overlay/settings.json`; the runtime watches the file and skips reloads
  of its own saves. The OBS password is stored there in plain text for now. Set `BUDDITEC_OVERLAY_SETTINGS`
  to point at a temp file when smoke-testing so the real one isn't touched.
- Widgets are keyed by `OverlayWidgets` ids (`status`, `chat`) for both bounds and `DisabledWidgets`. Don't
  save bounds for hidden widgets (they may never have been laid out).
- Colours must be `#RRGGBB` or `#AARRGGBB`.

## Comments

- Keep comments concise. Don't use XML-doc inline tags (`<c>`, `<see>`, `<paramref>`) inside comments;
  `<summary>`/`<remarks>` blocks are fine.
