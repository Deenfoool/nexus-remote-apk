# Nexus Remote PC

Windows-компаньон для `Nexus Remote`.

## Что есть в релизе

- native WPF GUI;
- QR-сопряжение с Android;
- список доверенных устройств;
- лог-файл в `%APPDATA%\Nexus Remote PC\logs`;
- журнал событий и ошибок прямо в окне приложения;
- проверка обновлений через GitHub Releases;
- MSI installer для обычной установки.

## Сборка

```bat
build-release.bat
```

Результат:

- `bin\Release\net8.0-windows\win-x64\publish\NexusRemotePC.exe`
- `bin\Release\NexusRemotePC-Setup.msi`

## Пользовательская установка

Лучше использовать MSI, а не запускать `.exe` вручную. Установщик создаёт правила firewall и ярлык в меню Пуск.

## Где лежат данные

- настройки и доверенные устройства: `%APPDATA%\Nexus Remote PC`
- лог-файл: `%APPDATA%\Nexus Remote PC\logs\nexus-remote.log`
