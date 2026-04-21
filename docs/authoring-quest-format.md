# Authoring YAML-формат Квеста

Связанные документы:

- `docs/README.md` - карта документации.
- `docs/current-state.md` - что реально реализовано в репозитории сейчас.
- `docs/json-quest-format.md` - runtime-контракт, в который компилируется authoring-слой.
- `docs/cli.md` - запуск CLI и выбор формата через флаг.

Этот документ фиксирует упрощенный authoring YAML-формат, который компилируется в поддерживаемый runtime JSON-контракт. Он предназначен для написания квеста человеком и не заменяет `docs/json-quest-format.md` как описание итогового runtime-формата.

## Верхний Уровень

```yaml
questId: demo_cell
version: 1.0.0
title: Пробуждение в камере
start: intro

vars:
  resolve: 0

flags:
  hasKey: false

textPools:
  cell_flavor:
    - В углу пахнет плесенью и мокрой соломой.
    - Где-то за стеной капает вода, мерно отбивая время.

scenes:
  intro:
    text: Вы приходите в себя в сырой камере.
    choices: []
```

Поддерживаемые поля:

- `questId` - обязательный непустой идентификатор квеста.
- `version` - обязательная непустая версия квеста.
- `title` - обязательный непустой заголовок.
- `start` - предпочтительный псевдоним для `startNodeId`.
- `startNodeId` - поддерживается как явное runtime-совместимое имя.
- `vars` - предпочтительный псевдоним для `variables`.
- `variables` - поддерживается как явное runtime-совместимое имя.
- `flags` - объект `string -> bool`.
- `textPools` - опциональный объект `poolId -> string[]` с текстовыми пулами (используются через `{{pool:poolId}}`).
- `scenes` - обязательный объект `sceneId -> scene definition`.

## Общая Форма Сцены

```yaml
text:
  - Первая строка.
  - Вторая строка.
```

Правила:

- ключ в `scenes` становится `node.id` в runtime-модели.
- `text` может быть строкой или массивом строк.
- `type` опционален, если его можно однозначно вывести из структуры сцены.
- если у сцены есть `result`, она трактуется как `end`.
- если у сцены есть `branches` или `default`, она трактуется как `branch`.
- если у сцены есть `choices`, но `type` не указан, она трактуется как `text`.

## Сцены С Выборами

```yaml
text: Шаги в коридоре затихают.
choices:
  - id: unlock_door
    text: Попробовать открыть дверь ключом
    when: hasKey
    goto: escape
  - id: force_door
    text: Навалиться на дверь плечом
    when: resolve >= 1
    do: resolve += 1
    goto: captured
```

Правила:

- `choices` соответствует runtime `choices`.
- `goto` - authoring-псевдоним для `nextNodeId`.
- `id` у choice обязателен.
- это требование нужно, чтобы save/load и `decisionHistory` не зависели от текста выбора, его порядка и authoring-эвристик генерации id.
- `when` может быть строкой или массивом строк.
- `do` может быть строкой или массивом строк.

## Сцены Автоматического Ветвления

```yaml
branches:
  - when: hasKey
    goto: escape
default: captured
```

Правила:

- `branches[].when` компилируется в runtime `conditions`.
- `branches[].goto` - authoring-псевдоним для `nextNodeId`.
- `default` - authoring-псевдоним для `defaultNextNodeId`.

## Финальные Сцены

```yaml
text: Замок поддается, и вы бесшумно исчезаете в коридоре.
result: victory
```

Правила:

- `result` делает сцену `end`-узлом, если `type` не задан явно.

## Sugar Для `when`

Поддерживаются формы:

- `hasKey` -> флаг `hasKey == true`
- `'!hasKey'` -> флаг `hasKey == false`
- `resolve >= 1`
- `resolve == 0`
- `hasKey != true`

Ограничения:

- отрицание через `!` в YAML нужно заключать в кавычки, иначе YAML воспримет его как tag syntax.
- сокращенная форма без оператора поддерживается только для флагов.
- для переменных нужно явно писать оператор сравнения.

## Sugar Для `do`

Поддерживаются формы:

- `hasKey = true` -> `set_flag`
- `resolve = 3` -> `set_variable`
- `resolve += 1` -> `add`
- `resolve -= 1` -> `add` со значением `-1`

Ограничения:

- `+=` и `-=` работают только с integer.
- если тип цели не удается однозначно вывести, окончательную проверку выполняет runtime-валидатор после компиляции.

## Пример Полного Authoring-Квеста

Актуальный рабочий пример лежит в `content/quests/demo-quest.author.yml`.

## Отношение К Runtime JSON

- authoring YAML не исполняется runtime напрямую.
- сначала он компилируется в те же доменные объекты, что и обычный runtime JSON.
- после компиляции применяется тот же `JsonQuestValidator`, поэтому ограничения ссылок, типов узлов, эффектов и условий остаются одинаковыми.
- неизвестные поля в authoring YAML считаются ошибкой загрузки, чтобы опечатки не терялись молча.
