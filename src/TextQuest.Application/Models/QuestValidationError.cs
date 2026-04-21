namespace TextQuest.Application.Models;

/// <summary>
/// Описывает отдельную проблему, найденную при проверке квеста.
/// </summary>
public sealed record QuestValidationError(
    string Code,
    string Message,
    string? Path = null);
