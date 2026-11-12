# Структура проекта / Project structure

## Русский

`GameLauncherGUI` остаётся одним WinForms-проектом. Каталоги разделяют ответственность, но сами по себе не обеспечивают изоляцию сборок или зависимостей.

- `Application` — главное окно и связывание функций приложения.
- `Bootstrap` — точка входа и экран запуска.
- `Features` — библиотека, запуск игр, Steam, моды, DLSS, производительность, рекомендации, настройки, обновления, сеть, сохранения и другие предметные области.
- `UI/Controls` и `UI/Theming` — общие элементы и оформление.
- `Infrastructure/Async` — управление фоновыми задачами, отменой и кэшем изображений.
- `Assets` — встроенные ресурсы; `Properties` — метаданные сборки.

Поиск игр вынесен из `Form1` в `Features/Library/Services/GameDiscovery.cs`. Он не зависит от окна. Модель `GameInfo` находится в `Features/Library/Models`. Существующие пространства имён остальных типов сохранены для совместимости; несколько старых файлов ещё объединяют интерфейс и сервисы. Их дальнейшее разделение требует отдельных изменений и проверок. Это реорганизация, а не заявление о завершённой модульной архитектуре.

Тесты распределены по соответствующим областям в `GameLauncherGUI.Tests`. Руководства находятся в `docs/features`, инженерные отчёты — в `docs/engineering`, описания версий — в `docs/releases`. Стандартные README, LICENSE, CONTRIBUTING и SECURITY остаются в корне.

## English

The launcher remains one WinForms assembly. `Application` coordinates features; `Features` groups domain code; `UI` contains shared controls and styling; `Infrastructure` manages asynchronous work. Folder boundaries are not enforced assembly boundaries. Existing namespaces remain compatible, and some legacy files still mix services and UI.

Game discovery has been extracted from the main form into a UI-independent library service. Tests follow feature folders. Documentation is under `docs`, with standard repository entry documents remaining at the root.
