using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

public sealed record EndNodeDefinition(
    string Id,
    IReadOnlyList<string> Text,
    string Result)
    : NodeDefinition(Id, NodeType.End, Text);
