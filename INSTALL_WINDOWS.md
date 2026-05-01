# Установка Nexus Remote PC

## Что установить

Используйте файл `NexusRemotePC-Setup.msi`.

Путь после локальной сборки:

```text
C:\Users\salum\AndroidStudioProjects\Nexus Remote PC\bin\Release\NexusRemotePC-Setup.msi
```

## Установка

1. Запустите `NexusRemotePC-Setup.msi`.
2. Подтвердите установку в Windows.
3. После завершения откройте `Nexus Remote PC` через меню Пуск.
4. Убедитесь, что в окне появился QR-код и статус `Онлайн`.

## Что делает установщик

- ставит приложение в `Program Files`;
- добавляет ярлык в меню Пуск;
- добавляет удаление через системный список приложений;
- создаёт firewall rule для TCP `8765`;
- создаёт firewall rule для UDP `8766`.

## Если Windows спрашивает про сеть

Разрешите доступ для частной сети. Для домашнего Wi‑Fi это обязательный шаг.
