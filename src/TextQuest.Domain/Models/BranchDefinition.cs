namespace TextQuest.Domain.Models;

public sealed record BranchDefinition(
    IReadOnlyList<ConditionDefinition> Conditions,
    string NextNodeId);
