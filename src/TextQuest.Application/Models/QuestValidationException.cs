using System.Text;

namespace TextQuest.Application.Models;

public sealed class QuestValidationException : Exception
{
    public QuestValidationException(IReadOnlyList<QuestValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

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
