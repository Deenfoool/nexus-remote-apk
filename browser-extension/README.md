# Nexus Remote Browser Bridge

Локальное расширение для Chromium-браузеров, которое передаёт метаданные активных медиа-вкладок в `Nexus Remote PC`.

## Что умеет

- отслеживает YouTube, YouTube Music и VK;
- читает `navigator.mediaSession.metadata`, когда сайт её заполняет;
- использует DOM/OpenGraph fallback для обложки и названия;
- отправляет данные по `ws://127.0.0.1:8767/browser-bridge`.

## Как установить

1. Откройте страницу расширений:
   - Chrome: `chrome://extensions`
   - Edge: `edge://extensions`
   - Yandex Browser: `browser://extensions`
2. Включите режим разработчика.
3. Нажмите `Load unpacked` / `Загрузить распакованное расширение`.
4. Выберите папку `browser-extension` из репозитория.
5. Убедитесь, что `Nexus Remote PC` уже запущен.

## Ограничения

- Сейчас это bridge только для whitelist-доменов: `youtube.com`, `music.youtube.com`, `vk.com`.
- Команды управления вкладкой добавим следующим шагом; на этом этапе идёт сбор правильных metadata, artwork и timeline.
