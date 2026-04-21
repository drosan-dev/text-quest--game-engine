using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

/// <summary>
/// Узел, в котором игрок принимает решение между несколькими вариантами.
/// </summary>
public sealed record DecisionNodeDefinition(
    string Id,
    IReadOnlyList<string> Text,
    IReadOnlyList<ChoiceDefinition> Choices)
    : NodeDefinition(Id, NodeType.Decision, Text);
