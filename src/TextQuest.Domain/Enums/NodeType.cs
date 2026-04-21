namespace TextQuest.Domain.Enums;

/// <summary>
/// Определяет разновидность узла сценария квеста.
/// </summary>
public enum NodeType
{
    /// <summary>
    /// Текстовый узел с набором пользовательских выборов.
    /// </summary>
    Text,

    /// <summary>
    /// Узел принятия решения игроком.
    /// </summary>
    Decision,

    /// <summary>
    /// Узел автоматического перехода по условиям.
    /// </summary>
    Branch,

    /// <summary>
    /// Финальный узел сценария.
    /// </summary>
    End,
}
