namespace TextQuest.Frontends.Contracts;

public sealed record PresentableState(
    string QuestId,
    string Title,
    string CurrentNodeId,
    IReadOnlyList<string> TextBlocks,
    IReadOnlyList<ChoiceViewModel> Choices,
    bool IsCompleted,
    string? Result = null);
