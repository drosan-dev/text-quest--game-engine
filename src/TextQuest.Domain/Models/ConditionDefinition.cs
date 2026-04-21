namespace TextQuest.Domain.Models;

/// <summary>
/// Описывает условие, проверяемое по текущему состоянию игры.
/// </summary>
public sealed record ConditionDefinition(
    string Target,
    string Operator,
    object? Value);
