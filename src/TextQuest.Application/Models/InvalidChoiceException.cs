namespace TextQuest.Application.Models;

/// <summary>
/// Выбрасывается, когда запрошенный вариант недоступен в текущем узле квеста.
/// </summary>
public sealed class InvalidChoiceException : Exception
{
    /// <summary>
    /// Инициализирует исключение для недопустимого выбора.
    /// </summary>
    public InvalidChoiceException(string choiceId, string nodeId)
        : base($"Choice '{choiceId}' is not available in node '{nodeId}'.")
    {
        ChoiceId = choiceId;
        NodeId = nodeId;
    }

    /// <summary>
    /// Идентификатор недоступного выбора.
    /// </summary>
    public string ChoiceId { get; }

    /// <summary>
    /// Идентификатор узла, в котором был запрошен выбор.
    /// </summary>
    public string NodeId { get; }
}
