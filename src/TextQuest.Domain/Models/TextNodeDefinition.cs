using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

public sealed record TextNodeDefinition(
    string Id,
    IReadOnlyList<string> Text,
    IReadOnlyList<ChoiceDefinition> Choices)
    : NodeDefinition(Id, NodeType.Text, Text);
