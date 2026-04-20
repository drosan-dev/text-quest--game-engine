namespace TextQuest.Domain.Models;

public sealed record QuestDefinition(
    string QuestId,
    string Version,
    string Title,
    string StartNodeId,
    IReadOnlyDictionary<string, int> InitialVariables,
    IReadOnlyDictionary<string, bool> InitialFlags,
    IReadOnlyDictionary<string, NodeDefinition> Nodes);
