const BRIDGE_URL = "ws://127.0.0.1:8767/browser-bridge";
const sessions = new Map();
let socket = null;
let reconnectTimer = null;

function connect() {
  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
    return;
  }

  socket = new WebSocket(BRIDGE_URL);

  socket.addEventListener("open", async () => {
    clearReconnect();
    send({
      type: "hello",
      browser: detectBrowserName()
    });
    await flushSessions();
  });

  socket.addEventListener("message", async (event) => {
    try {
      const payload = JSON.parse(event.data);
      if (payload.type === "command" && payload.command) {
        await dispatchCommand(payload.command);
      }
    } catch {
    }
  });

  socket.addEventListener("close", scheduleReconnect);
  socket.addEventListener("error", scheduleReconnect);
}

function clearReconnect() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
}

function scheduleReconnect() {
  if (reconnectTimer) {
    return;
  }
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connect();
  }, 1500);
}

function send(payload) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return false;
  }
  socket.send(JSON.stringify(payload));
  return true;
}

async function flushSessions() {
  const allSessions = [];
  for (const session of sessions.values()) {
    allSessions.push(await enrichActiveState(session));
  }
  if (allSessions.length > 0) {
    send({ type: "media_batch", sessions: allSessions });
  }
}

async function dispatchCommand(command) {
  const sourceId = command.sourceId;
  if (!sourceId || !sessions.has(sourceId)) {
    return;
  }
  const session = sessions.get(sourceId);
  if (!session || typeof session.tabId !== "number") {
    return;
  }
  try {
    await chrome.tabs.sendMessage(session.tabId, {
      type: "nexus_command",
      command
    });
  } catch {
  }
}

async function enrichActiveState(session) {
  try {
    const tab = await chrome.tabs.get(session.tabId);
    return {
      ...session,
      isActiveTab: Boolean(tab.active)
    };
  } catch {
    return session;
  }
}

function detectBrowserName() {
  const ua = navigator.userAgent;
  if (ua.includes("YaBrowser")) return "Yandex Browser";
  if (ua.includes("Edg/")) return "Microsoft Edge";
  if (ua.includes("Chrome/")) return "Google Chrome";
  return "Chromium Browser";
}

function detectBrowserId(browserName) {
  if (browserName === "Yandex Browser") return "yandex";
  if (browserName === "Microsoft Edge") return "edge";
  if (browserName === "Google Chrome") return "chrome";
  return "chromium";
}

chrome.runtime.onInstalled.addListener(() => connect());
chrome.runtime.onStartup?.addListener(() => connect());
connect();

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!sender.tab || !message || typeof message !== "object") {
    return;
  }

  const browserName = detectBrowserName();
  const browserId = detectBrowserId(browserName);

  if (message.type === "media_state") {
    const sourceId = `browser:${browserId}:tab-${sender.tab.id}`;
    const session = {
      ...message.session,
      sourceId,
      browserId,
      browserName,
      tabId: sender.tab.id,
      tabTitle: sender.tab.title || message.session.tabTitle || "",
      pageUrl: sender.tab.url || message.session.pageUrl || "",
      lastUpdatedUtc: new Date().toISOString()
    };

    sessions.set(sourceId, session);
    enrichActiveState(session).then((enriched) => {
      sessions.set(sourceId, enriched);
      send({ type: "media_state", session: enriched });
    });
    sendResponse({ ok: true });
  }

  if (message.type === "media_gone") {
    const sourceId = `browser:${browserId}:tab-${sender.tab.id}`;
    sessions.delete(sourceId);
    send({ type: "media_gone", sourceId });
    sendResponse({ ok: true });
  }
});

chrome.tabs.onRemoved.addListener((tabId) => {
  for (const [sourceId, session] of sessions.entries()) {
    if (session.tabId === tabId) {
      sessions.delete(sourceId);
      send({ type: "media_gone", sourceId });
    }
  }
});

chrome.tabs.onActivated.addListener(async ({ tabId }) => {
  for (const [sourceId, session] of sessions.entries()) {
    const isActiveTab = session.tabId === tabId;
    const updated = { ...session, isActiveTab, lastUpdatedUtc: new Date().toISOString() };
    sessions.set(sourceId, updated);
    send({ type: "media_state", session: updated });
  }
});
