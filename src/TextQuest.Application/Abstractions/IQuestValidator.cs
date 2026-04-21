using TextQuest.Domain.Models;
using TextQuest.Application.Models;

namespace TextQuest.Application.Abstractions;

/// <summary>
/// Проверяет целостность и корректность определения квеста.
/// </summary>
public interface IQuestValidator
{
    /// <summary>
    /// Возвращает результат валидации указанного квеста.
    /// </summary>
    QuestValidationResult Validate(QuestDefinition definition);
}
