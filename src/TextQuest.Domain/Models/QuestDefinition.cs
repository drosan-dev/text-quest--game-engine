namespace TextQuest.Domain.Models;

/// <summary>
/// Описывает полный сценарий квеста, доступный для загрузки и исполнения.
/// </summary>
public sealed record QuestDefinition(
    string QuestId,
    string Version,
    string Title,
    string StartNodeId,
    IReadOnlyDictionary<string, int> InitialVariables,
    IReadOnlyDictionary<string, bool> InitialFlags,
    IReadOnlyDictionary<string, TextPoolDefinition> TextPools,
    IReadOnlyDictionary<string, NodeDefinition> Nodes);
