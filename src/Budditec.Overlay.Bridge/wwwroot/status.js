import { connect, setupEditing, widgetToggle } from "./bridge.js";

const toggle = widgetToggle("status");
const micPath = "M12,14 A3,3 0 0 0 15,11 V5 A3,3 0 0 0 9,5 V11 A3,3 0 0 0 12,14 Z M17,11 A5,5 0 0 1 7,11 H5 A7,7 0 0 0 11,17.9 V21 H13 V17.9 A7,7 0 0 0 19,11 Z";
const mics = document.getElementById("mics");
let audio = { selectedSources: [], speakingThresholdDb: -45, holdMilliseconds: 250 };
let status = null;
const lastActive = new Map();

function render() {
  if (!status) return;
  const recording = status.record === "Recording", paused = status.record === "Paused";
  document.getElementById("rec-dot").className = "dot" + (recording ? " recording" : paused ? " paused" : "");
  document.getElementById("rec-label").textContent = paused ? "PAUSED" : "REC";
  document.getElementById("live").classList.toggle("live", status.streaming);
  document.getElementById("detail").textContent = status.connected ? status.currentScene ?? "" : `OBS: ${status.detail}`;

  const visible = status.audioSources.filter(s => audio.selectedSources.length === 0 || audio.selectedSources.includes(s.name));
  mics.replaceChildren(...visible.map(source => {
    const row = document.createElement("div");
    row.className = "mic" + (source.muted ? " muted" : "");
    row.dataset.name = source.name;
    row.innerHTML = `<svg viewBox="0 0 24 24"><path d="${micPath}"/><line class="slash" x1="2" y1="2" x2="22" y2="22"/></svg>
      <span class="label"></span><div class="meter"><div style="width:0"></div></div>`;
    row.querySelector(".label").textContent = source.name;
    return row;
  }));
}

function updateLevels(levels) {
  const now = performance.now();
  for (const row of mics.children) {
    const db = levels[row.dataset.name] ?? -100;
    if (db >= audio.speakingThresholdDb) lastActive.set(row.dataset.name, now);
    const active = !row.classList.contains("muted") && now - (lastActive.get(row.dataset.name) ?? -1e9) <= audio.holdMilliseconds;
    row.classList.toggle("active", active);
    row.querySelector(".meter > div").style.width = `${Math.round(Math.min(Math.max((db + 60) / 60, 0), 1) * 120)}px`;
  }
}

connect({
  ...setupEditing("Status"),
  settings: m => { toggle(m); audio = m.audio; render(); },
  obs: m => { status = m.status; render(); },
  levels: m => updateLevels(m.levels),
});
