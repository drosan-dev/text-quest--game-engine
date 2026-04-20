using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

public sealed record BranchNodeDefinition(
    string Id,
    IReadOnlyList<string> Text,
    IReadOnlyList<BranchDefinition> Branches,
    string DefaultNextNodeId)
    : NodeDefinition(Id, NodeType.Branch, Text);
