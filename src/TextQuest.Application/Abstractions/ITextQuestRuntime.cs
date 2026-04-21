using TextQuest.Domain.Models;
using TextQuest.Application.Models;

namespace TextQuest.Application.Abstractions;

/// <summary>
/// Выполняет сценарий текстового квеста поверх определения квеста и текущего состояния игры.
/// </summary>
public interface ITextQuestRuntime
{
    /// <summary>
    /// Создаёт новую игровую сессию из определения квеста.
    /// </summary>
    Task<RuntimeSession> StartNewGameAsync(QuestDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Применяет выбранный вариант и возвращает обновлённую сессию.
    /// </summary>
    Task<RuntimeSession> ApplyChoiceAsync(QuestDefinition definition, GameState gameState, string choiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Восстанавливает игровую сессию из ранее сохранённого состояния.
    /// </summary>
    Task<RuntimeSession> RestoreAsync(QuestDefinition definition, GameState gameState, CancellationToken cancellationToken = default);
}
