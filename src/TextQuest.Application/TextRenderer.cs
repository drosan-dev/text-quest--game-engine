using System.Text.RegularExpressions;
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

    private static readonly Regex VariablePattern = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);
    private static readonly Regex ConditionalPattern = new(@"\[if\s+(!?)(\w+)\](.*?)\[endif\]", RegexOptions.Compiled | RegexOptions.Singleline);

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

        var segments = new List<TemplateSegment>();
        var remaining = template;
        var offset = 0;

        // Process conditionals first (outermost)
        var conditionalMatches = ConditionalPattern.Matches(template);
        foreach (Match match in conditionalMatches)
        {
            // Add literal before the match
            if (match.Index > offset)
            {
                segments.Add(new TemplateSegment(SegmentType.Literal, template[offset..match.Index]));
            }

            var negated = match.Groups[1].Value == "!";
            var flagName = match.Groups[2].Value;
            var content = match.Groups[3].Value;
            var contentSegments = CompileTemplate(content, depth + 1); // Recursively compile content
            segments.Add(new TemplateSegment(SegmentType.Conditional, string.Empty, contentSegments, flagName, negated));

            offset = match.Index + match.Length;
        }

        // Add remaining literal
        if (offset < template.Length)
        {
            remaining = template[offset..];
        }
        else
        {
            remaining = string.Empty;
        }

        // Now process variables in the remaining text
        var variableMatches = VariablePattern.Matches(remaining);
        offset = 0;
        foreach (Match match in variableMatches)
        {
            // Add literal before the match
            if (match.Index > offset)
            {
                segments.Add(new TemplateSegment(SegmentType.Literal, remaining[offset..match.Index]));
            }

            var variableName = match.Groups[1].Value;
            segments.Add(new TemplateSegment(SegmentType.Variable, string.Empty, null, variableName));

            offset = match.Index + match.Length;
        }

        // Add final literal
        if (offset < remaining.Length)
        {
            segments.Add(new TemplateSegment(SegmentType.Literal, remaining[offset..]));
        }

        var compiled = segments.AsReadOnly();
        _templateCache[template] = compiled;
        return compiled;
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