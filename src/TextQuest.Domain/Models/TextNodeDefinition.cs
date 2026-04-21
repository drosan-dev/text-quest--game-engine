using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

/// <summary>
/// Узел с текстом повествования и явным набором доступных переходов.
/// </summary>
public sealed record TextNodeDefinition(
    string Id,
    IReadOnlyList<string> Text,
    IReadOnlyList<ChoiceDefinition> Choices)
    : NodeDefinition(Id, NodeType.Text, Text);
