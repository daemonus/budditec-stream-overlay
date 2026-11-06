import { connect, setupEditing, cssColor, withOpacity, widgetToggle } from "./bridge.js";

const chat = document.getElementById("chat");
const toggle = widgetToggle("chat");
let style = null;

const icons = {
  Twitch: ["#9146FF", "M3,1 L1,5 V17 H5 V20 H8 L11,17 H14 L19,12 V1 Z M17,11 L14,14 H10 L7,17 V14 H4 V3 H17 Z M14,5.5 H12 V10.5 H14 Z M9.5,5.5 H7.5 V10.5 H9.5 Z"],
  Kick: ["#53FC18", "M2,2 H7 V8 H9 V6 H11 V4 H13 V2 H18 V7 H16 V9 H14 V11 H16 V13 H18 V18 H13 V16 H11 V14 H9 V12 H7 V18 H2 Z"],
  YouTube: ["#FF0000", "M4,4 H16 A3,3 0 0 1 19,7 V13 A3,3 0 0 1 16,16 H4 A3,3 0 0 1 1,13 V7 A3,3 0 0 1 4,4 Z M8,7 V13 L13.5,10 Z"],
};

function applyStyle(s) {
  style = s;
  const root = document.documentElement.style;
  root.setProperty("--font-family", `"${s.fontFamily}", sans-serif`);
  root.setProperty("--font-size", `${s.fontSize}px`);
  root.setProperty("--font-weight", s.fontWeight);
  root.setProperty("--text-color", cssColor(s.textColor));
  root.setProperty("--emote-height", `${s.fontSize * s.emoteScale}px`);
  root.setProperty("--shadow", s.shadowEnabled
    ? `${s.shadowOffsetX}px ${s.shadowOffsetY}px ${s.shadowBlur}px ${withOpacity(s.shadowColor, s.shadowOpacity)}`
    : "none");
  for (const el of chat.children) el.style.setProperty("--name-color", nameColor(el.message));
  for (const icon of chat.querySelectorAll(".platform")) icon.style.display = s.showPlatformIcon ? "" : "none";
  trim();
}

function nameColor(m) {
  const platform = style?.usePlatformNameColors ? style.platformColors[m.platform] : null;
  return cssColor(platform ?? m.userColor ?? style?.textColor ?? "#FFFFFF");
}

function addMessage(m) {
  const el = document.createElement("div");
  el.className = "message" + (m.isAction ? " action" : "");
  el.message = m;
  el.dataset.id = m.id;
  el.style.setProperty("--name-color", nameColor(m));

  const [color, path] = icons[m.platform];
  const icon = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  icon.setAttribute("class", "platform");
  icon.setAttribute("viewBox", "0 0 20 20");
  icon.innerHTML = `<path fill="${color}" fill-rule="evenodd" d="${path}"/>`;
  if (!style?.showPlatformIcon) icon.style.display = "none";
  el.appendChild(icon);

  const name = document.createElement("span");
  name.className = "name";
  name.textContent = m.userDisplayName + (m.isAction ? " " : ": ");
  el.appendChild(name);

  const text = document.createElement("span");
  text.className = "text";
  for (const f of m.fragments) {
    if (f.kind === "emote") {
      const img = document.createElement("img");
      img.className = "emote";
      img.src = f.url;
      img.alt = img.title = f.code;
      text.appendChild(img);
    } else {
      text.appendChild(document.createTextNode(f.text));
    }
  }
  el.appendChild(text);
  chat.appendChild(el);
  trim();
}

function trim() {
  const max = Math.max(1, style?.maxMessages ?? 50);
  while (chat.children.length > max) chat.firstElementChild.remove();
}

function removeWhere(predicate) {
  for (const el of [...chat.children]) if (predicate(el.message)) el.remove();
}

setInterval(() => {
  if (!style?.fadeAfterSeconds) return;
  const cutoff = Date.now() - style.fadeAfterSeconds * 1000;
  for (const el of [...chat.children]) {
    if (new Date(el.message.timestamp).getTime() > cutoff) continue;
    if (el.classList.contains("faded")) el.remove();
    else el.classList.add("faded");
  }
}, 1000);

connect({
  ...setupEditing("Chat"),
  settings: m => { toggle(m); applyStyle(m.chatStyle); },
  chat: m => {
    switch (m.event) {
      case "message": addMessage(m.message); break;
      case "deleted": removeWhere(x => x.id === m.messageId); break;
      case "userCleared": removeWhere(x => x.platform === m.platform && x.userLogin === m.userLogin); break;
      case "cleared": removeWhere(x => x.platform === m.platform); break;
    }
  },
});
