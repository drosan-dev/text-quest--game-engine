# JSON-формат квеста

Связанные документы:

- `docs/README.md` - карта документации.
- `docs/current-state.md` - текущее состояние реализации, использующей этот контракт.
- `docs/authoring-quest-format.md` - упрощенный authoring-слой, который компилируется в этот контракт.
- `docs/technical-vision.md` - место формата в общей архитектуре.
- `docs/mvp-plan.md` - этапы, в которых формат вводился и расширялся.

Этот документ фиксирует поддерживаемый runtime JSON-формат квеста. Он описывает фактический контракт, который понимают `TextQuest.Content.Json`, runtime и CLI после загрузки или компиляции authoring-слоя.

## Верхний Уровень

```json
{
  "questId": "demo_cell",
  "version": "1.0.0",
  "title": "Пробуждение в камере",
  "startNodeId": "intro",
  "variables": {
    "resolve": 0
  },
  "flags": {
    "hasKey": false
  },
  "nodes": []
}
```

Поддерживаемые поля:

- `questId` - обязательный непустой идентификатор квеста.
- `version` - обязательная непустая версия квеста.
- `title` - обязательный непустой заголовок.
- `startNodeId` - обязательный идентификатор стартового узла.
- `variables` - опциональный объект `string -> int`.
- `flags` - опциональный объект `string -> bool`.
- `nodes` - обязательный массив узлов, минимум один элемент.

## Типы Узлов

- `text` - показывает текст и список выборов.
- `decision` - показывает текст и список выборов; для MVP отличается семантически, но использует тот же контракт choices.
- `branch` - автоматическое ветвление по условиям.
- `end` - финальный узел с результатом.

## Общая Форма Узла

```json
{
  "id": "intro",
  "type": "text",
  "text": [
    "Первая строка.",
    "Вторая строка."
  ]
}
```

Общие правила:

- `id` обязателен и должен быть уникален в пределах квеста.
- `type` обязателен и должен быть одним из поддерживаемых значений.
- `text` обязателен для `text`, `decision` и `end` узлов.
- Для `branch` узла `text` допустим, но не обязателен.

## Узлы `text` И `decision`

```json
{
  "id": "corridor_decision",
  "type": "decision",
  "text": [
    "Шаги в коридоре затихают."
  ],
  "choices": [
    {
      "id": "unlock_door",
      "text": "Попробовать открыть дверь ключом",
      "conditions": [
        {
          "target": "hasKey",
          "operator": "==",
          "value": true
        }
      ],
      "effects": [
        {
          "type": "set_flag",
          "target": "hasKey",
          "value": true
        }
      ],
      "nextNodeId": "outcome_branch"
    }
  ]
}
```

Правила:

- `choices` обязателен и должен содержать минимум один элемент.
- `choice.id` обязателен и уникален в пределах узла.
- `choice.text` обязателен.
- `choice.nextNodeId` обязателен и должен ссылаться на существующий узел.
- `conditions` и `effects` опциональны.

## Узел `branch`

```json
{
  "id": "outcome_branch",
  "type": "branch",
  "branches": [
    {
      "conditions": [
        {
          "target": "hasKey",
          "operator": "==",
          "value": true
        }
      ],
      "nextNodeId": "escape"
    }
  ],
  "defaultNextNodeId": "captured"
}
```

Правила:

- `branches` обязателен и должен содержать минимум одну ветку.
- Каждая ветка должна содержать минимум одно условие.
- `branch.nextNodeId` обязателен и должен ссылаться на существующий узел.
- `defaultNextNodeId` обязателен и должен ссылаться на существующий узел.

## Узел `end`

```json
{
  "id": "escape",
  "type": "end",
  "text": [
    "Замок поддается, и вы бесшумно исчезаете в коридоре."
  ],
  "result": "victory"
}
```

Правила:

- `result` обязателен и должен быть непустой строкой.

## Формат `conditions`

```json
{
  "target": "resolve",
  "operator": ">=",
  "value": 1
}
```

Поддержка зависит от типа target:

- если `target` указывает на variable, `value` должен быть integer
- если `target` указывает на flag, `value` должен быть boolean

Поддерживаемые операторы для variables:

- `==`
- `!=`
- `>`
- `>=`
- `<`
- `<=`

Поддерживаемые операторы для flags:

- `==`
- `!=`

`target` должен ссылаться на имя, объявленное в `variables` или `flags` верхнего уровня.

## Формат `effects`

```json
{
  "type": "add",
  "target": "resolve",
  "value": 1
}
```

Поддерживаемые эффекты для variables:

- `set_variable`
- `add`

Поддерживаемые эффекты для flags:

- `set_flag`

Правила:

- `target` обязателен и должен ссылаться на объявленную variable или flag.
- для `set_variable` и `add` значение должно быть integer.
- для `set_flag` значение должно быть boolean.

## Пример Полного Квеста

Актуальный рабочий пример лежит в `content/quests/demo-quest.json`.

Если нужен более короткий авторский синтаксис, использовать `docs/authoring-quest-format.md` и `content/quests/demo-quest.author.yml`.

## Примечания По Валидации

Валидатор дополнительно отклоняет:

- пустые имена variables и flags
- отсутствующий `startNodeId`
- ссылки на несуществующие узлы
- дубли `choice.id` внутри одного узла
- неподдерживаемые типы узлов, операторов и эффектов
- JSON-значения `value`, которые не являются примитивами
