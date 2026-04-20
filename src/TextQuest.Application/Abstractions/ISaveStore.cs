using TextQuest.Domain.Models;

namespace TextQuest.Application.Abstractions;

public interface ISaveStore
{
    Task SaveAsync(GameState gameState, CancellationToken cancellationToken = default);
    Task<GameState?> LoadAsync(string saveId, CancellationToken cancellationToken = default);
}
