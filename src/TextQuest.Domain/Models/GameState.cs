using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

public sealed record GameState(
    string QuestId,
    string QuestVersion,
    string CurrentNodeId,
    IReadOnlyDictionary<string, int> Variables,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyList<string> VisitedNodeIds,
    IReadOnlyList<DecisionRecord> DecisionHistory,
    GameStatus Status);
