namespace TextQuest.Domain.Models;

public sealed record ConditionDefinition(
    string Target,
    string Operator,
    object? Value);
