using System.Text.RegularExpressions;
using TextQuest.Application.Abstractions;
using TextQuest.Application.Models;
using TextQuest.Domain.Models;

namespace TextQuest.Content.Json;

/// <summary>
/// Проверяет JSON-представление квеста после преобразования в доменную модель.
/// </summary>
public sealed class JsonQuestValidator : IQuestValidator
{
    private static readonly Regex VariablePattern = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);
    private static readonly Regex ConditionalPattern = new(@"\[if\s+(!?)(\w+)\](.*?)\[endif\]", RegexOptions.Compiled | RegexOptions.Singleline);
    /// <inheritdoc />
    public QuestValidationResult Validate(QuestDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<QuestValidationError>();

        RequireValue(definition.QuestId, "$.questId", "Quest id is required.", errors);
        RequireValue(definition.Version, "$.version", "Quest version is required.", errors);
        RequireValue(definition.Title, "$.title", "Quest title is required.", errors);
        RequireValue(definition.StartNodeId, "$.startNodeId", "Start node id is required.", errors);

        if (definition.Nodes.Count == 0)
        {
            AddError(errors, "quest.nodes.empty", "Quest must contain at least one node.", "$.nodes", expected: "at least 1 node", actual: definition.Nodes.Count.ToString());
            return QuestValidationResult.Failure(errors);
        }

        if (!string.IsNullOrWhiteSpace(definition.StartNodeId) && !definition.Nodes.ContainsKey(definition.StartNodeId))
        {
            AddError(
                errors,
                "quest.start_node.missing",
                $"Start node '{definition.StartNodeId}' does not exist.",
                "$.startNodeId",
                expected: "existing node id",
                actual: FormatValue(definition.StartNodeId));
        }

        foreach (var variableName in definition.InitialVariables.Keys)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                AddError(errors, "quest.variables.name_required", "Variable name must not be empty.", "$.variables", expected: "non-empty variable name", actual: FormatValue(variableName));
            }
        }

        foreach (var flagName in definition.InitialFlags.Keys)
        {
            if (string.IsNullOrWhiteSpace(flagName))
            {
                AddError(errors, "quest.flags.name_required", "Flag name must not be empty.", "$.flags", expected: "non-empty flag name", actual: FormatValue(flagName));
            }
        }

        ValidateTextPools(definition.TextPools, errors);

        foreach (var (nodeId, node) in definition.Nodes)
        {
            var nodePath = BuildNodePath(nodeId);

            RequireValue(node.Id, nodePath + ".id", "Node id is required.", errors, nodeId: nodeId);

            if (!string.Equals(nodeId, node.Id, StringComparison.Ordinal))
            {
                AddError(
                    errors,
                    "node.id.mismatch",
                    $"Node dictionary key '{nodeId}' does not match node id '{node.Id}'.",
                    nodePath + ".id",
                    nodeId: nodeId,
                    expected: FormatValue(nodeId),
                    actual: FormatValue(node.Id));
            }

            if (node is not BranchNodeDefinition)
            {
                RequireText(node.Text, nodePath + ".text", errors, nodeId, definition.InitialVariables, definition.InitialFlags, definition.TextPools);
            }

            switch (node)
            {
                case TextNodeDefinition textNode:
                    ValidateChoices(textNode.Choices, definition.InitialVariables, definition.InitialFlags, definition.TextPools, definition.Nodes, nodeId, nodePath + ".choices", errors);
                    break;

                case DecisionNodeDefinition decisionNode:
                    ValidateChoices(decisionNode.Choices, definition.InitialVariables, definition.InitialFlags, definition.TextPools, definition.Nodes, nodeId, nodePath + ".choices", errors);
                    break;

                case BranchNodeDefinition branchNode:
                    if (branchNode.Branches.Count == 0)
                    {
                        AddError(errors, "branch.branches.empty", "Branch node must define at least one branch.", nodePath + ".branches", nodeId: nodeId, expected: "at least 1 branch", actual: branchNode.Branches.Count.ToString());
                    }

                    RequireValue(
                        branchNode.DefaultNextNodeId,
                        nodePath + ".defaultNextNodeId",
                        "Branch node default next node id is required.",
                        errors,
                        nodeId: nodeId);

                    if (!string.IsNullOrWhiteSpace(branchNode.DefaultNextNodeId)
                        && !definition.Nodes.ContainsKey(branchNode.DefaultNextNodeId))
                    {
                        AddError(
                            errors,
                            "branch.default_next_node.missing",
                            $"Branch default next node '{branchNode.DefaultNextNodeId}' does not exist.",
                            nodePath + ".defaultNextNodeId",
                            nodeId: nodeId,
                            expected: "existing node id",
                            actual: FormatValue(branchNode.DefaultNextNodeId));
                    }

                    for (var branchIndex = 0; branchIndex < branchNode.Branches.Count; branchIndex++)
                    {
                        var branch = branchNode.Branches[branchIndex];
                        var branchPath = $"{nodePath}.branches[{branchIndex}]";

                        if (branch.Conditions.Count == 0)
                        {
                            AddError(errors, "branch.conditions.empty", "Branch entry must contain at least one condition.", branchPath + ".conditions", nodeId: nodeId, expected: "at least 1 condition", actual: branch.Conditions.Count.ToString());
                        }

                        ValidateConditions(branch.Conditions, definition.InitialVariables, definition.InitialFlags, branchPath + ".conditions", errors, nodeId);
                        RequireValue(branch.NextNodeId, branchPath + ".nextNodeId", "Branch next node id is required.", errors, nodeId: nodeId);

                        if (!string.IsNullOrWhiteSpace(branch.NextNodeId) && !definition.Nodes.ContainsKey(branch.NextNodeId))
                        {
                            AddError(
                                errors,
                                "branch.next_node.missing",
                                $"Branch next node '{branch.NextNodeId}' does not exist.",
                                branchPath + ".nextNodeId",
                                nodeId: nodeId,
                                expected: "existing node id",
                                actual: FormatValue(branch.NextNodeId));
                        }
                    }

                    break;

                case EndNodeDefinition endNode:
                    RequireValue(endNode.Result, nodePath + ".result", "End node result is required.", errors, nodeId: nodeId);
                    break;
            }
        }

        return errors.Count == 0 ? QuestValidationResult.Success() : QuestValidationResult.Failure(errors);
    }

    private static void ValidateChoices(
        IReadOnlyList<ChoiceDefinition> choices,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        IReadOnlyDictionary<string, TextPoolDefinition> textPools,
        IReadOnlyDictionary<string, NodeDefinition> nodes,
        string nodeId,
        string path,
        List<QuestValidationError> errors)
    {
        if (choices.Count == 0)
        {
            AddError(errors, "choice.list.empty", "Node must contain at least one choice.", path, nodeId: nodeId, expected: "at least 1 choice", actual: choices.Count.ToString());
            return;
        }

        var choiceIds = new HashSet<string>(StringComparer.Ordinal);

        for (var choiceIndex = 0; choiceIndex < choices.Count; choiceIndex++)
        {
            var choice = choices[choiceIndex];
            var choicePath = $"{path}[{choiceIndex}]";

            RequireValue(choice.Id, choicePath + ".id", "Choice id is required.", errors, nodeId: nodeId);
            RequireValue(choice.Text, choicePath + ".text", "Choice text is required.", errors, nodeId: nodeId, choiceId: choice.Id);

            if (!string.IsNullOrWhiteSpace(choice.Text))
            {
                ValidateTextTemplates(choice.Text, choicePath + ".text", errors, nodeId, variables, flags, textPools, choice.Id);
            }

            RequireValue(choice.NextNodeId, choicePath + ".nextNodeId", "Choice next node id is required.", errors, nodeId: nodeId, choiceId: choice.Id);

            if (!string.IsNullOrWhiteSpace(choice.Id) && !choiceIds.Add(choice.Id))
            {
                AddError(
                    errors,
                    "choice.id.duplicate",
                    $"Choice id '{choice.Id}' must be unique within a node.",
                    choicePath + ".id",
                    nodeId: nodeId,
                    choiceId: choice.Id,
                    expected: "unique choice id within node",
                    actual: FormatValue(choice.Id));
            }

            if (!string.IsNullOrWhiteSpace(choice.NextNodeId) && !nodes.ContainsKey(choice.NextNodeId))
            {
                AddError(
                    errors,
                    "choice.next_node.missing",
                    $"Choice next node '{choice.NextNodeId}' does not exist.",
                    choicePath + ".nextNodeId",
                    nodeId: nodeId,
                    choiceId: choice.Id,
                    expected: "existing node id",
                    actual: FormatValue(choice.NextNodeId));
            }

            ValidateConditions(choice.Conditions ?? Array.Empty<ConditionDefinition>(), variables, flags, choicePath + ".conditions", errors, nodeId, choice.Id);
            ValidateEffects(choice.Effects ?? Array.Empty<EffectDefinition>(), variables, flags, choicePath + ".effects", errors, nodeId, choice.Id);
        }
    }

    private static void ValidateConditions(
        IReadOnlyList<ConditionDefinition> conditions,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        string path,
        List<QuestValidationError> errors,
        string? nodeId = null,
        string? choiceId = null)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            var condition = conditions[index];
            var conditionPath = $"{path}[{index}]";

            RequireValue(condition.Target, conditionPath + ".target", "Condition target is required.", errors, nodeId: nodeId, choiceId: choiceId);
            RequireValue(condition.Operator, conditionPath + ".operator", "Condition operator is required.", errors, nodeId: nodeId, choiceId: choiceId);

            if (string.IsNullOrWhiteSpace(condition.Target) || string.IsNullOrWhiteSpace(condition.Operator))
            {
                continue;
            }

            ValidateConditionTargetAndValue(condition, variables, flags, conditionPath, errors, nodeId, choiceId);
        }
    }

    private static void ValidateEffects(
        IReadOnlyList<EffectDefinition> effects,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        string path,
        List<QuestValidationError> errors,
        string? nodeId = null,
        string? choiceId = null)
    {
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            var effectPath = $"{path}[{index}]";

            RequireValue(effect.Type, effectPath + ".type", "Effect type is required.", errors, nodeId: nodeId, choiceId: choiceId);
            RequireValue(effect.Target, effectPath + ".target", "Effect target is required.", errors, nodeId: nodeId, choiceId: choiceId);

            if (string.IsNullOrWhiteSpace(effect.Type) || string.IsNullOrWhiteSpace(effect.Target))
            {
                continue;
            }

            ValidateEffectTargetAndValue(effect, variables, flags, effectPath, errors, nodeId, choiceId);
        }
    }

    private static void ValidateConditionTargetAndValue(
        ConditionDefinition condition,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        string path,
        List<QuestValidationError> errors,
        string? nodeId,
        string? choiceId)
    {
        if (flags.ContainsKey(condition.Target))
        {
            if (condition.Operator is not "==" and not "!=")
            {
                AddError(
                    errors,
                    "condition.operator.invalid_for_flag",
                    $"Operator '{condition.Operator}' is not supported for flag '{condition.Target}'.",
                    path + ".operator",
                    nodeId: nodeId,
                    choiceId: choiceId,
                    expected: "'==' or '!='",
                    actual: FormatValue(condition.Operator));
            }

            if (condition.Value is not bool)
            {
                AddError(
                    errors,
                    "condition.value.invalid_for_flag",
                    $"Condition value for flag '{condition.Target}' must be boolean.",
                    path + ".value",
                    nodeId: nodeId,
                    choiceId: choiceId,
                    expected: "boolean",
                    actual: FormatValue(condition.Value));
            }

            return;
        }

        if (variables.ContainsKey(condition.Target))
        {
            if (condition.Operator is not "==" and not "!=" and not ">" and not ">=" and not "<" and not "<=")
            {
                AddError(
                    errors,
                    "condition.operator.invalid_for_variable",
                    $"Operator '{condition.Operator}' is not supported for variable '{condition.Target}'.",
                    path + ".operator",
                    nodeId: nodeId,
                    choiceId: choiceId,
                    expected: "'==', '!=', '>', '>=', '<' or '<='",
                    actual: FormatValue(condition.Operator));
            }

            if (!IsIntegerValue(condition.Value))
            {
                AddError(
                    errors,
                    "condition.value.invalid_for_variable",
                    $"Condition value for variable '{condition.Target}' must be an integer.",
                    path + ".value",
                    nodeId: nodeId,
                    choiceId: choiceId,
                    expected: "integer",
                    actual: FormatValue(condition.Value));
            }

            return;
        }

        AddError(
            errors,
            "condition.target.missing",
            $"Condition target '{condition.Target}' is not defined in variables or flags.",
            path + ".target",
            nodeId: nodeId,
            choiceId: choiceId,
            expected: "defined variable or flag name",
            actual: FormatValue(condition.Target));
    }

    private static void ValidateEffectTargetAndValue(
        EffectDefinition effect,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        string path,
        List<QuestValidationError> errors,
        string? nodeId,
        string? choiceId)
    {
        switch (effect.Type)
        {
            case "add":
            case "set_variable":
                if (!variables.ContainsKey(effect.Target))
                {
                    AddError(
                        errors,
                        "effect.target.variable_missing",
                        $"Effect target '{effect.Target}' is not defined as a variable.",
                        path + ".target",
                        nodeId: nodeId,
                        choiceId: choiceId,
                        expected: "defined variable name",
                        actual: FormatValue(effect.Target));
                }

                if (!IsIntegerValue(effect.Value))
                {
                    AddError(
                        errors,
                        "effect.value.invalid_for_variable",
                        $"Effect value for variable '{effect.Target}' must be an integer.",
                        path + ".value",
                        nodeId: nodeId,
                        choiceId: choiceId,
                        expected: "integer",
                        actual: FormatValue(effect.Value));
                }

                break;

            case "set_flag":
                if (!flags.ContainsKey(effect.Target))
                {
                    AddError(
                        errors,
                        "effect.target.flag_missing",
                        $"Effect target '{effect.Target}' is not defined as a flag.",
                        path + ".target",
                        nodeId: nodeId,
                        choiceId: choiceId,
                        expected: "defined flag name",
                        actual: FormatValue(effect.Target));
                }

                if (effect.Value is not bool)
                {
                    AddError(
                        errors,
                        "effect.value.invalid_for_flag",
                        $"Effect value for flag '{effect.Target}' must be boolean.",
                        path + ".value",
                        nodeId: nodeId,
                        choiceId: choiceId,
                        expected: "boolean",
                        actual: FormatValue(effect.Value));
                }

                break;

            default:
                AddError(
                    errors,
                    "effect.type.invalid",
                    $"Effect type '{effect.Type}' is not supported.",
                    path + ".type",
                    nodeId: nodeId,
                    choiceId: choiceId,
                    expected: "'add', 'set_variable' or 'set_flag'",
                    actual: FormatValue(effect.Type));
                break;
        }
    }

    private static void ValidateTextTemplates(
        string text,
        string path,
        List<QuestValidationError> errors,
        string? nodeId,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        IReadOnlyDictionary<string, TextPoolDefinition> textPools,
        string? choiceId = null)
    {
        // Validate template syntax structure
        var openVariableCount = Regex.Matches(text, @"\{\{").Count;
        var closeVariableCount = Regex.Matches(text, @"\}\}").Count;
        if (openVariableCount != closeVariableCount)
        {
            AddError(
                errors,
                "text.template.malformed_variables",
                "Text template has unmatched variable braces {{ or }}.",
                path,
                nodeId: nodeId,
                choiceId: choiceId,
                expected: "balanced {{ and }} pairs",
                actual: $"{openVariableCount} open, {closeVariableCount} close");
        }

        var ifCount = Regex.Matches(text, @"\[if").Count;
        var endifCount = Regex.Matches(text, @"\[endif\]").Count;
        if (ifCount != endifCount)
        {
            AddError(
                errors,
                "text.template.malformed_conditionals",
                "Text template has unmatched conditional blocks [if or [endif].",
                path,
                nodeId: nodeId,
                choiceId: choiceId,
                expected: "balanced [if and [endif] pairs",
                actual: $"{ifCount} [if, {endifCount} [endif]");
        }

        // Validate variable substitutions {{variable}}
        foreach (Match match in VariablePattern.Matches(text))
        {
            var token = match.Groups[1].Value.Trim();

            if (TryParsePoolToken(token, out var poolId))
            {
                if (!textPools.ContainsKey(poolId))
                {
                    AddError(
                        errors,
                        "text.pool.missing",
                        $"Text pool '{poolId}' referenced in template is not defined.",
                        path,
                        nodeId: nodeId,
                        choiceId: choiceId,
                        expected: "defined text pool id",
                        actual: FormatValue(poolId));
                }

                continue;
            }

            if (token.StartsWith("pool:", StringComparison.OrdinalIgnoreCase))
            {
                AddError(
                    errors,
                    "text.pool.invalid",
                    "Text pool reference must have non-empty id in form {{pool:poolId}}.",
                    path,
                    nodeId: nodeId,
                    choiceId: choiceId,
                    expected: "{{pool:poolId}}",
                    actual: FormatValue(token));
                continue;
            }

            if (!variables.ContainsKey(token))
            {
                AddError(
                    errors,
                    "text.variable.missing",
                    $"Variable '{token}' referenced in text template is not defined.",
                    path,
                    nodeId: nodeId,
                    choiceId: choiceId,
                    expected: "defined variable name",
                    actual: FormatValue(token));
            }
        }

        // Validate conditionals [if flag]...[endif]
        foreach (Match match in ConditionalPattern.Matches(text))
        {
            var flagName = match.Groups[2].Value;
            if (!flags.ContainsKey(flagName))
            {
                AddError(
                    errors,
                    "text.flag.missing",
                    $"Flag '{flagName}' referenced in text template conditional is not defined.",
                    path,
                    nodeId: nodeId,
                    choiceId: choiceId,
                    expected: "defined flag name",
                    actual: FormatValue(flagName));
            }
        }
    }

    private static bool IsIntegerValue(object? value)
    {
        return value is int or long;
    }

    private static void RequireText(
        IReadOnlyList<string> text,
        string path,
        List<QuestValidationError> errors,
        string? nodeId,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags,
        IReadOnlyDictionary<string, TextPoolDefinition> textPools)
    {
        if (text.Count == 0)
        {
            AddError(errors, "node.text.empty", "Node text must contain at least one line.", path, nodeId: nodeId, expected: "at least 1 text line", actual: text.Count.ToString());
            return;
        }

        for (var lineIndex = 0; lineIndex < text.Count; lineIndex++)
        {
            var line = text[lineIndex];
            var linePath = $"{path}[{lineIndex}]";

            if (string.IsNullOrWhiteSpace(line))
            {
                AddError(errors, "node.text.line_empty", "Text line must not be empty.", linePath, nodeId: nodeId, expected: "non-empty text line", actual: FormatValue(line));
            }
            else
            {
                ValidateTextTemplates(line, linePath, errors, nodeId, variables, flags, textPools);
            }
        }
    }

    private static void ValidateTextPools(IReadOnlyDictionary<string, TextPoolDefinition> textPools, List<QuestValidationError> errors)
    {
        foreach (var (poolId, pool) in textPools)
        {
            var poolPath = $"$.textPools['{poolId.Replace("'", "\\'", StringComparison.Ordinal)}']";

            if (string.IsNullOrWhiteSpace(poolId) || string.IsNullOrWhiteSpace(pool.Id))
            {
                AddError(errors, "text_pool.id.required", "Text pool id must not be empty.", poolPath, expected: "non-empty pool id", actual: FormatValue(poolId));
                continue;
            }

            if (pool.Items.Count == 0)
            {
                AddError(errors, "text_pool.items.empty", $"Text pool '{poolId}' must contain at least one item.", poolPath, expected: "at least 1 item", actual: pool.Items.Count.ToString());
            }

            for (var index = 0; index < pool.Items.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(pool.Items[index]))
                {
                    AddError(errors, "text_pool.item.required", $"Text pool '{poolId}' must contain only non-empty strings.", $"{poolPath}[{index}]", expected: "non-empty string", actual: FormatValue(pool.Items[index]));
                }
            }
        }
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

    private static void RequireValue(
        string? value,
        string path,
        string message,
        List<QuestValidationError> errors,
        string? nodeId = null,
        string? choiceId = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, "validation.required", message, path, nodeId: nodeId, choiceId: choiceId, expected: "non-empty string", actual: FormatValue(value));
        }
    }

    private static void AddError(
        List<QuestValidationError> errors,
        string code,
        string message,
        string path,
        string? nodeId = null,
        string? choiceId = null,
        string? expected = null,
        string? actual = null)
    {
        errors.Add(new QuestValidationError(code, message, path, nodeId, choiceId, expected, actual));
    }

    private static string BuildNodePath(string nodeId)
    {
        return $"$.nodes['{nodeId.Replace("'", "\\'", StringComparison.Ordinal)}']";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"'{text}'",
            bool boolean => boolean ? "true" : "false",
            _ => value.ToString() ?? "null",
        };
    }
}
