namespace TextQuest.Domain.Models;

/// <summary>
/// Описывает условную ветку автоматического перехода между узлами.
/// </summary>
public sealed record BranchDefinition(
    IReadOnlyList<ConditionDefinition> Conditions,
    string NextNodeId);
