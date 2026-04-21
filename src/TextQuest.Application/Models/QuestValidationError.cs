namespace TextQuest.Application.Models;

/// <summary>
/// Описывает отдельную проблему, найденную при проверке квеста.
/// </summary>
public sealed record QuestValidationError(
    string Code,
    string Message,
    string? Path = null,
    string? NodeId = null,
    string? ChoiceId = null,
    string? Expected = null,
    string? Actual = null);
