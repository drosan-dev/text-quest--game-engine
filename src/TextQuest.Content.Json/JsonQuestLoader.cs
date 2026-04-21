using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TextQuest.Application.Abstractions;
using TextQuest.Application.Models;
using TextQuest.Domain.Enums;
using TextQuest.Domain.Models;

namespace TextQuest.Content.Json;

/// <summary>
/// Загружает квест из JSON-файла и преобразует его в доменную модель.
/// </summary>
public sealed class JsonQuestLoader : IQuestLoader
{
    private static readonly EventId QuestLoadStartedEvent = new(1000, "QuestLoadStarted");
    private static readonly EventId QuestSourceReadEvent = new(1001, "QuestSourceRead");
    private static readonly EventId QuestJsonMalformedEvent = new(1002, "QuestJsonMalformed");
    private static readonly EventId QuestMappingFailedEvent = new(1003, "QuestMappingFailed");
    private static readonly EventId QuestValidationFailedEvent = new(1004, "QuestValidationFailed");
    private static readonly EventId QuestLoadedEvent = new(1005, "QuestLoaded");
    private static readonly EventId QuestLoadReadFailedEvent = new(1006, "QuestLoadReadFailed");

    private readonly JsonQuestValidator _validator = new();
    private readonly ILogger<JsonQuestLoader> _logger;

    public JsonQuestLoader(ILogger<JsonQuestLoader>? logger = null)
    {
        _logger = logger ?? NullLogger<JsonQuestLoader>.Instance;
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

        _logger.LogInformation(QuestLoadStartedEvent, "Loading quest definition from {QuestSource}", source);

        string json;
        try
        {
            json = await File.ReadAllTextAsync(source, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(QuestLoadReadFailedEvent, exception, "Failed to read quest source {QuestSource}", source);
            throw;
        }
        catch (IOException exception)
        {
            _logger.LogError(QuestLoadReadFailedEvent, exception, "Failed to read quest source {QuestSource}", source);
            throw;
        }

        _logger.LogDebug(QuestSourceReadEvent, "Read quest source {QuestSource} with {CharacterCount} characters", source, json.Length);

        QuestDocumentDto? document;

        try
        {
            document = JsonSerializer.Deserialize<QuestDocumentDto>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(QuestJsonMalformedEvent, exception, "Quest source {QuestSource} contains malformed JSON at {JsonPath}", source, CreateJsonPath(exception.Path));
            throw new QuestValidationException(
            [
                new QuestValidationError(
                    "json.syntax",
                    exception.Message,
                    CreateJsonPath(exception.Path))
            ]);
        }

        if (document is null)
        {
            _logger.LogWarning(QuestJsonMalformedEvent, "Quest source {QuestSource} is empty after deserialization", source);
            throw new QuestValidationException(
            [
                new QuestValidationError("json.empty", "Quest JSON document is empty.", "$")
            ]);
        }

        var mappingErrors = new List<QuestValidationError>();
        var nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);
        var nodeDtos = document.Nodes ?? [];

        for (var nodeIndex = 0; nodeIndex < nodeDtos.Count; nodeIndex++)
        {
            var nodeDto = nodeDtos[nodeIndex];
            var nodePath = $"nodes[{nodeIndex}]";

            var nodeId = nodeDto.Id?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                mappingErrors.Add(new QuestValidationError(
                    "node.id.required",
                    "Node id is required.",
                    nodePath + ".id"));
                continue;
            }

            if (!TryParseNode(nodeDto, nodePath, out var node, out var nodeErrors))
            {
                mappingErrors.AddRange(nodeErrors);
                continue;
            }

            if (!nodes.TryAdd(nodeId, node!))
            {
                mappingErrors.Add(new QuestValidationError(
                    "node.id.duplicate",
                    $"Node id '{nodeId}' must be unique.",
                    nodePath + ".id"));
            }
        }

        if (mappingErrors.Count > 0)
        {
            _logger.LogWarning(QuestMappingFailedEvent, "Quest source {QuestSource} failed mapping with {ErrorCount} errors", source, mappingErrors.Count);
            throw new QuestValidationException(mappingErrors);
        }

        var textPools = ParseTextPools(document.TextPools, mappingErrors);

        if (mappingErrors.Count > 0)
        {
            _logger.LogWarning(QuestMappingFailedEvent, "Quest source {QuestSource} failed mapping with {ErrorCount} errors", source, mappingErrors.Count);
            throw new QuestValidationException(mappingErrors);
        }

        var definition = new QuestDefinition(
            document.QuestId?.Trim() ?? string.Empty,
            document.Version?.Trim() ?? string.Empty,
            document.Title?.Trim() ?? string.Empty,
            document.StartNodeId?.Trim() ?? string.Empty,
            document.Variables ?? new Dictionary<string, int>(StringComparer.Ordinal),
            document.Flags ?? new Dictionary<string, bool>(StringComparer.Ordinal),
            textPools,
            nodes);

        var validationResult = _validator.Validate(definition);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning(QuestValidationFailedEvent, "Quest {QuestId} version {QuestVersion} failed validation with {ErrorCount} errors", definition.QuestId, definition.Version, validationResult.Errors.Count);
            throw new QuestValidationException(validationResult.Errors);
        }

        _logger.LogInformation(QuestLoadedEvent, "Loaded quest {QuestId} version {QuestVersion} with {NodeCount} nodes from {QuestSource}", definition.QuestId, definition.Version, definition.Nodes.Count, source);

        return definition;
    }

    private static bool TryParseNode(
        NodeDto nodeDto,
        string nodePath,
        out NodeDefinition? node,
        out IReadOnlyList<QuestValidationError> errors)
    {
        var localErrors = new List<QuestValidationError>();
        var nodeId = nodeDto.Id?.Trim() ?? string.Empty;
        var nodeTypeValue = nodeDto.Type?.Trim();
        var text = nodeDto.Text?.Where(line => line is not null).Select(line => line!.Trim()).ToArray() ?? [];

        if (!Enum.TryParse<NodeType>(nodeTypeValue, ignoreCase: true, out var nodeType))
        {
            localErrors.Add(new QuestValidationError(
                "node.type.invalid",
                $"Node type '{nodeDto.Type}' is not supported.",
                nodePath + ".type"));

            node = null;
            errors = localErrors;
            return false;
        }

        node = nodeType switch
        {
            NodeType.Text => new TextNodeDefinition(nodeId, text, ParseChoices(nodeDto.Choices, nodePath + ".choices", localErrors)),
            NodeType.Decision => new DecisionNodeDefinition(nodeId, text, ParseChoices(nodeDto.Choices, nodePath + ".choices", localErrors)),
            NodeType.Branch => new BranchNodeDefinition(
                nodeId,
                text,
                ParseBranches(nodeDto.Branches, nodePath + ".branches", localErrors),
                nodeDto.DefaultNextNodeId?.Trim() ?? string.Empty),
            NodeType.End => new EndNodeDefinition(nodeId, text, nodeDto.Result?.Trim() ?? string.Empty),
            _ => null,
        };

        errors = localErrors;
        return true;
    }

    private static IReadOnlyList<ChoiceDefinition> ParseChoices(
        List<ChoiceDto>? choiceDtos,
        string path,
        List<QuestValidationError> errors)
    {
        if (choiceDtos is null)
        {
            return [];
        }

        var choices = new List<ChoiceDefinition>(choiceDtos.Count);

        for (var index = 0; index < choiceDtos.Count; index++)
        {
            var dto = choiceDtos[index];
            choices.Add(new ChoiceDefinition(
                dto.Id?.Trim() ?? string.Empty,
                dto.Text?.Trim() ?? string.Empty,
                dto.NextNodeId?.Trim() ?? string.Empty,
                ParseConditions(dto.Conditions, $"{path}[{index}].conditions", errors),
                ParseEffects(dto.Effects, $"{path}[{index}].effects", errors)));
        }

        return choices;
    }

    private static IReadOnlyList<BranchDefinition> ParseBranches(
        List<BranchDto>? branchDtos,
        string path,
        List<QuestValidationError> errors)
    {
        if (branchDtos is null)
        {
            return [];
        }

        var branches = new List<BranchDefinition>(branchDtos.Count);

        for (var index = 0; index < branchDtos.Count; index++)
        {
            var dto = branchDtos[index];
            branches.Add(new BranchDefinition(
                ParseConditions(dto.Conditions, $"{path}[{index}].conditions", errors),
                dto.NextNodeId?.Trim() ?? string.Empty));
        }

        return branches;
    }

    private static IReadOnlyList<ConditionDefinition> ParseConditions(
        List<ConditionDto>? conditionDtos,
        string path,
        List<QuestValidationError> errors)
    {
        if (conditionDtos is null)
        {
            return [];
        }

        var conditions = new List<ConditionDefinition>(conditionDtos.Count);

        for (var index = 0; index < conditionDtos.Count; index++)
        {
            var dto = conditionDtos[index];
            conditions.Add(new ConditionDefinition(
                dto.Target?.Trim() ?? string.Empty,
                dto.Operator?.Trim() ?? string.Empty,
                ConvertJsonValue(dto.Value, $"{path}[{index}].value", errors)));
        }

        return conditions;
    }

    private static IReadOnlyList<EffectDefinition> ParseEffects(
        List<EffectDto>? effectDtos,
        string path,
        List<QuestValidationError> errors)
    {
        if (effectDtos is null)
        {
            return [];
        }

        var effects = new List<EffectDefinition>(effectDtos.Count);

        for (var index = 0; index < effectDtos.Count; index++)
        {
            var dto = effectDtos[index];
            effects.Add(new EffectDefinition(
                dto.Type?.Trim() ?? string.Empty,
                dto.Target?.Trim() ?? string.Empty,
                ConvertJsonValue(dto.Value, $"{path}[{index}].value", errors)));
        }

        return effects;
    }

    private static object? ConvertJsonValue(JsonElement? value, string path, List<QuestValidationError> errors)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.False => false,
            JsonValueKind.True => true,
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number when value.Value.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when value.Value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.Value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => value.Value.GetDouble(),
            _ => AddUnsupportedValueError(path, errors, value.Value)
        };
    }

    private static object? AddUnsupportedValueError(string path, List<QuestValidationError> errors, JsonElement value)
    {
        errors.Add(new QuestValidationError(
            "json.value.unsupported",
            $"Unsupported JSON value kind '{value.ValueKind}'. Expected primitive value.",
            path));

        return null;
    }

    private static string CreateJsonPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "$" : path;
    }

    private static IReadOnlyDictionary<string, TextPoolDefinition> ParseTextPools(
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
            var poolPath = $"$.textPools['{poolIdRaw?.Replace("'", "\\'", StringComparison.Ordinal) ?? string.Empty}']";

            if (string.IsNullOrWhiteSpace(poolId))
            {
                errors.Add(new QuestValidationError(
                    "text_pool.id.required",
                    "Text pool id must not be empty.",
                    poolPath));
                continue;
            }

            if (itemsRaw is null)
            {
                errors.Add(new QuestValidationError(
                    "text_pool.items.required",
                    $"Text pool '{poolId}' must be an array of strings.",
                    poolPath));
                continue;
            }

            var items = new List<string>(itemsRaw.Count);
            for (var index = 0; index < itemsRaw.Count; index++)
            {
                var value = itemsRaw[index]?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add(new QuestValidationError(
                        "text_pool.item.required",
                        $"Text pool '{poolId}' must contain only non-empty strings.",
                        $"{poolPath}[{index}]"));
                    continue;
                }

                items.Add(value);
            }

            if (!result.TryAdd(poolId, new TextPoolDefinition(poolId, items)))
            {
                errors.Add(new QuestValidationError(
                    "text_pool.id.duplicate",
                    $"Text pool id '{poolId}' must be unique.",
                    poolPath));
            }
        }

        return result;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class QuestDocumentDto
    {
        public string? QuestId { get; init; }

        public string? Version { get; init; }

        public string? Title { get; init; }

        public string? StartNodeId { get; init; }

        public Dictionary<string, int>? Variables { get; init; }

        public Dictionary<string, bool>? Flags { get; init; }

        public Dictionary<string, List<string?>?>? TextPools { get; init; }

        public List<NodeDto>? Nodes { get; init; }
    }

    private sealed class NodeDto
    {
        public string? Id { get; init; }

        public string? Type { get; init; }

        public List<string?>? Text { get; init; }

        public List<ChoiceDto>? Choices { get; init; }

        public List<BranchDto>? Branches { get; init; }

        public string? DefaultNextNodeId { get; init; }

        public string? Result { get; init; }
    }

    private sealed class ChoiceDto
    {
        public string? Id { get; init; }

        public string? Text { get; init; }

        public string? NextNodeId { get; init; }

        public List<ConditionDto>? Conditions { get; init; }

        public List<EffectDto>? Effects { get; init; }
    }

    private sealed class BranchDto
    {
        public List<ConditionDto>? Conditions { get; init; }

        public string? NextNodeId { get; init; }
    }

    private sealed class ConditionDto
    {
        public string? Target { get; init; }

        public string? Operator { get; init; }

        public JsonElement? Value { get; init; }
    }

    private sealed class EffectDto
    {
        public string? Type { get; init; }

        public string? Target { get; init; }

        public JsonElement? Value { get; init; }
    }
}
