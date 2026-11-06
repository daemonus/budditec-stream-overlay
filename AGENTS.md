# Budditec Stream Overlay - Agent Notes

Desktop overlay (not an OBS source) that shows chat and stream status on top of games, for the streamer
only. Windows first; macOS/Linux later. Currently two prototypes share one .NET core so they can be compared.

## Layout

- `src/Budditec.Overlay.Core` - UI-free: chat models (`ChatMessage` with text/emote fragments), `ChatStyle`,
  settings store, Twitch IRC / Kick / YouTube chat clients (created by `ChatSources`) + 7TV/BTTV/FFZ emotes,
  obs-websocket v5 client and `ObsMonitor`. `OverlayRuntime` is what both front ends run: it owns settings
  (`Apply` validates via `OverlaySettings.Validate`, saves, restarts changed services; outside edits to the
  file are reloaded) and raises chat/OBS/settings events on background threads.
- `src/Budditec.Overlay.Desktop` - Avalonia prototype (native rendering). `OverlayController` just marshals
  `OverlayRuntime` events to the UI thread.
- `src/Budditec.Overlay.Bridge` - ASP.NET Core localhost bridge (`127.0.0.1:47820`) serving `wwwroot` pages
  (overlay widgets + `settings.html`), streaming Core events over `/ws`, and `GET`/`PUT /api/settings`;
  used by the Tauri prototype. Requests with a foreign `Origin` are rejected and `AllowedHosts` is
  loopback-only, so other websites can't read chat or change settings.
- `tauri/src-tauri` - Tauri v2 shell: transparent click-through windows that load the bridge pages.
- `tests/Budditec.Overlay.Core.Tests` - xUnit v3 tests for Core.

## Build & test

- Build: `dotnet build`
- Test: `dotnet test --project tests/Budditec.Overlay.Core.Tests` (Microsoft.Testing.Platform, set in
  `global.json`)
- Run Avalonia prototype: `dotnet run --project src/Budditec.Overlay.Desktop` (`-- --settings` opens settings)
- Run bridge alone: `dotnet run --project src/Budditec.Overlay.Bridge`, then open
  `http://127.0.0.1:47820/chat.html` / `status.html`
- Tauri: needs Rust (rustup) + MSVC build tools; `cargo run` in `tauri/src-tauri` after building the bridge.

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
  with Avalonia 12's transparent windows without `SetLayeredWindowAttributes`.
- Settings live in `%AppData%/Budditec.Overlay/settings.json`, shared by both prototypes (each runtime
  watches the file and skips reloads of its own saves). The OBS password is stored there in plain text for now.
  Set `BUDDITEC_OVERLAY_SETTINGS` to point at a temp file when smoke-testing so the real one isn't touched.
- Widgets are keyed by `OverlayWidgets` ids (`status`, `chat`) for both bounds and `DisabledWidgets`. In Tauri a
  disabled widget's page hides its own window (needs `core:window:allow-show/hide`), and the window-state
  plugin only persists position/size so a hidden window isn't restored hidden.
- Colours must be `#RRGGBB` or `#AARRGGBB` (Avalonia order); `cssColor` in `bridge.js` converts for CSS.

## Comments

- Keep comments concise. Don't use XML-doc inline tags (`<c>`, `<see>`, `<paramref>`) inside comments;
  `<summary>`/`<remarks>` blocks are fine.
