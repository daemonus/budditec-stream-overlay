import { connect, cssColor, withOpacity } from "./bridge.js";

const form = document.getElementById("form");
const message = document.getElementById("message");
const sourcesEl = document.getElementById("sources");
const commonFonts = ["Segoe UI", "Arial", "Bahnschrift", "Calibri", "Cascadia Code", "Comic Sans MS", "Consolas",
  "Georgia", "Impact", "Inter", "Roboto", "Tahoma", "Trebuchet MS", "Verdana"];
let settings = null;
let sourceNames = [];

const getPath = (obj, path) => path.split(".").reduce((o, k) => o?.[k], obj);
function setPath(obj, path, value) {
  const keys = path.split(".");
  const last = keys.pop();
  keys.reduce((o, k) => (o[k] ??= {}), obj)[last] = value;
}

function readInput(el) {
  if (el.type === "checkbox") return el.checked;
  return el.type === "number" || "number" in el.dataset ? Number(el.value) : el.value;
}

function fill() {
  for (const el of form.querySelectorAll("[data-path]")) {
    const value = getPath(settings, el.dataset.path);
    if (el.type === "checkbox") el.checked = !!value;
    else el.value = value ?? "";
  }
  for (const box of document.querySelectorAll("#widgets input")) box.checked = !settings.disabledWidgets.includes(box.value);
  syncSwatches();
  renderSources(sourceNames, settings.audio.selectedSources);
  preview();
}

function collect() {
  const s = structuredClone(settings);
  for (const el of form.querySelectorAll("[data-path]")) setPath(s, el.dataset.path, readInput(el));
  s.chatStyle.shadowOffsetY = s.chatStyle.shadowOffsetX;
  s.disabledWidgets = [...document.querySelectorAll("#widgets input")].filter(b => !b.checked).map(b => b.value);
  // With OBS disconnected there's nothing listed, so keep the saved selection.
  const boxes = [...sourcesEl.querySelectorAll("input")];
  if (boxes.length > 0)
    s.audio.selectedSources = boxes.every(b => b.checked) ? [] : boxes.filter(b => b.checked).map(b => b.value);
  return s;
}

function checkbox(value, label, checked) {
  const row = document.createElement("label");
  row.className = "check";
  const box = document.createElement("input");
  box.type = "checkbox";
  box.value = value;
  box.checked = checked;
  row.append(box, " ", label);
  return row;
}

function renderSources(names, selected) {
  sourcesEl.replaceChildren(...names.map(n => checkbox(n, n, selected.length === 0 || selected.includes(n))));
}

// Each colour has a text box (accepts #AARRGGBB) and a picker (#RRGGBB only).
function syncSwatches() {
  for (const pair of document.querySelectorAll(".color")) {
    const [text, picker] = pair.querySelectorAll("input");
    const hex = cssColor(text.value).slice(0, 7);
    if (/^#[0-9a-f]{6}$/i.test(hex)) picker.value = hex;
  }
}

function preview() {
  const s = collect().chatStyle;
  const el = document.getElementById("preview");
  el.style.fontFamily = `"${s.fontFamily}", sans-serif`;
  el.style.fontSize = `${s.fontSize}px`;
  el.style.fontWeight = s.fontWeight;
  el.style.color = cssColor(s.textColor);
  el.style.textShadow = s.shadowEnabled
    ? `${s.shadowOffsetX}px ${s.shadowOffsetX}px ${s.shadowBlur}px ${withOpacity(s.shadowColor, s.shadowOpacity)}`
    : "none";
}

function show(text, isError = false) {
  message.textContent = text;
  message.className = isError ? "error" : "";
}

for (const pair of document.querySelectorAll(".color")) {
  const [text, picker] = pair.querySelectorAll("input");
  picker.addEventListener("input", () => { text.value = picker.value.toUpperCase(); preview(); });
  text.addEventListener("input", syncSwatches);
}
form.addEventListener("input", () => { if (settings) preview(); });

form.addEventListener("submit", async e => {
  e.preventDefault();
  const response = await fetch("/api/settings", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(collect()),
  });
  const body = await response.json();
  if (!response.ok) return show(body.error ?? "Couldn't save settings", true);
  settings = body.settings;
  fill();
  show("Saved");
});

document.getElementById("test").addEventListener("click", () => fetch("/test-message", { method: "POST" }));

const fonts = document.getElementById("fonts");
fonts.replaceChildren(...commonFonts.map(f => Object.assign(document.createElement("option"), { value: f })));

const initial = await (await fetch("/api/settings")).json();
document.getElementById("widgets").replaceChildren(...initial.widgets.map(w => checkbox(w.id, w.name, true)));
settings = initial.settings;
fill();

connect({
  obs: m => {
    const status = m.status;
    document.getElementById("scene").textContent = status.connected
      ? `Audio sources in "${status.currentScene}"`
      : `OBS not connected: ${status.detail}`;
    const names = status.audioSources.map(s => s.name);
    if (names.join("\n") === sourceNames.join("\n")) return;
    // Keep the user's unsaved ticks when the list changes.
    const boxes = [...sourcesEl.querySelectorAll("input")];
    const selected = boxes.length === 0 || boxes.every(b => b.checked) ? [] : boxes.filter(b => b.checked).map(b => b.value);
    sourceNames = names;
    renderSources(names, boxes.length === 0 ? settings.audio.selectedSources : selected);
  },
});
