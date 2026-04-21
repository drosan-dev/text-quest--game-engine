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

        var builder = new StringBuilder($"Quest validation failed with {errors.Count} problem(s):");

        foreach (var group in errors.GroupBy(CreateGroupKey))
        {
            builder.AppendLine();
            builder.Append("- ");

            var groupHeader = BuildGroupHeader(group.First());
            builder.Append(groupHeader);

            foreach (var error in group)
            {
                builder.AppendLine();
                builder.Append("  * ");
                builder.Append(BuildIssue(error));
            }
        }

        return builder.ToString();
    }

    private static string CreateGroupKey(QuestValidationError error)
    {
        if (!string.IsNullOrWhiteSpace(error.NodeId) && !string.IsNullOrWhiteSpace(error.ChoiceId))
        {
            return $"choice:{error.NodeId}:{error.ChoiceId}";
        }

        if (!string.IsNullOrWhiteSpace(error.NodeId))
        {
            return $"node:{error.NodeId}";
        }

        return $"path:{error.Path}";
    }

    private static string BuildGroupHeader(QuestValidationError error)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(error.NodeId))
        {
            parts.Add($"nodeId='{error.NodeId}'");
        }

        if (!string.IsNullOrWhiteSpace(error.ChoiceId))
        {
            parts.Add($"choiceId='{error.ChoiceId}'");
        }

        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(error.Path))
        {
            parts.Add($"path={error.Path}");
        }

        return parts.Count == 0 ? "quest" : string.Join(", ", parts);
    }

    private static string BuildIssue(QuestValidationError error)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(error.Path))
        {
            parts.Add($"path={error.Path}");
        }

        parts.Add(error.Message);

        if (!string.IsNullOrWhiteSpace(error.Expected))
        {
            parts.Add($"expected={error.Expected}");
        }

        if (!string.IsNullOrWhiteSpace(error.Actual))
        {
            parts.Add($"actual={error.Actual}");
        }

        return string.Join("; ", parts);
    }
}
