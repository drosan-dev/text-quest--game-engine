using TextQuest.Application.Abstractions;
using TextQuest.Domain.Models;

namespace TextQuest.Application;

/// <summary>
/// Represents a segment of a compiled template.
/// </summary>
internal enum SegmentType
{
    Literal,
    Variable,
    Conditional
}

/// <summary>
/// A segment in a compiled template.
/// </summary>
internal readonly struct TemplateSegment
{
    public SegmentType Type { get; }
    public string Content { get; }
    public IReadOnlyList<TemplateSegment>? ContentSegments { get; }
    public string? Name { get; }
    public bool Negated { get; }

    public TemplateSegment(SegmentType type, string content, IReadOnlyList<TemplateSegment>? contentSegments = null, string? name = null, bool negated = false)
    {
        Type = type;
        Content = content;
        ContentSegments = contentSegments;
        Name = name;
        Negated = negated;
    }
}

/// <summary>
/// Renders templated text by substituting variables and evaluating conditional blocks based on game state.
/// </summary>
public sealed class TextRenderer : ITextRenderer
{
    private const int MaxTemplateNestingDepth = 10;
    private const string VariableOpen = "{{";
    private const string VariableClose = "}}";
    private const string ConditionalOpen = "[if";
    private const string ConditionalClose = "[endif]";

    private readonly Dictionary<string, IReadOnlyList<TemplateSegment>> _templateCache = new();

    /// <summary>
    /// Compiles a template string into segments for efficient rendering.
    /// </summary>
    private IReadOnlyList<TemplateSegment> CompileTemplate(string template, int depth = 0)
    {
        if (depth > MaxTemplateNestingDepth)
        {
            throw new InvalidOperationException($"Template nesting depth exceeds maximum allowed depth of {MaxTemplateNestingDepth}.");
        }

        if (_templateCache.TryGetValue(template, out var cached))
        {
            return cached;
        }

        var compiled = CompileSegments(template, depth).AsReadOnly();
        _templateCache[template] = compiled;
        return compiled;
    }

    private List<TemplateSegment> CompileSegments(string template, int depth)
    {
        var segments = new List<TemplateSegment>();
        var index = 0;

        while (index < template.Length)
        {
            var nextVariable = template.IndexOf(VariableOpen, index, StringComparison.Ordinal);
            var nextConditional = template.IndexOf(ConditionalOpen, index, StringComparison.Ordinal);

            var nextToken = FindNextTokenIndex(nextVariable, nextConditional);
            if (nextToken < 0)
            {
                segments.Add(new TemplateSegment(SegmentType.Literal, template[index..]));
                break;
            }

            if (nextToken > index)
            {
                segments.Add(new TemplateSegment(SegmentType.Literal, template[index..nextToken]));
            }

            if (nextToken == nextVariable)
            {
                var closeIndex = template.IndexOf(VariableClose, nextVariable + VariableOpen.Length, StringComparison.Ordinal);
                if (closeIndex < 0)
                {
                    segments.Add(new TemplateSegment(SegmentType.Literal, template[nextVariable..]));
                    break;
                }

                var name = template.Substring(nextVariable + VariableOpen.Length, closeIndex - (nextVariable + VariableOpen.Length)).Trim();
                segments.Add(new TemplateSegment(SegmentType.Variable, string.Empty, null, name));
                index = closeIndex + VariableClose.Length;
                continue;
            }

            var conditionalHeaderClose = template.IndexOf(']', nextConditional);
            if (conditionalHeaderClose < 0)
            {
                segments.Add(new TemplateSegment(SegmentType.Literal, template[nextConditional..]));
                break;
            }

            var header = template.Substring(nextConditional + ConditionalOpen.Length, conditionalHeaderClose - (nextConditional + ConditionalOpen.Length)).Trim();
            if (!TryParseConditionalHeader(header, out var flagName, out var negated))
            {
                segments.Add(new TemplateSegment(SegmentType.Literal, template[nextConditional..(conditionalHeaderClose + 1)]));
                index = conditionalHeaderClose + 1;
                continue;
            }

            var contentStart = conditionalHeaderClose + 1;
            var conditionalCloseIndex = FindMatchingEndif(template, contentStart);
            if (conditionalCloseIndex < 0)
            {
                segments.Add(new TemplateSegment(SegmentType.Literal, template[nextConditional..]));
                break;
            }

            var content = template.Substring(contentStart, conditionalCloseIndex - contentStart);
            var contentSegments = CompileTemplate(content, depth + 1);
            segments.Add(new TemplateSegment(SegmentType.Conditional, string.Empty, contentSegments, flagName, negated));
            index = conditionalCloseIndex + ConditionalClose.Length;
        }

        return segments;
    }

    private static int FindNextTokenIndex(int variableIndex, int conditionalIndex)
    {
        if (variableIndex < 0)
        {
            return conditionalIndex;
        }

        if (conditionalIndex < 0)
        {
            return variableIndex;
        }

        return Math.Min(variableIndex, conditionalIndex);
    }

    private static bool TryParseConditionalHeader(string header, out string flagName, out bool negated)
    {
        // header is everything between "[if" and "]", e.g. "hasKey" or "!hasKey"
        var value = header.Trim();
        negated = false;

        if (value.StartsWith("!", StringComparison.Ordinal))
        {
            negated = true;
            value = value[1..].Trim();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            flagName = string.Empty;
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                flagName = string.Empty;
                return false;
            }
        }

        flagName = value;
        return true;
    }

    private static int FindMatchingEndif(string template, int startIndex)
    {
        var index = startIndex;
        var depth = 1;

        while (index < template.Length)
        {
            var nextIf = template.IndexOf(ConditionalOpen, index, StringComparison.Ordinal);
            var nextEndif = template.IndexOf(ConditionalClose, index, StringComparison.Ordinal);

            if (nextEndif < 0)
            {
                return -1;
            }

            if (nextIf >= 0 && nextIf < nextEndif)
            {
                depth++;
                index = nextIf + ConditionalOpen.Length;
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return nextEndif;
            }

            index = nextEndif + ConditionalClose.Length;
        }

        return -1;
    }

    /// <summary>
    /// Renders a list of template segments into a string.
    /// </summary>
    private void RenderSegments(IReadOnlyList<TemplateSegment> segments, GameState gameState, System.Text.StringBuilder result)
    {
        foreach (var segment in segments)
        {
            switch (segment.Type)
            {
                case SegmentType.Literal:
                    result.Append(segment.Content);
                    break;
                case SegmentType.Variable:
                    if (!gameState.Variables.TryGetValue(segment.Name!, out var value))
                    {
                        throw new InvalidOperationException($"Variable '{segment.Name}' referenced in template does not exist in game state.");
                    }
                    result.Append(value);
                    break;
                case SegmentType.Conditional:
                    if (!gameState.Flags.TryGetValue(segment.Name!, out var flagValue))
                    {
                        throw new InvalidOperationException($"Flag '{segment.Name}' referenced in template does not exist in game state.");
                    }
                    var conditionMet = segment.Negated ? !flagValue : flagValue;
                    if (conditionMet)
                    {
                        RenderSegments(segment.ContentSegments!, gameState, result);
                    }
                    break;
            }
        }
    }

    /// <inheritdoc />
    public string Render(string template, GameState gameState)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(gameState);

        var segments = CompileTemplate(template, 0);
        var result = new System.Text.StringBuilder();
        RenderSegments(segments, gameState, result);
        return result.ToString();
    }
}
