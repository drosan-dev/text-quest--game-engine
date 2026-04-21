using TextQuest.Application.Models;

namespace TextQuest.Application.Models;

/// <summary>
/// Содержит итог проверки определения квеста.
/// </summary>
public sealed record QuestValidationResult(
    bool IsValid,
    IReadOnlyList<QuestValidationError> Errors)
{
    /// <summary>
    /// Создаёт успешный результат без ошибок.
    /// </summary>
    public static QuestValidationResult Success() => new(true, Array.Empty<QuestValidationError>());

    /// <summary>
    /// Создаёт неуспешный результат с набором ошибок.
    /// </summary>
    public static QuestValidationResult Failure(IEnumerable<QuestValidationError> errors)
    {
        return new(false, errors.ToArray());
    }
}
