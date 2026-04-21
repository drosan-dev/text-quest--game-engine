using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

/// <summary>
/// Финальный узел, завершающий прохождение и фиксирующий результат.
/// </summary>
public sealed record EndNodeDefinition(
    string Id,
    IReadOnlyList<string> Text,
    string Result)
    : NodeDefinition(Id, NodeType.End, Text);
