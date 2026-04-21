namespace TextQuest.Domain.Models;

/// <summary>
/// Описывает пул текстовых вариантов, из которого runtime может выбрать один вариант.
/// </summary>
public sealed record TextPoolDefinition(
    string Id,
    IReadOnlyList<string> Items);
