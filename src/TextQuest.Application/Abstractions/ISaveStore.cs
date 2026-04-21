using TextQuest.Domain.Models;

namespace TextQuest.Application.Abstractions;

/// <summary>
/// Предоставляет операции сохранения и восстановления состояния игры.
/// </summary>
public interface ISaveStore
{
    /// <summary>
    /// Сохраняет состояние игры в постоянное хранилище.
    /// </summary>
    Task SaveAsync(GameState gameState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает ранее сохранённое состояние игры по идентификатору.
    /// </summary>
    Task<GameState?> LoadAsync(string saveId, CancellationToken cancellationToken = default);
}
