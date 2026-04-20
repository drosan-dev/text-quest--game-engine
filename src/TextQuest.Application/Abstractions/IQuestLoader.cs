using TextQuest.Domain.Models;

namespace TextQuest.Application.Abstractions;

public interface IQuestLoader
{
    Task<QuestDefinition> LoadAsync(string source, CancellationToken cancellationToken = default);
}
