namespace TextQuest.Frontends.Contracts;

/// <summary>
/// Представляет вариант выбора, доступный игроку в текущем состоянии.
/// </summary>
public sealed record ChoiceViewModel(
    string Id,
    string Text);
