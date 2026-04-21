# TextQuest Engine

Минимальный MVP .NET-движка для текстовых квестов с JSON-контентом, общим runtime и CLI-фронтендом.

## Состав

- `src/TextQuest.Domain` - доменные модели квеста и состояния.
- `src/TextQuest.Application` - runtime, условия, эффекты, orchestration.
- `src/TextQuest.Content.Json` - загрузка и валидация JSON-квестов.
- `src/TextQuest.Infrastructure` - файловые сохранения.
- `src/TextQuest.Frontends.Contracts` - frontend-neutral модели представления.
- `src/TextQuest.Cli` - консольный запуск квеста.
- `tests/TextQuest.Tests` - unit и integration tests.

## Требования

- .NET SDK с поддержкой `net10.0`

## Быстрый Старт

Сборка solution:

```bash
dotnet build TextQuest.sln
```

Запуск тестов:

```bash
dotnet test TextQuest.sln
```

Запуск CLI с демонстрационным квестом:

```bash
dotnet run --project src/TextQuest.Cli -- --quest content/quests/demo-quest.json --saves-dir ./.local/saves
```

Если `--quest` не передан, CLI по умолчанию использует `content/quests/demo-quest.json`.

## Команды CLI

- `1`, `2`, ... - выбрать вариант по номеру.
- `save [id]` - сохранить текущее состояние. Без `id` используется слот `quick`.
- `load [id]` - загрузить сохранение. Без `id` используется слот `quick`.
- `exit` - завершить игру.

Для структурных логов можно выставить `TEXTQUEST_LOG_LEVEL`, например:

```bash
TEXTQUEST_LOG_LEVEL=Debug dotnet run --project src/TextQuest.Cli -- --quest content/quests/demo-quest.json
```

## Документация

- `docs/README.md` - карта документации и рекомендованный порядок чтения.
- `docs/current-state.md` - текущее состояние реализованного MVP и репозитория.
- `docs/cli.md` - запуск CLI и параметры.
- `docs/json-quest-format.md` - зафиксированный JSON-формат квеста MVP.
- `docs/technical-vision.md` - архитектурный контекст.
- `docs/mvp-plan.md` - завершенный план поставки MVP.
