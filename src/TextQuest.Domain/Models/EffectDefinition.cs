namespace TextQuest.Domain.Models;

public sealed record EffectDefinition(
    string Type,
    string Target,
    object? Value);
