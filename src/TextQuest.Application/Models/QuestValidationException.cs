using System.Text;

namespace TextQuest.Application.Models;

/// <summary>
/// Представляет ошибку загрузки или проверки квеста с полным набором найденных нарушений.
/// </summary>
public sealed class QuestValidationException : Exception
{
    /// <summary>
    /// Инициализирует исключение списком ошибок валидации.
    /// </summary>
    public QuestValidationException(IReadOnlyList<QuestValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>
    /// Ошибки, обнаруженные при разборе или валидации квеста.
    /// </summary>
    public IReadOnlyList<QuestValidationError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<QuestValidationError> errors)
    {
        if (errors.Count == 0)
        {
            return "Quest validation failed.";
        }

        var builder = new StringBuilder("Quest validation failed:");

        foreach (var error in errors)
        {
            builder.AppendLine();
            builder.Append("- ");

            if (!string.IsNullOrWhiteSpace(error.Path))
            {
                builder.Append(error.Path);
                builder.Append(": ");
            }

            builder.Append(error.Message);
        }

        return builder.ToString();
    }
}
