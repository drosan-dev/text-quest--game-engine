# Запуск CLI

Связанные документы:

- `docs/README.md` - карта документации.
- `docs/authoring-quest-format.md` - упрощенный authoring-формат и его ограничения.
- `docs/authoring-timeline-format.md` - формат timeline-кампаний (campaign.yml + локации).
- `docs/technical-vision.md` - место CLI в архитектуре.
- `docs/adapter-extension-points.md` - контракт, через который должен работать любой новый адаптер.

## Требования

- .NET SDK с поддержкой `net10.0`

## Сборка

```bash
dotnet build TextQuest.sln
```

## Запуск Демонстрационного Квеста

Из корня репозитория:

```bash
dotnet run --project src/TextQuest.Cli -- --quest content/quests/demo-quest.json --saves-dir ./.local/saves
```

Запуск того же демо-квеста через authoring YAML-слой:

```bash
dotnet run --project src/TextQuest.Cli -- --quest-format authoring-yaml --quest content/quests/demo-quest.author.yml --saves-dir ./.local/saves
```

Запуск demo timeline-кампании:

```bash
dotnet run --project src/TextQuest.Cli -- --quest-format timeline-yaml --quest content/timeline/demo-campaign/campaign.yml --saves-dir ./.local/saves
```

CLI также умеет стартовать без явного `--quest` и тогда использует встроенный путь по умолчанию:

```bash
dotnet run --project src/TextQuest.Cli -- --saves-dir ./.local/saves
```

Если добавить `--quest-format authoring-yaml`, по умолчанию будет использован `content/quests/demo-quest.author.yml`.
Если добавить `--quest-format timeline-yaml`, по умолчанию будет использован `content/timeline/demo-campaign/campaign.yml`.

## Аргументы

- `--quest <path>` - путь к входному файлу контента в зависимости от `--quest-format` (runtime JSON, authoring YAML или timeline campaign.yml).
- `--quest-format <runtime-json|authoring-yaml|timeline-yaml>` - выбрать формат входного контента.
- `--saves-dir <path>` - директория для файлов сохранений.

Если `--saves-dir` не указан, используется стандартная директория файлового save store, определяемая infrastructure-слоем.

Если `--quest-format` не указан, используется `runtime-json`.

## Команды Во Время Игры

- `1`, `2`, ... - применить выбор по номеру из текущего списка.
- `save [id]` - сохранить состояние в слот `id`. Если `id` не указан, используется `quick`.
- `load [id]` - загрузить состояние из слота `id`. Если `id` не указан, используется `quick`.
- `exit` - завершить текущую сессию.

## Логи

CLI пишет структурированные JSON-логи в `stderr`.

Уровень логирования задается через `TEXTQUEST_LOG_LEVEL`:

```bash
TEXTQUEST_LOG_LEVEL=Debug dotnet run --project src/TextQuest.Cli -- --quest content/quests/demo-quest.json
```

То же для authoring-слоя:

```bash
TEXTQUEST_LOG_LEVEL=Debug dotnet run --project src/TextQuest.Cli -- --quest-format authoring-yaml --quest content/quests/demo-quest.author.yml
```

Типовой сценарий для отладки CLI:

```bash
TEXTQUEST_LOG_LEVEL=Debug dotnet run --project src/TextQuest.Cli -- --quest content/quests/demo-quest.json 2>cli.log
```
