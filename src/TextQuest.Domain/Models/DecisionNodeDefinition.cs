using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

public sealed record DecisionNodeDefinition(
    string Id,
    IReadOnlyList<string> Text,
    IReadOnlyList<ChoiceDefinition> Choices)
    : NodeDefinition(Id, NodeType.Decision, Text);
