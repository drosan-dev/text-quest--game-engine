using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

/// <summary>
/// Узел автоматического ветвления, выбирающий следующий переход по условиям состояния.
/// </summary>
public sealed record BranchNodeDefinition(
    string Id,
    IReadOnlyList<string> Text,
    IReadOnlyList<BranchDefinition> Branches,
    string DefaultNextNodeId)
    : NodeDefinition(Id, NodeType.Branch, Text);
