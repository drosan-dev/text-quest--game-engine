# Authoring YAML: timeline-кампания (дни → персонажи → локации)

Этот формат предназначен для “необычного” текстового квеста, где:

- есть хронология по дням (`day`);
- каждый день игрок выбирает персонажа;
- персонаж ходит по локациям (каждая локация — отдельный YAML-файл);
- текст и действия зависят от флагов/переменных (глобальных и персонажных).

Технически этот authoring-слой компилируется в существующий runtime-контракт квеста (`QuestDefinition`) и исполняется тем же движком.

## 1) Структура кампании

Кампания — это файл `campaign.yml` + папка `locations/` рядом с ним.

Пример структуры:

```
content/timeline/demo-campaign/
  campaign.yml
  locations/
    home.yml
    abandoned_house.yml
```

### 1.1 `campaign.yml`

Минимальный каркас:

```yaml
questId: demo_timeline
version: 1.0.0
title: "Demo: дни, персонажи, локации"
days: 3

vars:
  day: 1

flags:
  world_door_open: false

characters:
  - id: alice
    name: Алиса
    startLocation: home
```

Поля:

- `questId`, `version`, `title` — как в обычном квесте.
- `days` — сколько дней в кампании (после превышения — завершение).
- `vars` — переменные (числа). Обязательны:
  - `day` — текущий день (обычно `1`).
- `flags` — булевы флаги. Здесь же объявляются и “ачивки”:
  - глобальные (например `world_door_open`);
  - персонажные (например `char_alice_hangover_next_morning`).
- `characters[]` — список персонажей.

Персонаж:

- `id` — идентификатор (латиница/цифры/`_`).
- `name` — имя, которое увидит игрок.
- `startLocation` — id локации, с которой начинается день для этого персонажа.
- `hangoverText` (опционально) — текст “похмелья”, если активен флаг `char_<id>_hangover_next_morning`.

### 1.2 Персональные/глобальные “ачивки”

Рекомендуемое именование (это обычные `flags`):

- глобальные: `world_<something>`
- персонажные: `char_<characterId>_<something>`

Примеры:

- `world_door_open`
- `char_alice_drank_today`
- `char_alice_hangover_next_morning`
- `char_alice_selected` (служебный флаг: выбран ли персонаж на текущий день)

Примечание: для удобства MVP loader автоматически добавляет служебные флаги `char_<id>_selected`, `char_<id>_drank_today`, `char_<id>_hangover_next_morning`, если они не объявлены в `flags`.

## 2) Формат локации

Каждая локация — отдельный файл `locations/<id>.yml`:

```yaml
id: home
title: Дом

entries:
  - id: default
    text:
      - "Дом. День {{day}}."
    actions:
      - id: go_abandoned_house
        text: "Пойти в заброшку"
        goto: abandoned_house
```

### 2.1 `entries[]`: текст + действия с условиями

Локация содержит `entries[]` — варианты состояния локации.

- У `entry` может быть `when` (условие). Если условие выполняется — entry может стать активным.
- Если `when` не указан — это “fallback entry” (по умолчанию).
- Если несколько `entry` подходят, выбирается первый по порядку, поэтому располагайте более специфичные выше.

Поле `text` может быть строкой или списком строк.

### 2.2 `actions[]`: выборы игрока

Каждое действие:

- `id` — стабильный id выбора.
- `text` — текст выбора.
- `when` (опционально) — условие доступности действия.
- `do` (опционально) — эффекты (изменение `vars`/`flags`).
- `goto` (опционально) — переход:
  - `<locationId>` — перейти в другую локацию;
  - `end_day` — закончить день.
  - если `goto` не задан — остаёмся в текущей локации (переоценка `entries` после `do`).

## 3) Синтаксис `when` и `do`

Это тот же “мини-язык”, что и в обычном authoring YAML:

### 3.1 `when`

- флаг должен быть `true`:
```yaml
when: world_door_open
```

- флаг должен быть `false`:
```yaml
when: '!world_door_open'
```

- сравнение переменной:
```yaml
when: day >= 2
```

- несколько условий списком (все должны выполниться):
```yaml
when:
  - day >= 2
  - '!world_door_open'
```

### 3.2 `do`

- установить флаг:
```yaml
do: world_door_open = true
```

- установить переменную:
```yaml
do: day = 3
```

- увеличить/уменьшить переменную:
```yaml
do: day += 1
```

- несколько эффектов списком:
```yaml
do:
  - world_door_open = true
  - day += 1
```

## 4) Встроенная логика “похмелья” в MVP

Если в конце дня выбранный персонаж имеет флаг `char_<id>_drank_today = true`, то при завершении дня:

- `char_<id>_drank_today` сбрасывается в `false`
- `char_<id>_hangover_next_morning` выставляется в `true`

На старте дня выбранного персонажа, если `char_<id>_hangover_next_morning = true`, показывается `hangoverText` (или дефолтный текст), и флаг сбрасывается в `false`.
