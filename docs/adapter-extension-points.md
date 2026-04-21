# Точки Расширения Адаптеров

## Цель

Подключать новый frontend-адаптер к движку без изменения runtime, JSON-loader и persistence.

## Публичный Контур Движка

Новый адаптер должен работать через следующие контракты:

- `TextQuest.Application.Abstractions.IQuestLoader`
- `TextQuest.Application.Abstractions.ITextQuestRuntime`
- `TextQuest.Application.Abstractions.ISaveStore`
- `TextQuest.Application.Models.RuntimeSession`
- `TextQuest.Domain.Models.GameState`
- `TextQuest.Frontends.Contracts.PresentableState`
- `TextQuest.Frontends.Contracts.ChoiceViewModel`

Этого набора достаточно для типового цикла:

1. Загрузить `QuestDefinition` через `IQuestLoader`.
2. Запустить новую сессию через `ITextQuestRuntime.StartNewGameAsync(...)`.
3. Рендерить `PresentableState` в нужном интерфейсе.
4. Передавать выбранный `choiceId` в `ITextQuestRuntime.ApplyChoiceAsync(...)`.
5. Сохранять `GameState` через `ISaveStore.SaveAsync(...)`.
6. Восстанавливать `GameState` через `ISaveStore.LoadAsync(...)` и `ITextQuestRuntime.RestoreAsync(...)`.

## Ответственность Адаптера

Адаптер должен:

- рендерить `PresentableState`
- принимать пользовательский ввод
- преобразовывать ввод в `choiceId` или служебную команду
- вызывать `ITextQuestRuntime`, `IQuestLoader` и `ISaveStore`
- показывать пользователю ошибки загрузки, валидации и восстановления

Адаптер не должен:

- вычислять conditions или effects
- самостоятельно резолвить `branch`-узлы
- менять правила переходов между узлами
- сериализовать `GameState` вне `ISaveStore`
- читать JSON квеста напрямую в обход `IQuestLoader`

## Точки Замены

Без изменения core можно заменить или добавить:

- новый loader, реализующий `IQuestLoader`, например для embedded-resource, HTTP или другого формата
- новый save-store, реализующий `ISaveStore`, например для базы данных или облачного backend
- новый frontend-адаптер, который рендерит `PresentableState` в Telegram, desktop UI или mobile UI

## Минимальный Сценарий Для Второго Адаптера

Telegram- или desktop-адаптеру достаточно повторить тот же orchestration flow, который уже использует CLI:

1. На старте выбрать источник квеста и создать реализации `IQuestLoader`, `ITextQuestRuntime`, `ISaveStore`.
2. При начале игры вызвать `StartNewGameAsync(...)`.
3. После каждого пользовательского действия вызывать `ApplyChoiceAsync(...)` с выбранным `choiceId`.
4. Для команд сохранения и загрузки использовать `SaveAsync(...)`, `LoadAsync(...)` и `RestoreAsync(...)`.
5. Всю UI-специфику ограничить отображением `PresentableState` и маппингом пользовательского ввода в `choiceId`.

## Что Считается Стабильным Для MVP

- runtime flow `start -> apply choice -> restore`
- модель `GameState` как сериализуемое внутреннее состояние
- модель `PresentableState` как единый формат данных для UI
- application-абстракции `IQuestLoader`, `ITextQuestRuntime`, `ISaveStore`

Второй адаптер пока не реализован, но текущий набор контрактов и тестов фиксирует, что core уже готов к его подключению.
