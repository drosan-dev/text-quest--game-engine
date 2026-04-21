using TextQuest.Application.Abstractions;
using TextQuest.Domain.Models;

namespace TextQuest.Infrastructure;

/// <summary>
/// Заглушка файлового хранилища сохранений до реализации следующего этапа MVP.
/// </summary>
public sealed class FileSystemSaveStore : ISaveStore
{
    /// <inheritdoc />
    public Task SaveAsync(GameState gameState, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("File-based saves are implemented in MVP stage 5.");
    }

    /// <inheritdoc />
    public Task<GameState?> LoadAsync(string saveId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("File-based saves are implemented in MVP stage 5.");
    }
}
