// Tauri shell for the web prototype: transparent, always-on-top, click-through windows showing the pages
// served by the .NET bridge (src/Budditec.Overlay.Bridge), which it starts and stops.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::net::TcpStream;
use std::path::PathBuf;
use std::process::{Child, Command};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use tauri::menu::{Menu, MenuItem};
use tauri::tray::TrayIconBuilder;
use tauri::{AppHandle, Manager, RunEvent, WebviewUrl, WebviewWindowBuilder};
use tauri_plugin_global_shortcut::{Code, GlobalShortcutExt, Modifiers, Shortcut, ShortcutState};
use tauri_plugin_window_state::{AppHandleExt, StateFlags, WindowExt};

const BRIDGE: &str = "127.0.0.1:47820";

#[derive(Default)]
struct AppState {
    bridge: Mutex<Option<Child>>,
    editing: Mutex<bool>,
}

fn main() {
    let edit_shortcut = Shortcut::new(Some(Modifiers::CONTROL | Modifiers::ALT), Code::KeyO);

    tauri::Builder::default()
        .plugin(tauri_plugin_window_state::Builder::default().build())
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(move |app, shortcut, event| {
                    if shortcut == &edit_shortcut && event.state() == ShortcutState::Pressed {
                        toggle_editing(app);
                    }
                })
                .build(),
        )
        .manage(AppState::default())
        .setup(move |app| {
            *app.state::<AppState>().bridge.lock().unwrap() = start_bridge();
            wait_for_bridge(Duration::from_secs(30));

            create_widget(app.handle(), "status", "Status", (40.0, 40.0), (320.0, 220.0))?;
            create_widget(app.handle(), "chat", "Chat", (40.0, 300.0), (420.0, 520.0))?;
            app.global_shortcut().register(edit_shortcut)?;

            let edit = MenuItem::with_id(app, "edit", "Edit layout (Ctrl+Alt+O)", true, None::<&str>)?;
            let quit = MenuItem::with_id(app, "quit", "Exit", true, None::<&str>)?;
            TrayIconBuilder::new()
                .icon(app.default_window_icon().unwrap().clone())
                .tooltip("Budditec Overlay (Tauri)")
                .menu(&Menu::with_items(app, &[&edit, &quit])?)
                .on_menu_event(|app, event| match event.id.as_ref() {
                    "edit" => toggle_editing(app),
                    "quit" => app.exit(0),
                    _ => {}
                })
                .build(app)?;
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("failed to build Tauri app")
        .run(|app, event| {
            if let RunEvent::Exit = event {
                let _ = app.save_window_state(StateFlags::POSITION | StateFlags::SIZE);
                if let Some(mut child) = app.state::<AppState>().bridge.lock().unwrap().take() {
                    let _ = child.kill();
                }
            }
        });
}

fn create_widget(app: &AppHandle, label: &str, title: &str, position: (f64, f64), size: (f64, f64)) -> tauri::Result<()> {
    let url = format!("http://{BRIDGE}/{label}.html").parse().expect("valid url");
    let window = WebviewWindowBuilder::new(app, label, WebviewUrl::External(url))
        .title(title)
        .transparent(true)
        .decorations(false)
        .shadow(false)
        .always_on_top(true)
        .skip_taskbar(true)
        .focused(false)
        .resizable(true)
        .position(position.0, position.1)
        .inner_size(size.0, size.1)
        .build()?;
    let _ = window.restore_state(StateFlags::POSITION | StateFlags::SIZE);
    window.set_ignore_cursor_events(true)?;
    Ok(())
}

/// Edit mode: windows take the mouse so they can be dragged, and the pages show their frame.
fn toggle_editing(app: &AppHandle) {
    let state = app.state::<AppState>();
    let mut editing = state.editing.lock().unwrap();
    *editing = !*editing;
    for window in app.webview_windows().values() {
        let _ = window.set_ignore_cursor_events(!*editing);
        let _ = window.eval(format!("window.setEditing && window.setEditing({})", *editing));
    }
    if !*editing {
        let _ = app.save_window_state(StateFlags::POSITION | StateFlags::SIZE);
    }
}

/// Starts the bridge unless one is already running. BUDDITEC_BRIDGE_EXE overrides the dev build path.
fn start_bridge() -> Option<Child> {
    if TcpStream::connect(BRIDGE).is_ok() {
        return None;
    }
    let project = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../src/Budditec.Overlay.Bridge");
    let exe = std::env::var_os("BUDDITEC_BRIDGE_EXE")
        .map(PathBuf::from)
        .unwrap_or_else(|| project.join("bin/Debug/net10.0/Budditec.Overlay.Bridge.exe"));

    let mut command = Command::new(&exe);
    // The bridge serves wwwroot from its working directory.
    command.current_dir(&project);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    command
        .spawn()
        .map_err(|e| eprintln!("Couldn't start bridge at {}: {e}", exe.display()))
        .ok()
}

/// The pages are loaded from the bridge, so wait for it before creating windows.
fn wait_for_bridge(timeout: Duration) {
    let start = Instant::now();
    while TcpStream::connect(BRIDGE).is_err() && start.elapsed() < timeout {
        std::thread::sleep(Duration::from_millis(200));
    }
}
