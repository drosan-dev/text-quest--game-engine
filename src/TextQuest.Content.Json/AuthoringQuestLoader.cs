using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TextQuest.Application.Abstractions;
using TextQuest.Application.Models;
using TextQuest.Domain.Enums;
using TextQuest.Domain.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TextQuest.Content.Json;

/// <summary>
/// Загружает упрощенный authoring YAML и компилирует его в доменную модель квеста.
/// </summary>
public sealed class AuthoringQuestLoader : IQuestLoader
{
    private static readonly Regex ComparisonExpressionRegex = new(
        "^(?<target>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<operator>==|!=|>=|<=|>|<)\\s*(?<value>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EffectExpressionRegex = new(
        "^(?<target>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<operator>\\+=|-=|=)\\s*(?<value>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly EventId QuestLoadStartedEvent = new(1100, "AuthoringQuestLoadStarted");
    private static readonly EventId QuestSourceReadEvent = new(1101, "AuthoringQuestSourceRead");
    private static readonly EventId QuestYamlMalformedEvent = new(1102, "AuthoringQuestYamlMalformed");
    private static readonly EventId QuestCompilationFailedEvent = new(1103, "AuthoringQuestCompilationFailed");
    private static readonly EventId QuestValidationFailedEvent = new(1104, "AuthoringQuestValidationFailed");
    private static readonly EventId QuestLoadedEvent = new(1105, "AuthoringQuestLoaded");
    private static readonly EventId QuestLoadReadFailedEvent = new(1106, "AuthoringQuestLoadReadFailed");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly JsonQuestValidator _validator = new();
    private readonly ILogger<AuthoringQuestLoader> _logger;

    public AuthoringQuestLoader(ILogger<AuthoringQuestLoader>? logger = null)
    {
        _logger = logger ?? NullLogger<AuthoringQuestLoader>.Instance;
    }

    /// <inheritdoc />
    public Task<QuestDefinition> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        return LoadCoreAsync(source, cancellationToken);
    }

    private async Task<QuestDefinition> LoadCoreAsync(string source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Quest source path must not be empty.", nameof(source));
        }

        _logger.LogInformation(QuestLoadStartedEvent, "Loading authoring quest definition from {QuestSource}", source);

        string content;
        try
        {
            content = await File.ReadAllTextAsync(source, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(QuestLoadReadFailedEvent, exception, "Failed to read authoring quest source {QuestSource}", source);
            throw;
        }
        catch (IOException exception)
        {
            _logger.LogError(QuestLoadReadFailedEvent, exception, "Failed to read authoring quest source {QuestSource}", source);
            throw;
        }

        _logger.LogDebug(QuestSourceReadEvent, "Read authoring quest source {QuestSource} with {CharacterCount} characters", source, content.Length);

        AuthoringQuestDocumentDto? document;
        try
        {
            document = Deserializer.Deserialize<AuthoringQuestDocumentDto>(content);
        }
        catch (YamlException exception)
        {
            _logger.LogWarning(QuestYamlMalformedEvent, exception, "Authoring quest source {QuestSource} contains malformed YAML", source);
            throw new QuestValidationException(
            [
                new QuestValidationError(
                    "authoring.syntax",
                    exception.Message,
                    "$")
            ]);
        }

        if (document is null)
        {
            _logger.LogWarning(QuestYamlMalformedEvent, "Authoring quest source {QuestSource} is empty after deserialization", source);
            throw new QuestValidationException(
            [
                new QuestValidationError("authoring.empty", "Authoring quest document is empty.", "$")
            ]);
        }

        var compilationErrors = new List<QuestValidationError>();
        var definition = CompileDocument(document, compilationErrors);
        if (compilationErrors.Count > 0)
        {
            _logger.LogWarning(QuestCompilationFailedEvent, "Authoring quest source {QuestSource} failed compilation with {ErrorCount} errors", source, compilationErrors.Count);
            throw new QuestValidationException(compilationErrors);
        }

        var validationResult = _validator.Validate(definition);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning(QuestValidationFailedEvent, "Authoring quest {QuestId} version {QuestVersion} failed validation with {ErrorCount} errors", definition.QuestId, definition.Version, validationResult.Errors.Count);
            throw new QuestValidationException(validationResult.Errors);
        }

        _logger.LogInformation(QuestLoadedEvent, "Loaded authoring quest {QuestId} version {QuestVersion} with {NodeCount} nodes from {QuestSource}", definition.QuestId, definition.Version, definition.Nodes.Count, source);
        return definition;
    }

    private static QuestDefinition CompileDocument(AuthoringQuestDocumentDto document, List<QuestValidationError> errors)
    {
        var variables = document.Vars ?? document.Variables ?? new Dictionary<string, int>(StringComparer.Ordinal);
        var flags = document.Flags ?? new Dictionary<string, bool>(StringComparer.Ordinal);
        var textPools = CompileTextPools(document.TextPools, errors);
        var scenes = document.Scenes ?? new Dictionary<string, AuthoringSceneDto>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);

        foreach (var (sceneId, sceneDto) in scenes)
        {
            var scenePath = $"$.scenes.{sceneId}";
            if (sceneDto is null)
            {
                errors.Add(new QuestValidationError("scene.required", $"Scene '{sceneId}' must be an object.", scenePath));
                continue;
            }

            var node = CompileScene(sceneId, sceneDto, variables, flags, textPools, errors, scenePath);
            if (node is null)
            {
                continue;
            }

            if (!nodes.TryAdd(sceneId, node))
            {
                errors.Add(new QuestValidationError("scene.id.duplicate", $"Scene id '{sceneId}' must be unique.", scenePath));
            }
        }

        return new QuestDefinition(
            document.QuestId?.Trim() ?? string.Empty,
            document.Version?.Trim() ?? string.Empty,
            document.Title?.Trim() ?? string.Empty,
            (document.Start ?? document.StartNodeId)?.Trim() ?? string.Empty,
            variables,
            flags,
            textPools,
            nodes);
    }

    private static Dictionary<string, TextPoolDefinition> CompileTextPools(
        Dictionary<string, List<string?>?>? pools,
        List<QuestValidationError> errors)
    {
        if (pools is null || pools.Count == 0)
        {
            return new Dictionary<string, TextPoolDefinition>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, TextPoolDefinition>(StringComparer.Ordinal);

        foreach (var (poolIdRaw, itemsRaw) in pools)
        {
            var poolId = (poolIdRaw ?? string.Empty).Trim();
            var poolPath = $"$.textPools.{poolIdRaw}";

            if (string.IsNullOrWhiteSpace(poolId))
            {
                errors.Add(new QuestValidationError("text_pool.id.required", "Text pool id must not be empty.", poolPath));
                continue;
            }

            if (itemsRaw is null)
            {
                errors.Add(new QuestValidationError("text_pool.items.required", $"Text pool '{poolId}' must be an array of strings.", poolPath));
                continue;
            }

            var items = new List<string>(itemsRaw.Count);
            for (var index = 0; index < itemsRaw.Count; index++)
            {
                var value = itemsRaw[index]?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add(new QuestValidationError("text_pool.item.required", $"Text pool '{poolId}' must contain only non-empty strings.", $"{poolPath}[{index}]"));
                    continue;
                }

                items.Add(value);
            }

            if (!result.TryAdd(poolId, new TextPoolDefinition(poolId, items)))
            {
                errors.Add(new QuestValidationError("text_pool.id.duplicate", $"Text pool id '{poolId}' must be unique.", poolPath));
            }
        }

        return result;
    }

    private static NodeDefinition? CompileScene(
        string sceneId,
        AuthoringSceneDto sceneDto,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        Dictionary<string, TextPoolDefinition> textPools,
        List<QuestValidationError> errors,
        string path)
    {
        var nodeType = ResolveNodeType(sceneDto, path, errors);
        if (nodeType is null)
        {
            return null;
        }

        var localPoolIdMap = BuildLocalTextPoolIdMap(sceneId, sceneDto.TextPools, path + ".textPools", textPools, errors);
        var text = TextPoolReferenceRewriter.Rewrite(ParseText(sceneDto.Text, path + ".text", errors), localPoolIdMap);

        return nodeType.Value switch
        {
            NodeType.Text => new TextNodeDefinition(sceneId, text, RewriteChoices(CompileChoices(sceneId, sceneDto.Choices, variables, flags, errors, path + ".choices"), localPoolIdMap)),
            NodeType.Decision => new DecisionNodeDefinition(sceneId, text, RewriteChoices(CompileChoices(sceneId, sceneDto.Choices, variables, flags, errors, path + ".choices"), localPoolIdMap)),
            NodeType.Branch => new BranchNodeDefinition(
                sceneId,
                text,
                CompileBranches(sceneDto.Branches, variables, flags, errors, path + ".branches"),
                (sceneDto.Default ?? sceneDto.DefaultNextNodeId)?.Trim() ?? string.Empty),
            NodeType.End => new EndNodeDefinition(sceneId, text, sceneDto.Result?.Trim() ?? string.Empty),
            _ => null,
        };
    }

    private static IReadOnlyList<ChoiceDefinition> RewriteChoices(IReadOnlyList<ChoiceDefinition> choices, IReadOnlyDictionary<string, string> localPoolIdMap)
    {
        if (choices.Count == 0 || localPoolIdMap.Count == 0)
        {
            return choices;
        }

        return choices
            .Select(choice => choice with { Text = TextPoolReferenceRewriter.Rewrite(choice.Text, localPoolIdMap) })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> BuildLocalTextPoolIdMap(
        string sceneId,
        Dictionary<string, List<string?>?>? localPools,
        string path,
        Dictionary<string, TextPoolDefinition> globalPools,
        List<QuestValidationError> errors)
    {
        if (localPools is null || localPools.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (poolIdRaw, itemsRaw) in localPools)
        {
            var poolId = (poolIdRaw ?? string.Empty).Trim();
            var poolPath = $"{path}.{poolIdRaw}";

            if (string.IsNullOrWhiteSpace(poolId))
            {
                errors.Add(new QuestValidationError("text_pool.id.required", "Text pool id must not be empty.", poolPath, NodeId: sceneId));
                continue;
            }

            var globalId = $"{sceneId}.{poolId}";
            if (!idMap.TryAdd(poolId, globalId))
            {
                errors.Add(new QuestValidationError("text_pool.id.duplicate", $"Text pool id '{poolId}' must be unique within scene.", poolPath, NodeId: sceneId));
                continue;
            }

            if (itemsRaw is null)
            {
                errors.Add(new QuestValidationError("text_pool.items.required", $"Text pool '{poolId}' must be an array of strings.", poolPath, NodeId: sceneId));
                continue;
            }

            var items = new List<string>(itemsRaw.Count);
            for (var index = 0; index < itemsRaw.Count; index++)
            {
                var value = itemsRaw[index]?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add(new QuestValidationError("text_pool.item.required", $"Text pool '{poolId}' must contain only non-empty strings.", $"{poolPath}[{index}]", NodeId: sceneId));
                    continue;
                }

                items.Add(value);
            }

            if (items.Count == 0)
            {
                continue;
            }

            if (globalPools.ContainsKey(globalId))
            {
                errors.Add(new QuestValidationError("text_pool.id.duplicate", $"Text pool id '{globalId}' must be unique.", poolPath, NodeId: sceneId));
                continue;
            }

            globalPools[globalId] = new TextPoolDefinition(globalId, items);
        }

        return idMap;
    }

    private static NodeType? ResolveNodeType(AuthoringSceneDto sceneDto, string path, List<QuestValidationError> errors)
    {
        var explicitType = sceneDto.Type?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitType))
        {
            if (Enum.TryParse<NodeType>(explicitType, ignoreCase: true, out var nodeType))
            {
                return nodeType;
            }

            errors.Add(new QuestValidationError("scene.type.invalid", $"Scene type '{sceneDto.Type}' is not supported.", path + ".type"));
            return null;
        }

        if (!string.IsNullOrWhiteSpace(sceneDto.Result))
        {
            return NodeType.End;
        }

        if (sceneDto.Branches is { Count: > 0 } || !string.IsNullOrWhiteSpace(sceneDto.Default) || !string.IsNullOrWhiteSpace(sceneDto.DefaultNextNodeId))
        {
            return NodeType.Branch;
        }

        if (sceneDto.Choices is { Count: > 0 })
        {
            return NodeType.Text;
        }

        errors.Add(new QuestValidationError(
            "scene.type.unresolved",
            "Scene type could not be inferred. Add 'type' or provide fields matching text/decision, branch, or end scene.",
            path));
        return null;
    }

    private static IReadOnlyList<string> ParseText(object? value, string path, List<QuestValidationError> errors)
    {
        if (value is null)
        {
            return [];
        }

        if (value is string text)
        {
            return [text.Trim()];
        }

        if (value is IEnumerable<object?> sequence)
        {
            var items = new List<string>();
            var index = 0;
            foreach (var item in sequence)
            {
                if (item is not string line)
                {
                    errors.Add(new QuestValidationError("scene.text.invalid", "Scene text array must contain only strings.", $"{path}[{index}]"));
                }
                else
                {
                    items.Add(line.Trim());
                }

                index++;
            }

            return items;
        }

        errors.Add(new QuestValidationError("scene.text.invalid", "Scene text must be a string or an array of strings.", path));
        return [];
    }

    private static IReadOnlyList<ChoiceDefinition> CompileChoices(
        string sceneId,
        List<AuthoringChoiceDto?>? choices,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        List<QuestValidationError> errors,
        string path)
    {
        if (choices is null)
        {
            return [];
        }

        var compiled = new List<ChoiceDefinition>(choices.Count);

        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            if (choice is null)
            {
                errors.Add(new QuestValidationError("choice.required", "Choice entry must be an object.", $"{path}[{index}]"));
                continue;
            }

            var choicePath = $"{path}[{index}]";
            if (string.IsNullOrWhiteSpace(choice.Id))
            {
                errors.Add(new QuestValidationError(
                    "choice.id.required",
                    "Choice id is required in authoring YAML to keep save/load and decision history stable across content edits.",
                    choicePath + ".id",
                    NodeId: sceneId));
                continue;
            }

            var choiceId = choice.Id.Trim();

            compiled.Add(new ChoiceDefinition(
                choiceId,
                choice.Text?.Trim() ?? string.Empty,
                choice.Goto?.Trim() ?? choice.NextNodeId?.Trim() ?? string.Empty,
                CompileConditions(choice.When, variables, flags, errors, choicePath + ".when"),
                CompileEffects(choice.Do, variables, flags, errors, choicePath + ".do")));
        }

        return compiled;
    }

    private static IReadOnlyList<BranchDefinition> CompileBranches(
        List<AuthoringBranchDto?>? branches,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        List<QuestValidationError> errors,
        string path)
    {
        if (branches is null)
        {
            return [];
        }

        var compiled = new List<BranchDefinition>(branches.Count);
        for (var index = 0; index < branches.Count; index++)
        {
            var branch = branches[index];
            if (branch is null)
            {
                errors.Add(new QuestValidationError("branch.required", "Branch entry must be an object.", $"{path}[{index}]"));
                continue;
            }

            var branchPath = $"{path}[{index}]";
            compiled.Add(new BranchDefinition(
                CompileConditions(branch.When, variables, flags, errors, branchPath + ".when"),
                branch.Goto?.Trim() ?? branch.NextNodeId?.Trim() ?? string.Empty));
        }

        return compiled;
    }

    private static IReadOnlyList<ConditionDefinition> CompileConditions(
        object? value,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        List<QuestValidationError> errors,
        string path)
    {
        var expressions = ParseExpressionList(value, path, errors, "condition list");
        var compiled = new List<ConditionDefinition>(expressions.Count);

        for (var index = 0; index < expressions.Count; index++)
        {
            var expressionPath = $"{path}[{index}]";
            var condition = CompileConditionExpression(expressions[index], variables, flags, expressionPath, errors);
            if (condition is not null)
            {
                compiled.Add(condition);
            }
        }

        return compiled;
    }

    private static IReadOnlyList<EffectDefinition> CompileEffects(
        object? value,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        List<QuestValidationError> errors,
        string path)
    {
        var expressions = ParseExpressionList(value, path, errors, "effect list");
        var compiled = new List<EffectDefinition>(expressions.Count);

        for (var index = 0; index < expressions.Count; index++)
        {
            var expressionPath = $"{path}[{index}]";
            var effect = CompileEffectExpression(expressions[index], variables, flags, expressionPath, errors);
            if (effect is not null)
            {
                compiled.Add(effect);
            }
        }

        return compiled;
    }

    private static List<string> ParseExpressionList(object? value, string path, List<QuestValidationError> errors, string fieldDescription)
    {
        var expressions = new List<string>();
        if (value is null)
        {
            return expressions;
        }

        if (value is string expression)
        {
            expressions.Add(expression.Trim());
            return expressions;
        }

        if (value is IEnumerable<object?> sequence)
        {
            var index = 0;
            foreach (var item in sequence)
            {
                if (item is not string line)
                {
                    errors.Add(new QuestValidationError("authoring.expression.invalid", $"{fieldDescription} must contain only strings.", $"{path}[{index}]"));
                }
                else
                {
                    expressions.Add(line.Trim());
                }

                index++;
            }

            return expressions;
        }

        errors.Add(new QuestValidationError("authoring.expression.invalid", $"{fieldDescription} must be a string or array of strings.", path));
        return expressions;
    }

    private static ConditionDefinition? CompileConditionExpression(
        string expression,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        string path,
        List<QuestValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            errors.Add(new QuestValidationError("condition.expression.empty", "Condition expression must not be empty.", path));
            return null;
        }

        if (expression.StartsWith('!'))
        {
            return new ConditionDefinition(expression[1..].Trim(), "==", false);
        }

        var match = ComparisonExpressionRegex.Match(expression);
        if (match.Success)
        {
            var target = match.Groups["target"].Value.Trim();
            var @operator = match.Groups["operator"].Value;
            var literal = match.Groups["value"].Value.Trim();
            return new ConditionDefinition(target, @operator, ParseLiteralValue(target, literal, variables, flags));
        }

        if (flags.ContainsKey(expression))
        {
            return new ConditionDefinition(expression, "==", true);
        }

        if (variables.ContainsKey(expression))
        {
            errors.Add(new QuestValidationError("condition.expression.invalid", $"Condition shorthand '{expression}' is only supported for flags. Use an explicit comparison for variables.", path));
            return null;
        }

        return new ConditionDefinition(expression, "==", true);
    }

    private static EffectDefinition? CompileEffectExpression(
        string expression,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        string path,
        List<QuestValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            errors.Add(new QuestValidationError("effect.expression.empty", "Effect expression must not be empty.", path));
            return null;
        }

        var match = EffectExpressionRegex.Match(expression);
        if (!match.Success)
        {
            errors.Add(new QuestValidationError("effect.expression.invalid", $"Effect expression '{expression}' is not supported.", path));
            return null;
        }

        var target = match.Groups["target"].Value.Trim();
        var @operator = match.Groups["operator"].Value;
        var literal = match.Groups["value"].Value.Trim();
        var parsedValue = ParseLiteralValue(target, literal, variables, flags);

        return @operator switch
        {
            "=" => parsedValue is bool
                ? new EffectDefinition("set_flag", target, parsedValue)
                : new EffectDefinition("set_variable", target, parsedValue),
            "+=" => CreateAddEffect(target, parsedValue, path, errors),
            "-=" => CreateSubtractEffect(target, parsedValue, path, errors),
            _ => null,
        };
    }

    private static EffectDefinition? CreateAddEffect(string target, object? value, string path, List<QuestValidationError> errors)
    {
        if (value is not int intValue)
        {
            errors.Add(new QuestValidationError("effect.expression.invalid", "Operator '+=' requires an integer value.", path));
            return null;
        }

        return new EffectDefinition("add", target, intValue);
    }

    private static EffectDefinition? CreateSubtractEffect(string target, object? value, string path, List<QuestValidationError> errors)
    {
        if (value is not int intValue)
        {
            errors.Add(new QuestValidationError("effect.expression.invalid", "Operator '-=' requires an integer value.", path));
            return null;
        }

        return new EffectDefinition("add", target, -intValue);
    }

    private static object? ParseLiteralValue(
        string target,
        string literal,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags)
    {
        if (flags.ContainsKey(target) && bool.TryParse(literal, out var boolValue))
        {
            return boolValue;
        }

        if (variables.ContainsKey(target) && int.TryParse(literal, out var intValue))
        {
            return intValue;
        }

        if (bool.TryParse(literal, out boolValue))
        {
            return boolValue;
        }

        if (int.TryParse(literal, out intValue))
        {
            return intValue;
        }

        return literal.Trim('"', '\'');
    }

    private sealed class AuthoringQuestDocumentDto
    {
        public string? QuestId { get; init; }

        public string? Version { get; init; }

        public string? Title { get; init; }

        public string? Start { get; init; }

        public string? StartNodeId { get; init; }

        public Dictionary<string, int>? Vars { get; init; }

        public Dictionary<string, int>? Variables { get; init; }

        public Dictionary<string, bool>? Flags { get; init; }

        public Dictionary<string, List<string?>?>? TextPools { get; init; }

        public Dictionary<string, AuthoringSceneDto>? Scenes { get; init; }
    }

    private sealed class AuthoringSceneDto
    {
        public string? Type { get; init; }

        public object? Text { get; init; }

        public Dictionary<string, List<string?>?>? TextPools { get; init; }

        public List<AuthoringChoiceDto?>? Choices { get; init; }

        public List<AuthoringBranchDto?>? Branches { get; init; }

        public string? Default { get; init; }

        public string? DefaultNextNodeId { get; init; }

        public string? Result { get; init; }
    }

    private sealed class AuthoringChoiceDto
    {
        public string? Id { get; init; }

        public string? Text { get; init; }

        public string? Goto { get; init; }

        public string? NextNodeId { get; init; }

        public object? When { get; init; }

        public object? Do { get; init; }
    }

    private sealed class AuthoringBranchDto
    {
        public object? When { get; init; }

        public string? Goto { get; init; }

        public string? NextNodeId { get; init; }
    }
}
