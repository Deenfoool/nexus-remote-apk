(function () {
  const SUPPORTED_HOSTS = ["youtube.com", "music.youtube.com", "vk.com"];
  const browserHint = detectBrowserHint();
  let lastSignature = "";
  let observedMedia = null;

  function detectBrowserHint() {
    const ua = navigator.userAgent;
    if (ua.includes("YaBrowser")) return "yandex";
    if (ua.includes("Edg/")) return "edge";
    if (ua.includes("Firefox/")) return "firefox";
    if (ua.includes("Chrome/")) return "chrome";
    return "chromium";
  }

  function isSupportedHost(host) {
    return SUPPORTED_HOSTS.some((item) => host === item || host.endsWith(`.${item}`));
  }

  function chooseMediaElement() {
    const elements = Array.from(document.querySelectorAll("video, audio"));
    if (elements.length === 0) {
      return null;
    }

    const playing = elements.find((item) => !item.paused && !item.ended && item.readyState > 0);
    if (playing) {
      return playing;
    }

    return elements
      .sort((left, right) => mediaScore(right) - mediaScore(left))[0] || null;
  }

  function mediaScore(element) {
    const base = (element.clientWidth || 0) * (element.clientHeight || 0);
    return base + (element.currentTime > 0 ? 1000 : 0);
  }

  function extractMediaSessionMetadata() {
    const metadata = navigator.mediaSession && navigator.mediaSession.metadata;
    if (!metadata) {
      return {};
    }

    const artwork = Array.isArray(metadata.artwork) && metadata.artwork.length > 0
      ? metadata.artwork[metadata.artwork.length - 1].src
      : "";

    return {
      title: safe(metadata.title),
      artist: safe(metadata.artist),
      album: safe(metadata.album),
      artworkUrl: safe(artwork)
    };
  }

  function safe(value) {
    if (typeof value !== "string") {
      return "";
    }
    return value.trim();
  }

  function queryMeta(propertyName) {
    const propertyNode = document.querySelector(`meta[property="${propertyName}"]`);
    if (propertyNode && propertyNode.content) {
      return propertyNode.content.trim();
    }
    const nameNode = document.querySelector(`meta[name="${propertyName}"]`);
    if (nameNode && nameNode.content) {
      return nameNode.content.trim();
    }
    return "";
  }

  function extractYouTubeFallback(mediaElement) {
    const title =
      safe(document.querySelector("h1.ytd-watch-metadata yt-formatted-string")?.textContent) ||
      safe(document.querySelector("h1.title yt-formatted-string")?.textContent) ||
      safe(document.title.replace(/\s*-\s*YouTube\s*$/i, ""));

    const artist =
      safe(document.querySelector("#owner a")?.textContent) ||
      safe(document.querySelector("ytd-channel-name a")?.textContent);

    const artworkUrl =
      queryMeta("og:image") ||
      safe(document.querySelector("link[rel='image_src']")?.href) ||
      safe(mediaElement?.poster);

    return {
      mediaKind: "video",
      title,
      artist,
      artworkUrl,
      canNext: Boolean(document.querySelector(".ytp-next-button")),
      canPrevious: Boolean(document.querySelector(".ytp-prev-button"))
    };
  }

  function extractVkFallback(mediaElement) {
    const title =
      safe(document.querySelector("[data-testid='audio_page_player_track_title']")?.textContent) ||
      safe(document.querySelector(".audio_page_player_track_title")?.textContent) ||
      queryMeta("og:title") ||
      safe(document.title);

    const artist =
      safe(document.querySelector("[data-testid='audio_page_player_track_subtitle']")?.textContent) ||
      safe(document.querySelector(".audio_page_player_track_subtitle")?.textContent) ||
      safe(document.querySelector(".audio_page_player_meta_artist")?.textContent);

    const artworkUrl =
      queryMeta("og:image") ||
      safe(document.querySelector(".audio_page_player_cover img")?.src) ||
      safe(document.querySelector("img.AudioPlayer__cover")?.src) ||
      safe(mediaElement?.poster);

    const mediaKind = document.querySelector("video") ? "video" : "music";

    return {
      mediaKind,
      title,
      artist,
      artworkUrl,
      canNext: hasAnySelector([
        "[data-testid='audio-player-controls-next']",
        ".audio_page_player_ctrl.next",
        ".AudioPlayer__controls .next"
      ]),
      canPrevious: hasAnySelector([
        "[data-testid='audio-player-controls-prev']",
        ".audio_page_player_ctrl.prev",
        ".AudioPlayer__controls .prev"
      ])
    };
  }

  function extractGenericFallback(mediaElement) {
    return {
      mediaKind: mediaElement instanceof HTMLVideoElement ? "video" : "music",
      title: queryMeta("og:title") || safe(document.title),
      artist: "",
      artworkUrl: queryMeta("og:image") || safe(mediaElement?.poster),
      canNext: false,
      canPrevious: false
    };
  }

  function buildSession(mediaElement) {
    const host = location.hostname.replace(/^www\./i, "");
    if (!isSupportedHost(host)) {
      return null;
    }

    const base = {
      browserHint,
      site: host,
      pageUrl: location.href,
      tabTitle: safe(document.title),
      title: "",
      artist: "",
      album: "",
      artworkUrl: "",
      artworkBase64: "",
      mediaKind: mediaElement instanceof HTMLVideoElement ? "video" : "music",
      isPlaying: !mediaElement.paused && !mediaElement.ended,
      isMuted: Boolean(mediaElement.muted),
      positionMs: Number.isFinite(mediaElement.currentTime) ? Math.round(mediaElement.currentTime * 1000) : null,
      durationMs: Number.isFinite(mediaElement.duration) ? Math.round(mediaElement.duration * 1000) : null,
      canTogglePlayPause: true,
      canNext: false,
      canPrevious: false,
      canSeek: Number.isFinite(mediaElement.duration) && mediaElement.duration > 0
    };

    const mediaSessionMetadata = extractMediaSessionMetadata();
    let fallback = extractGenericFallback(mediaElement);

    if (host.includes("youtube")) {
      fallback = extractYouTubeFallback(mediaElement);
    } else if (host.includes("vk.com")) {
      fallback = extractVkFallback(mediaElement);
    }

    return {
      ...base,
      title: mediaSessionMetadata.title || fallback.title || base.tabTitle,
      artist: mediaSessionMetadata.artist || fallback.artist || "",
      album: mediaSessionMetadata.album || "",
      artworkUrl: mediaSessionMetadata.artworkUrl || fallback.artworkUrl || "",
      mediaKind: fallback.mediaKind || base.mediaKind,
      canNext: Boolean(fallback.canNext),
      canPrevious: Boolean(fallback.canPrevious)
    };
  }

  function hasAnySelector(selectors) {
    return selectors.some((selector) => Boolean(document.querySelector(selector)));
  }

  function sendKeyboardLikeCommand(code, key) {
    const target = document.activeElement || document.body;
    if (!target) {
      return;
    }
    ["keydown", "keyup"].forEach((type) => {
      target.dispatchEvent(new KeyboardEvent(type, { key, code, bubbles: true }));
    });
  }

  function clickFirst(selectors) {
    for (const selector of selectors) {
      const node = document.querySelector(selector);
      if (node instanceof HTMLElement) {
        node.click();
        return true;
      }
    }
    return false;
  }

  function toggleFullscreen(mediaElement) {
    if (document.fullscreenElement) {
      document.exitFullscreen?.();
      return;
    }
    const target = mediaElement || document.documentElement;
    target.requestFullscreen?.();
  }

  function handleCommand(command) {
    const mediaElement = chooseMediaElement();
    if (!mediaElement) {
      return { ok: false };
    }

    switch (command.type) {
      case "media_play_pause":
        if (mediaElement.paused) {
          mediaElement.play?.();
        } else {
          mediaElement.pause?.();
        }
        return { ok: true };
      case "media_stop":
        mediaElement.pause?.();
        mediaElement.currentTime = 0;
        return { ok: true };
      case "media_seek_relative": {
        const seconds = Number(command.payload?.seconds || 0);
        if (!Number.isFinite(seconds)) return { ok: false };
        mediaElement.currentTime = Math.max(0, mediaElement.currentTime + seconds);
        return { ok: true };
      }
      case "media_seek_to": {
        const positionMs = Number(command.payload?.positionMs || 0);
        if (!Number.isFinite(positionMs)) return { ok: false };
        mediaElement.currentTime = Math.max(0, positionMs / 1000);
        return { ok: true };
      }
      case "media_fullscreen":
        toggleFullscreen(mediaElement);
        return { ok: true };
      case "media_subtitles":
        if (location.hostname.includes("youtube")) {
          const clicked = clickFirst([".ytp-subtitles-button", ".ytp-captions-button"]);
          if (!clicked) sendKeyboardLikeCommand("KeyC", "c");
          return { ok: true };
        }
        return { ok: false };
      case "media_mute":
        mediaElement.muted = !mediaElement.muted;
        return { ok: true };
      case "media_volume_relative": {
        const delta = Number(command.payload?.delta || 0);
        if (!Number.isFinite(delta)) return { ok: false };
        mediaElement.volume = Math.max(0, Math.min(1, mediaElement.volume + delta / 100));
        if (mediaElement.volume > 0) mediaElement.muted = false;
        return { ok: true };
      }
      case "media_next":
        if (location.hostname.includes("youtube")) {
          return { ok: clickFirst([".ytp-next-button"]) };
        }
        if (location.hostname.includes("vk.com")) {
          return { ok: clickFirst([
            "[data-testid='audio-player-controls-next']",
            ".audio_page_player_ctrl.next",
            ".AudioPlayer__controls .next"
          ]) };
        }
        return { ok: false };
      case "media_prev":
        if (location.hostname.includes("youtube")) {
          return { ok: clickFirst([".ytp-prev-button"]) };
        }
        if (location.hostname.includes("vk.com")) {
          return { ok: clickFirst([
            "[data-testid='audio-player-controls-prev']",
            ".audio_page_player_ctrl.prev",
            ".AudioPlayer__controls .prev"
          ]) };
        }
        return { ok: false };
      default:
        return { ok: false };
    }
  }

  function pushState() {
    const mediaElement = chooseMediaElement();
    if (!mediaElement) {
      if (lastSignature) {
        lastSignature = "";
        chrome.runtime.sendMessage({ type: "media_gone" });
      }
      observedMedia = null;
      return;
    }

    if (observedMedia !== mediaElement) {
      attachMediaEvents(mediaElement);
      observedMedia = mediaElement;
    }

    const session = buildSession(mediaElement);
    if (!session) {
      return;
    }

    const signature = JSON.stringify(session);
    if (signature === lastSignature) {
      return;
    }

    lastSignature = signature;
    chrome.runtime.sendMessage({ type: "media_state", session });
  }

  function attachMediaEvents(mediaElement) {
    ["play", "pause", "timeupdate", "durationchange", "loadedmetadata", "volumechange", "ended", "seeking", "seeked"].forEach((eventName) => {
      mediaElement.addEventListener(eventName, () => {
        pushState();
      });
    });
  }

  const observer = new MutationObserver(() => pushState());
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true
  });

  setInterval(pushState, 500);
  window.addEventListener("focus", pushState);
  document.addEventListener("visibilitychange", pushState);
  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (!message || message.type !== "nexus_command") {
      return;
    }
    const result = handleCommand(message.command || {});
    sendResponse(result);
  });
  pushState();
})();
