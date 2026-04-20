using TextQuest.Application.Models;

namespace TextQuest.Application.Models;

public sealed record QuestValidationResult(
    bool IsValid,
    IReadOnlyList<QuestValidationError> Errors)
{
    public static QuestValidationResult Success() => new(true, Array.Empty<QuestValidationError>());

    public static QuestValidationResult Failure(IEnumerable<QuestValidationError> errors)
    {
        return new(false, errors.ToArray());
    }
}
