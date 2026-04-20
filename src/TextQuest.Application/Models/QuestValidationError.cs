namespace TextQuest.Application.Models;

public sealed record QuestValidationError(
    string Code,
    string Message,
    string? Path = null);
