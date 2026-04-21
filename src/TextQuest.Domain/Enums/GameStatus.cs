namespace TextQuest.Domain.Enums;

/// <summary>
/// Отражает текущее состояние прохождения квеста.
/// </summary>
public enum GameStatus
{
    /// <summary>
    /// Квест находится в процессе прохождения.
    /// </summary>
    InProgress,

    /// <summary>
    /// Квест завершён и больше не принимает выборы.
    /// </summary>
    Completed,
}
