// Connects to the bridge WebSocket and dispatches messages by type, reconnecting forever.
export function connect(handlers) {
  const url = `${location.protocol === "https:" ? "wss" : "ws"}://${location.host}/ws`;
  let delay = 1000;

  function open() {
    const ws = new WebSocket(url);
    ws.onopen = () => { delay = 1000; };
    ws.onmessage = e => {
      const message = JSON.parse(e.data);
      handlers[message.type]?.(message);
    };
    ws.onclose = () => {
      setTimeout(open, delay);
      delay = Math.min(delay * 2, 15000);
    };
  }
  open();
}

// Edit mode is driven by the shell (Tauri) via window.setEditing, or by the bridge's /edit endpoint.
export function setupEditing(title) {
  const frame = document.createElement("div");
  frame.className = "edit-frame";
  frame.setAttribute("data-tauri-drag-region", "");
  frame.innerHTML = `<span class="edit-label" data-tauri-drag-region>${title} (drag to move, Ctrl+Alt+O to finish)</span>`;
  const tauriWindow = window.__TAURI__?.window.getCurrentWindow();
  if (tauriWindow) {
    const grip = document.createElement("div");
    grip.className = "edit-grip";
    grip.addEventListener("mousedown", e => {
      e.preventDefault();
      tauriWindow.startResizeDragging("SouthEast");
    });
    frame.appendChild(grip);
  }
  document.body.appendChild(frame);
  window.setEditing = on => document.body.classList.toggle("editing", on);
  return { edit: m => window.setEditing(m.on) };
}

// Avalonia-style #AARRGGBB to CSS #RRGGBBAA.
export function cssColor(value) {
  return /^#[0-9a-f]{8}$/i.test(value) ? `#${value.slice(3)}${value.slice(1, 3)}` : value;
}

export function withOpacity(value, opacity) {
  const hex = cssColor(value).replace("#", "");
  const [r, g, b] = [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16));
  const a = (hex.length === 8 ? parseInt(hex.slice(6, 8), 16) / 255 : 1) * opacity;
  return `rgba(${r}, ${g}, ${b}, ${a})`;
}
