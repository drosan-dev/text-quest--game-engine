using TextQuest.Domain.Models;
using TextQuest.Frontends.Contracts;

namespace TextQuest.Application.Models;

/// <summary>
/// Объединяет внутреннее состояние игры и его представление для фронтенда.
/// </summary>
public sealed record RuntimeSession(
    GameState GameState,
    PresentableState PresentableState);
