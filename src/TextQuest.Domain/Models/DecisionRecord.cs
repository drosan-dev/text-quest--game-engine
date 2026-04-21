namespace TextQuest.Domain.Models;

/// <summary>
/// Фиксирует выбор, сделанный игроком в конкретном узле.
/// </summary>
public sealed record DecisionRecord(
    string NodeId,
    string ChoiceId);
