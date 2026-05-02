# Nexus Remote Browser Bridge

Настоящий набор локальных расширений для браузеров, который передаёт метаданные активных медиа-вкладок в `Nexus Remote PC`.

## Что умеет

- отслеживает YouTube, YouTube Music и VK;
- читает `navigator.mediaSession.metadata`, когда сайт её заполняет;
- использует DOM/OpenGraph fallback для обложки и названия;
- отправляет данные в локальный bridge `Nexus Remote PC`;
- показывает мини-панель состояния в popup самого расширения.

## Готовые пакеты

После запуска [build-browser-extensions.ps1](C:/Users/salum/AndroidStudioProjects/nexusremote/build-browser-extensions.ps1) в папке `browser-extension/dist` появляются:

- `Nexus-Remote-Browser-Bridge-Chrome.zip`
- `Nexus-Remote-Browser-Bridge-Yandex.zip`
- `Nexus-Remote-Browser-Bridge-Chromium.zip`
- `Nexus-Remote-Browser-Bridge-Firefox.xpi`
- `chrome-unpacked/`
- `yandex-unpacked/`
- `chromium-unpacked/`
- `firefox-unpacked/`

## Как установить

### Google Chrome

1. Распакуйте `Nexus-Remote-Browser-Bridge-Chrome.zip` в удобную папку.
2. Откройте `chrome://extensions`.
3. Включите режим разработчика.
4. Нажмите `Load unpacked`.
5. Выберите папку `browser-extension/dist/chrome-unpacked`.
6. Убедитесь, что `Nexus Remote PC` уже запущен.

### Yandex Browser

1. Распакуйте `Nexus-Remote-Browser-Bridge-Yandex.zip` в удобную папку.
2. Откройте `browser://extensions`.
3. Включите режим разработчика.
4. Нажмите `Загрузить распакованное расширение`.
5. Выберите папку `browser-extension/dist/yandex-unpacked`.
6. Убедитесь, что `Nexus Remote PC` уже запущен.

### Microsoft Edge

1. Распакуйте `Nexus-Remote-Browser-Bridge-Chromium.zip` в удобную папку.
2. Откройте `edge://extensions`.
3. Включите режим разработчика.
4. Нажмите `Load unpacked`.
5. Выберите папку `browser-extension/dist/chromium-unpacked`.
5. Убедитесь, что `Nexus Remote PC` уже запущен.

### Firefox

1. Откройте `about:debugging#/runtime/this-firefox`.
2. Нажмите `Load Temporary Add-on`.
3. Выберите файл [Nexus-Remote-Browser-Bridge-Firefox.xpi](C:/Users/salum/AndroidStudioProjects/nexusremote/browser-extension/dist/Nexus-Remote-Browser-Bridge-Firefox.xpi).
4. Убедитесь, что `Nexus Remote PC` уже запущен.

Важно:
- в таком режиме Firefox загружает расширение временно;
- после перезапуска Firefox его нужно загрузить снова через `about:debugging`;
- `.xpi` уже собирается автоматически, но для постоянной установки вне режима отладки Firefox обычно требует подпись.
- после установки нажмите на иконку расширения: popup покажет, видит ли расширение bridge и сколько сейчас активных медиа-источников.

## Ограничения

- Сейчас это bridge только для whitelist-доменов: `youtube.com`, `music.youtube.com`, `vk.com`.
- Пакеты готовы для локальной установки, но публикация в официальные магазины браузеров потребует отдельной подготовки и, для части браузеров, подписи.

## Публикация

Для подготовки к публикации смотри:

- [PUBLISHING.md](C:/Users/salum/AndroidStudioProjects/nexusremote/browser-extension/PUBLISHING.md)
- [STORE_LISTING.md](C:/Users/salum/AndroidStudioProjects/nexusremote/browser-extension/STORE_LISTING.md)
- [PRIVACY.md](C:/Users/salum/AndroidStudioProjects/nexusremote/browser-extension/PRIVACY.md)
