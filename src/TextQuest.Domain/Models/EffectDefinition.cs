namespace TextQuest.Domain.Models;

/// <summary>
/// Описывает изменение состояния, применяемое при выборе игрока.
/// </summary>
public sealed record EffectDefinition(
    string Type,
    string Target,
    object? Value);
