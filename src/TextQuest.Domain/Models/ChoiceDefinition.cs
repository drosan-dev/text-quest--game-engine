namespace TextQuest.Domain.Models;

public sealed record ChoiceDefinition(
    string Id,
    string Text,
    string NextNodeId,
    IReadOnlyList<ConditionDefinition>? Conditions = null,
    IReadOnlyList<EffectDefinition>? Effects = null);
