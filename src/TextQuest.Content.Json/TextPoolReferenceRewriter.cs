using System.Text;
using System.Text.RegularExpressions;

namespace TextQuest.Content.Json;

internal static class TextPoolReferenceRewriter
{
    private static readonly Regex TokenPattern = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

    public static string Rewrite(string template, IReadOnlyDictionary<string, string> localToGlobalPoolIds)
    {
        if (string.IsNullOrEmpty(template) || localToGlobalPoolIds.Count == 0)
        {
            return template;
        }

        var matches = TokenPattern.Matches(template);
        if (matches.Count == 0)
        {
            return template;
        }

        var builder = new StringBuilder(template.Length);
        var lastIndex = 0;

        foreach (Match match in matches)
        {
            builder.Append(template, lastIndex, match.Index - lastIndex);

            var token = match.Groups[1].Value.Trim();
            if (TryParsePoolToken(token, out var poolId) && localToGlobalPoolIds.TryGetValue(poolId, out var globalId))
            {
                builder.Append("{{pool:");
                builder.Append(globalId);
                builder.Append("}}");
            }
            else
            {
                builder.Append(match.Value);
            }

            lastIndex = match.Index + match.Length;
        }

        builder.Append(template, lastIndex, template.Length - lastIndex);
        return builder.ToString();
    }

    public static IReadOnlyList<string> Rewrite(IReadOnlyList<string> templates, IReadOnlyDictionary<string, string> localToGlobalPoolIds)
    {
        if (templates.Count == 0 || localToGlobalPoolIds.Count == 0)
        {
            return templates;
        }

        var rewritten = new string[templates.Count];
        for (var index = 0; index < templates.Count; index++)
        {
            rewritten[index] = Rewrite(templates[index], localToGlobalPoolIds);
        }

        return rewritten;
    }

    private static bool TryParsePoolToken(string token, out string poolId)
    {
        if (token.StartsWith("pool:", StringComparison.OrdinalIgnoreCase))
        {
            poolId = token["pool:".Length..].Trim();
            return !string.IsNullOrWhiteSpace(poolId);
        }

        poolId = string.Empty;
        return false;
    }
}
