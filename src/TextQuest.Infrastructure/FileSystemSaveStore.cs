using TextQuest.Application.Abstractions;
using TextQuest.Domain.Models;

namespace TextQuest.Infrastructure;

public sealed class FileSystemSaveStore : ISaveStore
{
    public Task SaveAsync(GameState gameState, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("File-based saves are implemented in MVP stage 5.");
    }

    public Task<GameState?> LoadAsync(string saveId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("File-based saves are implemented in MVP stage 5.");
    }
}
