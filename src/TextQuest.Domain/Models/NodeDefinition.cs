using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

/// <summary>
/// Базовый тип для всех узлов сценария квеста.
/// </summary>
public abstract record NodeDefinition(string Id, NodeType Type, IReadOnlyList<string> Text);
