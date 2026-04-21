# Запуск CLI

Связанные документы:

- `docs/README.md` - карта документации.
- `docs/authoring-quest-format.md` - упрощенный authoring-формат и его ограничения.
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

CLI также умеет стартовать без явного `--quest` и тогда использует встроенный путь по умолчанию:

```bash
dotnet run --project src/TextQuest.Cli -- --saves-dir ./.local/saves
```

Если добавить `--quest-format authoring-yaml`, по умолчанию будет использован `content/quests/demo-quest.author.yml`.

## Аргументы

- `--quest <path>` - путь к runtime JSON-файлу квеста или к authoring YAML-файлу, в зависимости от `--quest-format`.
- `--quest-format <runtime-json|authoring-yaml>` - выбрать прямую загрузку runtime JSON или компиляцию authoring YAML перед запуском.
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
