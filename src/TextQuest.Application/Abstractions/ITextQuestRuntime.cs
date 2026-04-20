using TextQuest.Domain.Models;
using TextQuest.Frontends.Contracts;

namespace TextQuest.Application.Abstractions;

public interface ITextQuestRuntime
{
    Task<PresentableState> StartNewGameAsync(QuestDefinition definition, CancellationToken cancellationToken = default);
    Task<PresentableState> ApplyChoiceAsync(GameState gameState, string choiceId, CancellationToken cancellationToken = default);
    Task<PresentableState> RestoreAsync(QuestDefinition definition, GameState gameState, CancellationToken cancellationToken = default);
}
