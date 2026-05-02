const BRIDGE_URL = "ws://127.0.0.1:8767/browser-bridge";
const BRIDGE_HTTP_PUSH = "http://127.0.0.1:8767/browser-bridge/push";
const BRIDGE_HTTP_COMMANDS = "http://127.0.0.1:8767/browser-bridge/commands";
const sessions = new Map();
let socket = null;
let reconnectTimer = null;
let pollTimer = null;
let lastCommandId = 0;
const FIREFOX_MODE = typeof browser !== "undefined" && !globalThis.chrome?.runtime?.id;
let bridgeConnected = false;

function setBridgeConnected(value) {
  bridgeConnected = value;
}

function connect() {
  if (FIREFOX_MODE) {
    startFirefoxPolling();
    send({
      type: "hello",
      browser: detectBrowserName()
    });
    flushSessions();
    return;
  }

  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
    return;
  }

  socket = new WebSocket(BRIDGE_URL);

  socket.addEventListener("open", async () => {
    clearReconnect();
    setBridgeConnected(true);
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

  socket.addEventListener("close", () => {
    setBridgeConnected(false);
    scheduleReconnect();
  });
  socket.addEventListener("error", () => {
    setBridgeConnected(false);
    scheduleReconnect();
  });
}

function clearReconnect() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
}

function scheduleReconnect() {
  if (FIREFOX_MODE) {
    startFirefoxPolling();
    return;
  }
  if (reconnectTimer) {
    return;
  }
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connect();
  }, 1500);
}

function send(payload) {
  if (FIREFOX_MODE) {
    void fetch(BRIDGE_HTTP_PUSH, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    }).then(() => {
      setBridgeConnected(true);
    }).catch(() => {
      setBridgeConnected(false);
    });
    return true;
  }
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    setBridgeConnected(false);
    return false;
  }
  socket.send(JSON.stringify(payload));
  setBridgeConnected(true);
  return true;
}

function startFirefoxPolling() {
  if (pollTimer) {
    return;
  }
  pollTimer = setInterval(() => {
    void pollCommands();
  }, 1000);
}

async function pollCommands() {
  try {
    const response = await fetch(`${BRIDGE_HTTP_COMMANDS}?afterId=${lastCommandId}`, { method: "GET" });
    if (!response.ok) {
      setBridgeConnected(false);
      return;
    }
    setBridgeConnected(true);
    const payload = await response.json();
    for (const command of payload.commands || []) {
      lastCommandId = Math.max(lastCommandId, Number(command.id) || 0);
      await dispatchCommand(command);
    }
  } catch {
    setBridgeConnected(false);
  }
}

function getBridgeState() {
  const activeSites = [...new Set(
    [...sessions.values()]
      .map((session) => session.site)
      .filter((item) => typeof item === "string" && item)
  )];

  return {
    isConnected: bridgeConnected,
    browserName: detectBrowserName(),
    transport: FIREFOX_MODE ? "HTTP polling" : "WebSocket",
    sessionCount: sessions.size,
    activeSites
  };
}

function forceReconnect() {
  if (socket) {
    try {
      socket.close();
    } catch {
    }
    socket = null;
  }
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
  setBridgeConnected(false);
  connect();
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
  if (command.type === "media_zoom_in" || command.type === "media_zoom_out") {
    try {
      const currentZoom = await chrome.tabs.getZoom(session.tabId);
      const delta = command.type === "media_zoom_in" ? 0.1 : -0.1;
      const nextZoom = Math.max(0.3, Math.min(3, currentZoom + delta));
      await chrome.tabs.setZoom(session.tabId, Number(nextZoom.toFixed(2)));
    } catch {
    }
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
  if (ua.includes("Firefox/")) return "Firefox";
  if (ua.includes("Chrome/")) return "Google Chrome";
  return "Chromium Browser";
}

function detectBrowserId(browserName) {
  if (browserName === "Yandex Browser") return "yandex";
  if (browserName === "Microsoft Edge") return "edge";
  if (browserName === "Firefox") return "firefox";
  if (browserName === "Google Chrome") return "chrome";
  return "chromium";
}

chrome.runtime.onInstalled.addListener(() => connect());
chrome.runtime.onStartup?.addListener(() => connect());
connect();

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "get_bridge_state") {
    sendResponse(getBridgeState());
    return;
  }

  if (message?.type === "force_reconnect") {
    forceReconnect();
    sendResponse({ ok: true });
    return;
  }

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
