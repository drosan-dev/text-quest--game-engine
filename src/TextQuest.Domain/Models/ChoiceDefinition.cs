namespace TextQuest.Domain.Models;

/// <summary>
/// Описывает вариант выбора игрока и его последствия.
/// </summary>
public sealed record ChoiceDefinition(
    string Id,
    string Text,
    string NextNodeId,
    IReadOnlyList<ConditionDefinition>? Conditions = null,
    IReadOnlyList<EffectDefinition>? Effects = null);
