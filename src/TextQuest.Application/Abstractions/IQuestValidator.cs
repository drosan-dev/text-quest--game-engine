using TextQuest.Domain.Models;
using TextQuest.Application.Models;

namespace TextQuest.Application.Abstractions;

public interface IQuestValidator
{
    QuestValidationResult Validate(QuestDefinition definition);
}
