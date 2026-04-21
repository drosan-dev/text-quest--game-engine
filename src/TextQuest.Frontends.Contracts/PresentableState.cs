namespace TextQuest.Frontends.Contracts;

/// <summary>
/// Содержит состояние квеста в виде, готовом к отображению пользователю.
/// </summary>
public sealed record PresentableState(
    string QuestId,
    string Title,
    string CurrentNodeId,
    IReadOnlyList<string> TextBlocks,
    IReadOnlyList<ChoiceViewModel> Choices,
    bool IsCompleted,
    string? Result = null);
