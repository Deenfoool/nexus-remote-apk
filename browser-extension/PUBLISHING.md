# Publishing Guide

This folder contains the browser companion for `Nexus Remote PC`.

## Build environment

- Windows 10/11
- PowerShell 5.1+ or PowerShell 7+
- .NET runtime is not required for the extension itself
- no Node.js, npm, webpack, minifier, or transpiler is used

## Ready artifacts

Build packages with:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-browser-extensions.ps1
```

The build script uses:

- `browser-extension/manifest.chromium.json`
- `browser-extension/manifest.firefox.json`
- `browser-extension/background.js`
- `browser-extension/content.js`
- `browser-extension/popup.html`
- `browser-extension/popup.css`
- `browser-extension/popup.js`
- `browser-extension/source-assets/icon-source.png`

The script generates resized icons into `browser-extension/icons/` and then creates the final browser packages.

Output:

- `browser-extension/dist/Nexus-Remote-Browser-Bridge-Chrome.zip`
- `browser-extension/dist/Nexus-Remote-Browser-Bridge-Yandex.zip`
- `browser-extension/dist/Nexus-Remote-Browser-Bridge-Chromium.zip`
- `browser-extension/dist/Nexus-Remote-Browser-Bridge-Firefox.xpi`

## Store preparation checklist

Before publishing:

1. Rebuild the packages from the current source.
2. Check that `Nexus Remote PC` is running and the popup shows bridge status correctly.
3. Verify playback on:
   - YouTube
   - YouTube Music
   - VK music
   - VK video
4. Confirm that supported permissions match the actual code.
5. Prepare screenshots of:
   - extension popup connected;
   - extension popup waiting for PC app;
   - media control working in browser + phone.

## Chrome Web Store

Submit:

- unpacked source from `browser-extension/dist/chrome-unpacked`
- or upload a packaged zip created from the same folder if needed by your workflow

You will need:

- extension name;
- short description;
- detailed description;
- 128x128 icon;
- screenshots;
- privacy policy URL or published privacy text.

## Firefox Add-ons

Current package:

- `browser-extension/dist/Nexus-Remote-Browser-Bridge-Firefox.xpi`

Notes:

- local installation works as a temporary add-on;
- permanent public distribution usually requires Mozilla signing;
- the Firefox build uses local HTTP polling instead of WebSocket.
- for new AMO submissions, the manifest already includes `browser_specific_settings.gecko.data_collection_permissions.required = ["none"]`.

## Yandex Browser

For local installation, use:

- `browser-extension/dist/yandex-unpacked`

If later you want browser-store style distribution, the Chromium package is the base.

## Suggested listing copy

Use the prepared store text from:

- [STORE_LISTING.md](C:/Users/salum/AndroidStudioProjects/nexusremote/browser-extension/STORE_LISTING.md)

## Privacy policy

Use:

- [PRIVACY.md](C:/Users/salum/AndroidStudioProjects/nexusremote/browser-extension/PRIVACY.md)
