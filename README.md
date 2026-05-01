# Nexus Remote

`Nexus Remote` это связка из Android-приложения и Windows-компаньона для управления своим ПК по локальной сети.

Что умеет релиз `1.0.0`:

- подключение через QR-сопряжение;
- мониторинг CPU, RAM, диска, сети, громкости и доступных сенсоров ПК;
- управление медиа и системными действиями;
- запуск выбранных программ с телефона;
- список доверенных устройств, логи и журнал ошибок на ПК;
- автопоиск сервера в локальной сети.

## Что нужно пользователю

- Windows 10/11 для PC-приложения;
- Android 7.0+ для мобильного приложения;
- телефон и ПК должны быть в одной локальной сети;
- для первого подключения нужен доступ к камере на телефоне.

## Быстрый старт

1. Установите `Nexus Remote PC` на компьютер.
2. Запустите приложение на ПК и дождитесь QR-кода.
3. Установите Android-приложение `Nexus Remote`.
4. На телефоне откройте подключение и нажмите `Сканировать QR сопряжения`.
5. Подтвердите новое устройство на ПК.

Подробные инструкции:

- [Установка Windows](C:/Users/salum/AndroidStudioProjects/nexusremote/INSTALL_WINDOWS.md)
- [Установка Android](C:/Users/salum/AndroidStudioProjects/nexusremote/INSTALL_ANDROID.md)
- [FAQ и troubleshooting](C:/Users/salum/AndroidStudioProjects/nexusremote/FAQ.md)
- [История изменений](C:/Users/salum/AndroidStudioProjects/nexusremote/CHANGELOG.md)

## Артефакты релиза

После сборки основные файлы лежат здесь:

- Windows installer: `C:\Users\salum\AndroidStudioProjects\Nexus Remote PC\bin\Release\NexusRemotePC-Setup.msi`
- Android APK: `C:\Users\salum\AndroidStudioProjects\nexusremote\app\build\outputs\apk\release\app-release.apk`
- Android AAB: `C:\Users\salum\AndroidStudioProjects\nexusremote\app\build\outputs\bundle\release\app-release.aab`

Для сборки общего пакета релиза:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release-package.ps1
```
