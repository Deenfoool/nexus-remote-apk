# Nexus Remote Browser Bridge Privacy Policy

`Nexus Remote Browser Bridge` works only on supported media sites and only on the local computer where it is installed.

## What data the extension reads

The extension reads media metadata from supported tabs:

- page URL and site hostname;
- tab title;
- media title, artist, album, artwork URL;
- playback state, mute state, current position, duration;
- browser tab id needed to send playback commands back to that tab.

Supported sites right now:

- `youtube.com`
- `music.youtube.com`
- `vk.com`

## What the extension sends

The extension sends this media metadata only to the local companion app:

- `Nexus Remote PC`
- local bridge address: `127.0.0.1`

The extension does not send data to external cloud servers.

## What the extension does not collect

The extension does not:

- read passwords, payment data, or form inputs;
- scan every website by default;
- upload browsing history to remote servers;
- create a user account;
- use analytics, ads, or third-party trackers.

## Permissions

- `tabs` is used to detect supported media tabs and know which tab should receive playback commands.
- host permissions for YouTube and VK are used to read media metadata only on those supported sites.
- `storage` is reserved for extension settings and future user preferences.

## Local network behavior

The extension communicates only with the local desktop app running on the same computer.

- Chromium-based browsers use a local WebSocket connection to `ws://127.0.0.1:8767/browser-bridge`.
- Firefox uses local HTTP polling against `http://127.0.0.1:8767/browser-bridge/*`.

## Contact

Project repository:

- [https://github.com/Deenfoool/nexus-remote-apk](https://github.com/Deenfoool/nexus-remote-apk)
