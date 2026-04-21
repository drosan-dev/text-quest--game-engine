using System.Text;
using System.Text.RegularExpressions;
using TextQuest.Application.Abstractions;
using TextQuest.Domain.Models;

namespace TextQuest.Application;

/// <summary>
/// Раскрывает ссылки на текстовые пулы внутри шаблонов вида {{pool:poolId}} и стабилизирует результат в состоянии игры.
/// </summary>
internal sealed class TextPoolTemplateExpander
{
    private static readonly Regex TokenPattern = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

    private readonly IRandomProvider _randomProvider;

    public TextPoolTemplateExpander(IRandomProvider randomProvider)
    {
        _randomProvider = randomProvider ?? throw new ArgumentNullException(nameof(randomProvider));
    }

    public (string Template, GameState State) Expand(string template, QuestDefinition definition, GameState state, string scopeKey)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        var matches = TokenPattern.Matches(template);
        if (matches.Count == 0)
        {
            return (template, state);
        }

        var builder = new StringBuilder(template.Length);
        var lastIndex = 0;
        var poolTokenIndex = 0;
        Dictionary<string, int>? updatedSelections = null;

        foreach (Match match in matches)
        {
            builder.Append(template, lastIndex, match.Index - lastIndex);

            var token = match.Groups[1].Value.Trim();
            if (!TryParsePoolToken(token, out var poolId))
            {
                builder.Append(match.Value);
                lastIndex = match.Index + match.Length;
                continue;
            }

            if (!definition.TextPools.TryGetValue(poolId, out var pool))
            {
                throw new InvalidOperationException($"Text pool '{poolId}' referenced in template does not exist in quest '{definition.QuestId}'.");
            }

            if (pool.Items.Count == 0)
            {
                throw new InvalidOperationException($"Text pool '{poolId}' is empty in quest '{definition.QuestId}'.");
            }

            var selectionKey = $"{scopeKey}|{poolId}|{poolTokenIndex}";
            poolTokenIndex++;

            if (!state.RandomSelections.TryGetValue(selectionKey, out var selectedIndex))
            {
                selectedIndex = _randomProvider.NextInt(state.RandomSeed, selectionKey, 0, pool.Items.Count);
                updatedSelections ??= new Dictionary<string, int>(state.RandomSelections, StringComparer.Ordinal);
                updatedSelections[selectionKey] = selectedIndex;
            }

            builder.Append(pool.Items[selectedIndex]);
            lastIndex = match.Index + match.Length;
        }

        builder.Append(template, lastIndex, template.Length - lastIndex);

        if (updatedSelections is not null)
        {
            state = state with { RandomSelections = updatedSelections };
        }

        return (builder.ToString(), state);
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
