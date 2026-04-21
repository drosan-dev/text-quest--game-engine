using TextQuest.Domain.Models;

namespace TextQuest.Application.Abstractions;

/// <summary>
/// Загружает определение квеста из внешнего источника.
/// </summary>
public interface IQuestLoader
{
    /// <summary>
    /// Читает и десериализует квест из указанного источника.
    /// </summary>
    Task<QuestDefinition> LoadAsync(string source, CancellationToken cancellationToken = default);
}
