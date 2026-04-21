using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TextQuest.Application.Abstractions;
using TextQuest.Application.Models;
using TextQuest.Domain.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TextQuest.Content.Json;

/// <summary>
/// Загружает timeline-campaign YAML (campaign.yml + locations/*.yml) и компилирует в существующий runtime-контракт квеста.
/// </summary>
public sealed class TimelineCampaignLoader : IQuestLoader
{
    private static readonly Regex ComparisonExpressionRegex = new(
        "^(?<target>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<operator>==|!=|>=|<=|>|<)\\s*(?<value>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EffectExpressionRegex = new(
        "^(?<target>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<operator>\\+=|-=|=)\\s*(?<value>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly EventId CampaignLoadStartedEvent = new(1200, "TimelineCampaignLoadStarted");
    private static readonly EventId CampaignYamlMalformedEvent = new(1201, "TimelineCampaignYamlMalformed");
    private static readonly EventId CampaignCompilationFailedEvent = new(1202, "TimelineCampaignCompilationFailed");
    private static readonly EventId CampaignValidationFailedEvent = new(1203, "TimelineCampaignValidationFailed");
    private static readonly EventId CampaignLoadedEvent = new(1204, "TimelineCampaignLoaded");
    private static readonly EventId CampaignLoadReadFailedEvent = new(1205, "TimelineCampaignLoadReadFailed");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly JsonQuestValidator _validator = new();
    private readonly ILogger<TimelineCampaignLoader> _logger;

    public TimelineCampaignLoader(ILogger<TimelineCampaignLoader>? logger = null)
    {
        _logger = logger ?? NullLogger<TimelineCampaignLoader>.Instance;
    }

    public Task<QuestDefinition> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        return LoadCoreAsync(source, cancellationToken);
    }

    private async Task<QuestDefinition> LoadCoreAsync(string source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Campaign source path must not be empty.", nameof(source));
        }

        _logger.LogInformation(CampaignLoadStartedEvent, "Loading timeline campaign definition from {CampaignSource}", source);

        string content;
        try
        {
            content = await File.ReadAllTextAsync(source, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(CampaignLoadReadFailedEvent, exception, "Failed to read timeline campaign source {CampaignSource}", source);
            throw;
        }
        catch (IOException exception)
        {
            _logger.LogError(CampaignLoadReadFailedEvent, exception, "Failed to read timeline campaign source {CampaignSource}", source);
            throw;
        }

        CampaignDto? campaign;
        try
        {
            campaign = Deserializer.Deserialize<CampaignDto>(content);
        }
        catch (YamlException exception)
        {
            _logger.LogWarning(CampaignYamlMalformedEvent, exception, "Timeline campaign source {CampaignSource} contains malformed YAML", source);
            throw new QuestValidationException(
            [
                new QuestValidationError(
                    "timeline.syntax",
                    exception.Message,
                    "$")
            ]);
        }

        if (campaign is null)
        {
            _logger.LogWarning(CampaignYamlMalformedEvent, "Timeline campaign source {CampaignSource} is empty after deserialization", source);
            throw new QuestValidationException(
            [
                new QuestValidationError("timeline.empty", "Timeline campaign document is empty.", "$")
            ]);
        }

        var compilationErrors = new List<QuestValidationError>();
        var definition = await CompileCampaignAsync(source, campaign, compilationErrors, cancellationToken);
        if (compilationErrors.Count > 0)
        {
            _logger.LogWarning(CampaignCompilationFailedEvent, "Timeline campaign source {CampaignSource} failed compilation with {ErrorCount} errors", source, compilationErrors.Count);
            throw new QuestValidationException(compilationErrors);
        }

        var validationResult = _validator.Validate(definition);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning(CampaignValidationFailedEvent, "Timeline campaign {QuestId} version {QuestVersion} failed validation with {ErrorCount} errors", definition.QuestId, definition.Version, validationResult.Errors.Count);
            throw new QuestValidationException(validationResult.Errors);
        }

        _logger.LogInformation(CampaignLoadedEvent, "Loaded timeline campaign {QuestId} version {QuestVersion} with {NodeCount} nodes from {CampaignSource}", definition.QuestId, definition.Version, definition.Nodes.Count, source);
        return definition;
    }

    private static async Task<QuestDefinition> CompileCampaignAsync(
        string source,
        CampaignDto campaign,
        List<QuestValidationError> errors,
        CancellationToken cancellationToken)
    {
        var campaignDir = Path.GetDirectoryName(Path.GetFullPath(source));
        if (campaignDir is null)
        {
            errors.Add(new QuestValidationError("timeline.path.invalid", $"Cannot resolve campaign directory for '{source}'.", "$"));
            return EmptyDefinition(campaign);
        }

        var questId = RequireString(campaign.QuestId, "$.questId", errors);
        var version = RequireString(campaign.Version, "$.version", errors);
        var title = RequireString(campaign.Title, "$.title", errors);
        var days = RequireInt(campaign.Days, "$.days", errors, minValue: 1);

        var variables = campaign.Vars ?? new Dictionary<string, int>(StringComparer.Ordinal);
        var flags = campaign.Flags ?? new Dictionary<string, bool>(StringComparer.Ordinal);

        RequireVar(variables, "day", "$.vars", errors);

        var characters = campaign.Characters ?? [];
        if (characters.Count == 0)
        {
            errors.Add(new QuestValidationError("timeline.characters.empty", "Campaign must contain at least one character.", "$.characters"));
        }

        var characterIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            var path = $"$.characters[{index}]";

            if (character is null)
            {
                errors.Add(new QuestValidationError("timeline.character.required", "Character must be an object.", path));
                continue;
            }

            var characterId = RequireString(character.Id, path + ".id", errors);
            RequireString(character.Name, path + ".name", errors);
            RequireString(character.StartLocation, path + ".startLocation", errors);

            if (!string.IsNullOrWhiteSpace(characterId) && !characterIds.Add(characterId))
            {
                errors.Add(new QuestValidationError("timeline.character.id.duplicate", $"Character id '{characterId}' must be unique.", path + ".id"));
            }

            if (!string.IsNullOrWhiteSpace(characterId))
            {
                // Служебные флаги персонажа для MVP.
                EnsureCharacterFlag(flags, characterId, "selected");
                EnsureCharacterFlag(flags, characterId, "drank_today");
                EnsureCharacterFlag(flags, characterId, "hangover_next_morning");
            }
        }

        var locations = await LoadLocationsAsync(campaignDir, errors, cancellationToken);
        var locationById = new Dictionary<string, LocationDto>(StringComparer.Ordinal);
        foreach (var location in locations)
        {
            if (location is null)
            {
                continue;
            }

            var locationId = location.LocationId ?? location.Id;
            if (string.IsNullOrWhiteSpace(locationId))
            {
                errors.Add(new QuestValidationError("timeline.location.id.required", "Location id is required.", "$.locations[*].id"));
                continue;
            }

            if (!locationById.TryAdd(locationId, location))
            {
                errors.Add(new QuestValidationError("timeline.location.id.duplicate", $"Location id '{locationId}' must be unique.", "$.locations"));
            }
        }

        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (character is null || character.StartLocation is null || character.Id is null)
            {
                continue;
            }

            if (!locationById.ContainsKey(character.StartLocation))
            {
                errors.Add(new QuestValidationError("timeline.character.start_location.missing", $"Character '{character.Id}' start location '{character.StartLocation}' does not exist.", $"$.characters[{index}].startLocation"));
            }
        }

        var nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);

        const string dayStartNodeId = "day_start";
        const string endDayNodeId = "end_day";
        const string endDayIncrementNodeId = "end_day_increment";
        const string dayCheckEndNodeId = "day_check_end";
        const string endNodeId = "game_end";

        nodes[dayStartNodeId] = BuildDayStartNode(dayStartNodeId, characters, characterIds);

        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (character is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(character.Id))
            {
                continue;
            }

            var characterId = character!.Id!;
            var entryNodeId = $"character_entry_{characterId}";
            var hangoverNodeId = $"character_hangover_{characterId}";
            var startNodeId = $"character_start_{characterId}";

            nodes[entryNodeId] = new BranchNodeDefinition(
                entryNodeId,
                [],
                [
                    new BranchDefinition(
                        [
                            new ConditionDefinition($"char_{characterId}_hangover_next_morning", "==", true),
                        ],
                        hangoverNodeId),
                ],
                startNodeId);

            nodes[hangoverNodeId] = new DecisionNodeDefinition(
                hangoverNodeId,
                [character.HangoverText ?? "Утро начинается тяжело: похмелье даёт о себе знать."],
                [
                    new ChoiceDefinition(
                        "continue",
                        "Продолжить",
                        startNodeId,
                        null,
                        [
                            new EffectDefinition("set_flag", $"char_{characterId}_hangover_next_morning", false),
                        ]),
                ]);

            nodes[startNodeId] = BuildCharacterStartBranch(
                startNodeId,
                characterId,
                character.StartLocation!,
                character.StartOverrides,
                locationById.Keys.ToHashSet(StringComparer.Ordinal),
                variables,
                flags,
                errors,
                $"$.characters[{index}]");
        }

        foreach (var (locationId, location) in locationById)
        {
            CompileLocation(locationId, location, nodes, variables, flags, locationById.Keys.ToHashSet(StringComparer.Ordinal), errors);
        }

        nodes[endDayNodeId] = BuildEndDayBranch(endDayNodeId, characters, endDayIncrementNodeId);

        foreach (var character in characters.Where(c => c is not null))
        {
            var characterId = character!.Id!;
            var nodeId = $"end_day_hangover_{characterId}";
            nodes[nodeId] = new DecisionNodeDefinition(
                nodeId,
                ["Ночь проходит шумно. Завтра может быть тяжёлое утро."],
                [
                    new ChoiceDefinition(
                        "continue",
                        "Продолжить",
                        endDayIncrementNodeId,
                        null,
                        [
                            new EffectDefinition("set_flag", $"char_{characterId}_drank_today", false),
                            new EffectDefinition("set_flag", $"char_{characterId}_hangover_next_morning", true),
                        ]),
                ]);
        }

        nodes[endDayIncrementNodeId] = new DecisionNodeDefinition(
            endDayIncrementNodeId,
            ["День подходит к концу."],
            [
                new ChoiceDefinition(
                    "next_day",
                    "Следующий день",
                    dayCheckEndNodeId,
                    null,
                    [
                        new EffectDefinition("add", "day", 1),
                    ]),
            ]);

        nodes[dayCheckEndNodeId] = new BranchNodeDefinition(
            dayCheckEndNodeId,
            [],
            [
                new BranchDefinition(
                    [new ConditionDefinition("day", ">", days)],
                    endNodeId),
            ],
            dayStartNodeId);

        nodes[endNodeId] = new EndNodeDefinition(
            endNodeId,
            ["История на этой временной шкале завершена."],
            "completed");

        var definition = new QuestDefinition(
            questId,
            version,
            title,
            dayCheckEndNodeId,
            new Dictionary<string, int>(variables, StringComparer.Ordinal),
            new Dictionary<string, bool>(flags, StringComparer.Ordinal),
            new Dictionary<string, TextPoolDefinition>(StringComparer.Ordinal),
            nodes);

        return definition;
    }

    private static QuestDefinition EmptyDefinition(CampaignDto campaign)
    {
        return new QuestDefinition(
            campaign.QuestId ?? "timeline",
            campaign.Version ?? "0.0.0",
            campaign.Title ?? "Timeline",
            "game_end",
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, bool>(StringComparer.Ordinal),
            new Dictionary<string, TextPoolDefinition>(StringComparer.Ordinal),
            new Dictionary<string, NodeDefinition>(StringComparer.Ordinal)
            {
                ["game_end"] = new EndNodeDefinition("game_end", ["Invalid campaign."], "invalid")
            });
    }

    private static DecisionNodeDefinition BuildDayStartNode(
        string nodeId,
        IReadOnlyList<CharacterDto?> characters,
        IReadOnlySet<string> uniqueCharacterIds)
    {
        var sortedCharacterIds = uniqueCharacterIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var choices = new List<ChoiceDefinition>();

        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (character is null || string.IsNullOrWhiteSpace(character.Id) || string.IsNullOrWhiteSpace(character.Name))
            {
                continue;
            }

            var effects = new List<EffectDefinition>();
            for (var j = 0; j < sortedCharacterIds.Length; j++)
            {
                var targetCharacterId = sortedCharacterIds[j];
                effects.Add(new EffectDefinition(
                    "set_flag",
                    $"char_{targetCharacterId}_selected",
                    string.Equals(targetCharacterId, character.Id, StringComparison.Ordinal)));
            }

            choices.Add(new ChoiceDefinition(
                $"pick_{character.Id}",
                character.Name,
                $"character_entry_{character.Id}",
                null,
                effects));
        }

        return new DecisionNodeDefinition(
            nodeId,
            ["День {{day}}.", "Выберите персонажа:"],
            choices);
    }

    private static BranchNodeDefinition BuildCharacterStartBranch(
        string nodeId,
        string characterId,
        string defaultStartLocation,
        List<CharacterStartOverrideDto?>? overrides,
        HashSet<string> knownLocations,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        List<QuestValidationError> errors,
        string path)
    {
        var branches = new List<BranchDefinition>();

        if (overrides is not null)
        {
            for (var index = 0; index < overrides.Count; index++)
            {
                var rule = overrides[index];
                var rulePath = $"{path}.startOverrides[{index}]";
                if (rule is null)
                {
                    errors.Add(new QuestValidationError("timeline.override.required", "Start override must be an object.", rulePath));
                    continue;
                }

                var whenExpressions = NormalizeExpressionList(rule.When, rulePath + ".when", errors);
                var conditions = CompileConditions(whenExpressions, variables, flags, rulePath + ".when", errors);

                if (conditions.Count == 0)
                {
                    errors.Add(new QuestValidationError("timeline.override.when.required", "Start override must contain at least one condition.", rulePath + ".when"));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.StartLocation))
                {
                    errors.Add(new QuestValidationError("timeline.override.start_location.required", "Start override startLocation is required.", rulePath + ".startLocation"));
                    continue;
                }

                if (!knownLocations.Contains(rule.StartLocation))
                {
                    errors.Add(new QuestValidationError("timeline.override.start_location.missing", $"Start override location '{rule.StartLocation}' does not exist.", rulePath + ".startLocation"));
                    continue;
                }

                branches.Add(new BranchDefinition(conditions, BuildLocationBranchNodeId(rule.StartLocation)));
            }
        }

        if (!knownLocations.Contains(defaultStartLocation))
        {
            errors.Add(new QuestValidationError("timeline.character.start_location.missing", $"Default start location '{defaultStartLocation}' does not exist.", path + ".startLocation"));
        }

        if (branches.Count == 0)
        {
            // Валидатор текущего runtime-контракта требует хотя бы одну ветку.
            branches.Add(new BranchDefinition(
                [new ConditionDefinition("day", ">=", 1)],
                BuildLocationBranchNodeId(defaultStartLocation)));
        }

        return new BranchNodeDefinition(
            nodeId,
            [],
            branches,
            BuildLocationBranchNodeId(defaultStartLocation));
    }

    private static BranchNodeDefinition BuildEndDayBranch(
        string nodeId,
        IReadOnlyList<CharacterDto?> characters,
        string endDayIncrementNodeId)
    {
        var branches = new List<BranchDefinition>();

        foreach (var character in characters.Where(c => c is not null))
        {
            var characterId = character!.Id!;
            branches.Add(new BranchDefinition(
                [
                    new ConditionDefinition($"char_{characterId}_selected", "==", true),
                    new ConditionDefinition($"char_{characterId}_drank_today", "==", true),
                ],
                $"end_day_hangover_{characterId}"));
        }

        return new BranchNodeDefinition(
            nodeId,
            [],
            branches,
            endDayIncrementNodeId);
    }

    private static string BuildLocationBranchNodeId(string locationId)
    {
        return $"loc_{locationId}";
    }

    private static void CompileLocation(
        string locationId,
        LocationDto location,
        Dictionary<string, NodeDefinition> nodes,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        HashSet<string> knownLocations,
        List<QuestValidationError> errors)
    {
        var entries = location.Entries ?? [];
        if (entries.Count == 0)
        {
            errors.Add(new QuestValidationError("timeline.location.entries.empty", $"Location '{locationId}' must contain at least one entry.", $"$.locations.{locationId}.entries"));
            return;
        }

        var locationBranchNodeId = $"loc_{locationId}";
        var entryNodeIds = new List<string>();
        var branches = new List<BranchDefinition>();

        string? defaultEntryNodeId = null;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var entryPath = $"$.locations.{locationId}.entries[{index}]";
            if (entry is null)
            {
                errors.Add(new QuestValidationError("timeline.entry.required", "Entry must be an object.", entryPath));
                continue;
            }

            var entryKey = !string.IsNullOrWhiteSpace(entry.Id) ? entry.Id : index.ToString();
            var entryNodeId = $"loc_{locationId}__{entryKey}";
            entryNodeIds.Add(entryNodeId);

            var entryText = CompileText(entry.Text, entryPath + ".text", errors);
            var choiceDefinitions = CompileActions(locationId, entry.Actions, knownLocations, variables, flags, errors, entryPath + ".actions");

            // Всегда добавляем возможность завершить день, чтобы контент не "застревал".
            choiceDefinitions.Add(new ChoiceDefinition(
                "end_day",
                "Закончить день",
                "end_day",
                null,
                null));

            nodes[entryNodeId] = new DecisionNodeDefinition(entryNodeId, entryText, choiceDefinitions);

            var whenExpressions = NormalizeExpressionList(entry.When, entryPath + ".when", errors);
            var conditions = CompileConditions(whenExpressions, variables, flags, entryPath + ".when", errors);
            if (conditions.Count == 0)
            {
                if (defaultEntryNodeId is null)
                {
                    defaultEntryNodeId = entryNodeId;
                }

                continue;
            }

            branches.Add(new BranchDefinition(conditions, entryNodeId));
        }

        if (defaultEntryNodeId is null)
        {
            errors.Add(new QuestValidationError("timeline.location.entry.default_missing", $"Location '{locationId}' must contain at least one entry without 'when' as a default.", $"$.locations.{locationId}.entries"));
            return;
        }

        if (branches.Count == 0)
        {
            // Валидатор текущего runtime-контракта требует хотя бы одну ветку.
            branches.Add(new BranchDefinition(
                [new ConditionDefinition("day", ">=", 1)],
                defaultEntryNodeId));
        }

        nodes[locationBranchNodeId] = new BranchNodeDefinition(locationBranchNodeId, [], branches, defaultEntryNodeId);
    }

    private static List<ChoiceDefinition> CompileActions(
        string locationId,
        List<LocationActionDto?>? actions,
        HashSet<string> knownLocations,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        List<QuestValidationError> errors,
        string path)
    {
        var compiled = new List<ChoiceDefinition>();
        if (actions is null)
        {
            return compiled;
        }

        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var actionPath = $"{path}[{index}]";
            if (action is null)
            {
                errors.Add(new QuestValidationError("timeline.action.required", "Action must be an object.", actionPath));
                continue;
            }

            var id = RequireString(action.Id, actionPath + ".id", errors);
            var text = RequireString(action.Text, actionPath + ".text", errors);

            var whenExpressions = NormalizeExpressionList(action.When, actionPath + ".when", errors);
            var conditions = CompileConditions(whenExpressions, variables, flags, actionPath + ".when", errors);

            var doExpressions = NormalizeExpressionList(action.Do, actionPath + ".do", errors);
            var effects = CompileEffects(doExpressions, variables, flags, actionPath + ".do", errors);

            var nextNodeId = string.IsNullOrWhiteSpace(action.Goto) ? $"loc_{locationId}" : $"loc_{action.Goto}";
            if (string.Equals(action.Goto, "end_day", StringComparison.OrdinalIgnoreCase))
            {
                nextNodeId = "end_day";
            }
            else if (!string.IsNullOrWhiteSpace(action.Goto) && !knownLocations.Contains(action.Goto))
            {
                errors.Add(new QuestValidationError("timeline.action.goto.missing", $"Action goto '{action.Goto}' does not exist as a location id.", actionPath + ".goto"));
            }

            compiled.Add(new ChoiceDefinition(
                id,
                text,
                nextNodeId,
                conditions.Count == 0 ? null : conditions,
                effects.Count == 0 ? null : effects));
        }

        return compiled;
    }

    private static IReadOnlyList<LocationDto?> LoadLocationsFromFolder(string folderPath, List<QuestValidationError> errors)
    {
        if (!Directory.Exists(folderPath))
        {
            errors.Add(new QuestValidationError("timeline.locations.missing", $"Locations folder '{folderPath}' does not exist.", "$.locations"));
            return [];
        }

        var files = Directory.GetFiles(folderPath, "*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(folderPath, "*.yaml", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            errors.Add(new QuestValidationError("timeline.locations.empty", $"Locations folder '{folderPath}' does not contain any *.yml/*.yaml files.", "$.locations"));
            return [];
        }

        var result = new List<LocationDto?>(files.Length);

        foreach (var file in files)
        {
            try
            {
                var yaml = File.ReadAllText(file);
                var dto = Deserializer.Deserialize<LocationDto>(yaml);
                result.Add(dto);
            }
            catch (YamlException exception)
            {
                errors.Add(new QuestValidationError("timeline.location.syntax", $"{Path.GetFileName(file)}: {exception.Message}", "$.locations"));
            }
            catch (IOException exception)
            {
                errors.Add(new QuestValidationError("timeline.location.read_failed", $"{Path.GetFileName(file)}: {exception.Message}", "$.locations"));
            }
            catch (UnauthorizedAccessException exception)
            {
                errors.Add(new QuestValidationError("timeline.location.read_failed", $"{Path.GetFileName(file)}: {exception.Message}", "$.locations"));
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<LocationDto?>> LoadLocationsAsync(
        string campaignDir,
        List<QuestValidationError> errors,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var locationsDir = Path.Combine(campaignDir, "locations");
        return LoadLocationsFromFolder(locationsDir, errors);
    }

    private static List<string> CompileText(object? textValue, string path, List<QuestValidationError> errors)
    {
        switch (textValue)
        {
            case null:
                errors.Add(new QuestValidationError("timeline.text.required", "Text is required.", path));
                return ["(missing text)"];

            case string text:
                return [text];

            case List<object?> list:
                {
                    var lines = new List<string>();
                    for (var index = 0; index < list.Count; index++)
                    {
                        if (list[index] is null)
                        {
                            continue;
                        }

                        if (list[index] is not string line)
                        {
                            errors.Add(new QuestValidationError("timeline.text.line.invalid", "Text line must be a string.", $"{path}[{index}]"));
                            continue;
                        }

                        lines.Add(line);
                    }

                    if (lines.Count == 0)
                    {
                        errors.Add(new QuestValidationError("timeline.text.empty", "Text must not be empty.", path));
                        return ["(empty text)"];
                    }

                    return lines;
                }

            default:
                errors.Add(new QuestValidationError("timeline.text.invalid", "Text must be a string or a list of strings.", path));
                return ["(invalid text)"];
        }
    }

    private static List<string> NormalizeExpressionList(object? value, string path, List<QuestValidationError> errors)
    {
        switch (value)
        {
            case null:
                return [];

            case string expression:
                return string.IsNullOrWhiteSpace(expression) ? [] : [expression.Trim()];

            case List<object?> list:
                {
                    var result = new List<string>();
                    for (var index = 0; index < list.Count; index++)
                    {
                        if (list[index] is null)
                        {
                            continue;
                        }

                        if (list[index] is not string item)
                        {
                            errors.Add(new QuestValidationError("timeline.expression.invalid", "Expression must be a string.", $"{path}[{index}]"));
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(item))
                        {
                            continue;
                        }

                        result.Add(item.Trim());
                    }

                    return result;
                }

            default:
                errors.Add(new QuestValidationError("timeline.expression.invalid", "Expression must be a string or a list of strings.", path));
                return [];
        }
    }

    private static List<ConditionDefinition> CompileConditions(
        IReadOnlyList<string> expressions,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        string path,
        List<QuestValidationError> errors)
    {
        var compiled = new List<ConditionDefinition>();

        foreach (var expr in expressions)
        {
            if (string.IsNullOrWhiteSpace(expr))
            {
                continue;
            }

            if (expr.StartsWith("!", StringComparison.Ordinal))
            {
                var name = expr[1..].Trim();
                if (!flags.ContainsKey(name))
                {
                    if (variables.ContainsKey(name))
                    {
                        errors.Add(new QuestValidationError("timeline.condition.shorthand.invalid", $"Negated shorthand condition '!{name}' is only valid for flags, not variables.", path));
                    }
                    else
                    {
                        errors.Add(new QuestValidationError("timeline.condition.target.missing", $"Condition target '{name}' is not defined in vars or flags.", path));
                    }

                    continue;
                }

                compiled.Add(new ConditionDefinition(name, "==", false));
                continue;
            }

            var match = ComparisonExpressionRegex.Match(expr);
            if (!match.Success)
            {
                var name = expr.Trim();
                if (!flags.ContainsKey(name))
                {
                    if (variables.ContainsKey(name))
                    {
                        errors.Add(new QuestValidationError("timeline.condition.shorthand.invalid", $"Shorthand condition '{name}' is only valid for flags, not variables. Use comparisons like '{name} >= 1'.", path));
                    }
                    else
                    {
                        errors.Add(new QuestValidationError("timeline.condition.target.missing", $"Condition target '{name}' is not defined in vars or flags.", path));
                    }

                    continue;
                }

                compiled.Add(new ConditionDefinition(name, "==", true));
                continue;
            }

            var target = match.Groups["target"].Value;
            var op = match.Groups["operator"].Value;
            var valueRaw = match.Groups["value"].Value.Trim();

            if (flags.ContainsKey(target))
            {
                if (!bool.TryParse(valueRaw, out var boolValue))
                {
                    errors.Add(new QuestValidationError("timeline.condition.value.invalid", $"Condition value for flag '{target}' must be 'true' or 'false'.", path));
                    continue;
                }

                compiled.Add(new ConditionDefinition(target, op, boolValue));
                continue;
            }

            if (variables.ContainsKey(target))
            {
                if (!int.TryParse(valueRaw, out var intValue))
                {
                    errors.Add(new QuestValidationError("timeline.condition.value.invalid", $"Condition value for variable '{target}' must be an integer.", path));
                    continue;
                }

                compiled.Add(new ConditionDefinition(target, op, intValue));
                continue;
            }

            errors.Add(new QuestValidationError("timeline.condition.target.missing", $"Condition target '{target}' is not defined in vars or flags.", path));
        }

        return compiled;
    }

    private static List<EffectDefinition> CompileEffects(
        IReadOnlyList<string> expressions,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        string path,
        List<QuestValidationError> errors)
    {
        var compiled = new List<EffectDefinition>();

        foreach (var expr in expressions)
        {
            if (string.IsNullOrWhiteSpace(expr))
            {
                continue;
            }

            var match = EffectExpressionRegex.Match(expr);
            if (!match.Success)
            {
                errors.Add(new QuestValidationError("timeline.effect.invalid", $"Effect '{expr}' is not a valid expression.", path));
                continue;
            }

            var target = match.Groups["target"].Value;
            var op = match.Groups["operator"].Value;
            var valueRaw = match.Groups["value"].Value.Trim();

            if (flags.ContainsKey(target))
            {
                if (op is not "=")
                {
                    errors.Add(new QuestValidationError("timeline.effect.operator.invalid", $"Only '=' is supported for flag '{target}'.", path));
                    continue;
                }

                if (!bool.TryParse(valueRaw, out var boolValue))
                {
                    errors.Add(new QuestValidationError("timeline.effect.value.invalid", $"Effect value for flag '{target}' must be 'true' or 'false'.", path));
                    continue;
                }

                compiled.Add(new EffectDefinition("set_flag", target, boolValue));
                continue;
            }

            if (variables.ContainsKey(target))
            {
                if (!int.TryParse(valueRaw, out var intValue))
                {
                    errors.Add(new QuestValidationError("timeline.effect.value.invalid", $"Effect value for variable '{target}' must be an integer.", path));
                    continue;
                }

                compiled.Add(op switch
                {
                    "=" => new EffectDefinition("set_variable", target, intValue),
                    "+=" => new EffectDefinition("add", target, intValue),
                    "-=" => new EffectDefinition("add", target, -intValue),
                    _ => throw new InvalidOperationException($"Unsupported operator '{op}'."),
                });
                continue;
            }

            errors.Add(new QuestValidationError("timeline.effect.target.missing", $"Effect target '{target}' is not defined in vars or flags.", path));
        }

        return compiled;
    }

    private static string RequireString(string? value, string path, List<QuestValidationError> errors)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        errors.Add(new QuestValidationError("timeline.value.required", "Value is required.", path));
        return string.Empty;
    }

    private static int RequireInt(int? value, string path, List<QuestValidationError> errors, int? minValue = null)
    {
        if (value is null)
        {
            errors.Add(new QuestValidationError("timeline.value.required", "Value is required.", path));
            return 0;
        }

        if (minValue is not null && value.Value < minValue.Value)
        {
            errors.Add(new QuestValidationError("timeline.value.range", $"Value must be >= {minValue.Value}.", path));
        }

        return value.Value;
    }

    private static void RequireVar(Dictionary<string, int> variables, string name, string path, List<QuestValidationError> errors)
    {
        if (!variables.ContainsKey(name))
        {
            errors.Add(new QuestValidationError("timeline.vars.missing", $"Variable '{name}' must be declared in vars.", path, Expected: "declared variable", Actual: name));
        }
    }

    private static void EnsureCharacterFlag(Dictionary<string, bool> flags, string characterId, string suffix)
    {
        var name = $"char_{characterId}_{suffix}";
        if (!flags.ContainsKey(name))
        {
            flags[name] = false;
        }
    }

    private sealed class CampaignDto
    {
        public string? QuestId { get; init; }
        public string? Version { get; init; }
        public string? Title { get; init; }
        public int? Days { get; init; }
        public Dictionary<string, int>? Vars { get; init; }
        public Dictionary<string, bool>? Flags { get; init; }
        public List<CharacterDto?>? Characters { get; init; }
    }

    private sealed class CharacterDto
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? StartLocation { get; init; }
        public string? HangoverText { get; init; }
        public List<CharacterStartOverrideDto?>? StartOverrides { get; init; }
    }

    private sealed class CharacterStartOverrideDto
    {
        public object? When { get; init; }
        public string? StartLocation { get; init; }
    }

    private sealed class LocationDto
    {
        public string? Id { get; init; }
        public string? LocationId { get; init; }
        public string? Title { get; init; }
        public List<LocationEntryDto?>? Entries { get; init; }
    }

    private sealed class LocationEntryDto
    {
        public string? Id { get; init; }
        public object? When { get; init; }
        public object? Text { get; init; }
        public List<LocationActionDto?>? Actions { get; init; }
    }

    private sealed class LocationActionDto
    {
        public string? Id { get; init; }
        public string? Text { get; init; }
        public string? Goto { get; init; }
        public object? When { get; init; }
        public object? Do { get; init; }
    }
}
