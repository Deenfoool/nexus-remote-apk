const statusDot = document.getElementById("statusDot");
const statusTitle = document.getElementById("statusTitle");
const statusSubtitle = document.getElementById("statusSubtitle");
const browserName = document.getElementById("browserName");
const transportMode = document.getElementById("transportMode");
const sessionCount = document.getElementById("sessionCount");
const activeSites = document.getElementById("activeSites");
const version = document.getElementById("version");
const reloadButton = document.getElementById("reloadButton");
const refreshButton = document.getElementById("refreshButton");

version.textContent = `Browser Bridge v${chrome.runtime.getManifest().version}`;

function askBackground(message) {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage(message, (response) => {
      resolve(response || {});
    });
  });
}

function renderState(state) {
  const connected = Boolean(state.isConnected);
  statusDot.classList.toggle("is-ok", connected);
  statusTitle.textContent = connected ? "Bridge подключён" : "Bridge ждёт Nexus Remote PC";
  statusSubtitle.textContent = connected
    ? "Медиа-вкладки готовы к управлению"
    : "Запустите Nexus Remote PC, чтобы расширение начало передавать данные";

  browserName.textContent = state.browserName || "-";
  transportMode.textContent = state.transport || "-";
  sessionCount.textContent = String(state.sessionCount || 0);
  activeSites.textContent = Array.isArray(state.activeSites) && state.activeSites.length > 0
    ? state.activeSites.join(", ")
    : "-";
}

async function refresh() {
  const state = await askBackground({ type: "get_bridge_state" });
  renderState(state);
}

reloadButton.addEventListener("click", async () => {
  await askBackground({ type: "force_reconnect" });
  setTimeout(() => {
    void refresh();
  }, 250);
});

refreshButton.addEventListener("click", () => {
  void refresh();
});

void refresh();
