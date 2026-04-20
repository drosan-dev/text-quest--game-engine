using TextQuest.Application.Abstractions;
using TextQuest.Application.Models;
using TextQuest.Domain.Models;

namespace TextQuest.Content.Json;

public sealed class JsonQuestValidator : IQuestValidator
{
    public QuestValidationResult Validate(QuestDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<QuestValidationError>();

        RequireValue(definition.QuestId, "questId", "Quest id is required.", errors);
        RequireValue(definition.Version, "version", "Quest version is required.", errors);
        RequireValue(definition.Title, "title", "Quest title is required.", errors);
        RequireValue(definition.StartNodeId, "startNodeId", "Start node id is required.", errors);

        if (definition.Nodes.Count == 0)
        {
            errors.Add(new QuestValidationError("quest.nodes.empty", "Quest must contain at least one node.", "nodes"));
            return QuestValidationResult.Failure(errors);
        }

        if (!string.IsNullOrWhiteSpace(definition.StartNodeId) && !definition.Nodes.ContainsKey(definition.StartNodeId))
        {
            errors.Add(new QuestValidationError(
                "quest.start_node.missing",
                $"Start node '{definition.StartNodeId}' does not exist.",
                "startNodeId"));
        }

        foreach (var variableName in definition.InitialVariables.Keys)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                errors.Add(new QuestValidationError(
                    "quest.variables.name_required",
                    "Variable name must not be empty.",
                    "variables"));
            }
        }

        foreach (var flagName in definition.InitialFlags.Keys)
        {
            if (string.IsNullOrWhiteSpace(flagName))
            {
                errors.Add(new QuestValidationError(
                    "quest.flags.name_required",
                    "Flag name must not be empty.",
                    "flags"));
            }
        }

        foreach (var (nodeId, node) in definition.Nodes)
        {
            var nodePath = $"nodes.{nodeId}";

            RequireValue(node.Id, nodePath + ".id", "Node id is required.", errors);

            if (!string.Equals(nodeId, node.Id, StringComparison.Ordinal))
            {
                errors.Add(new QuestValidationError(
                    "node.id.mismatch",
                    $"Node dictionary key '{nodeId}' does not match node id '{node.Id}'.",
                    nodePath));
            }

            if (node is not BranchNodeDefinition)
            {
                RequireText(node.Text, nodePath + ".text", errors);
            }

            switch (node)
            {
                case TextNodeDefinition textNode:
                    ValidateChoices(textNode.Choices, definition.Nodes, nodePath + ".choices", errors);
                    break;

                case DecisionNodeDefinition decisionNode:
                    ValidateChoices(decisionNode.Choices, definition.Nodes, nodePath + ".choices", errors);
                    break;

                case BranchNodeDefinition branchNode:
                    if (branchNode.Branches.Count == 0)
                    {
                        errors.Add(new QuestValidationError(
                            "branch.branches.empty",
                            "Branch node must define at least one branch.",
                            nodePath + ".branches"));
                    }

                    RequireValue(
                        branchNode.DefaultNextNodeId,
                        nodePath + ".defaultNextNodeId",
                        "Branch node default next node id is required.",
                        errors);

                    if (!string.IsNullOrWhiteSpace(branchNode.DefaultNextNodeId)
                        && !definition.Nodes.ContainsKey(branchNode.DefaultNextNodeId))
                    {
                        errors.Add(new QuestValidationError(
                            "branch.default_next_node.missing",
                            $"Branch default next node '{branchNode.DefaultNextNodeId}' does not exist.",
                            nodePath + ".defaultNextNodeId"));
                    }

                    for (var branchIndex = 0; branchIndex < branchNode.Branches.Count; branchIndex++)
                    {
                        var branch = branchNode.Branches[branchIndex];
                        var branchPath = $"{nodePath}.branches[{branchIndex}]";

                        if (branch.Conditions.Count == 0)
                        {
                            errors.Add(new QuestValidationError(
                                "branch.conditions.empty",
                                "Branch entry must contain at least one condition.",
                                branchPath + ".conditions"));
                        }

                        ValidateConditions(branch.Conditions, branchPath + ".conditions", errors);
                        RequireValue(branch.NextNodeId, branchPath + ".nextNodeId", "Branch next node id is required.", errors);

                        if (!string.IsNullOrWhiteSpace(branch.NextNodeId) && !definition.Nodes.ContainsKey(branch.NextNodeId))
                        {
                            errors.Add(new QuestValidationError(
                                "branch.next_node.missing",
                                $"Branch next node '{branch.NextNodeId}' does not exist.",
                                branchPath + ".nextNodeId"));
                        }
                    }

                    break;

                case EndNodeDefinition endNode:
                    RequireValue(endNode.Result, nodePath + ".result", "End node result is required.", errors);
                    break;
            }
        }

        return errors.Count == 0 ? QuestValidationResult.Success() : QuestValidationResult.Failure(errors);
    }

    private static void ValidateChoices(
        IReadOnlyList<ChoiceDefinition> choices,
        IReadOnlyDictionary<string, NodeDefinition> nodes,
        string path,
        List<QuestValidationError> errors)
    {
        if (choices.Count == 0)
        {
            errors.Add(new QuestValidationError("choice.list.empty", "Node must contain at least one choice.", path));
            return;
        }

        var choiceIds = new HashSet<string>(StringComparer.Ordinal);

        for (var choiceIndex = 0; choiceIndex < choices.Count; choiceIndex++)
        {
            var choice = choices[choiceIndex];
            var choicePath = $"{path}[{choiceIndex}]";

            RequireValue(choice.Id, choicePath + ".id", "Choice id is required.", errors);
            RequireValue(choice.Text, choicePath + ".text", "Choice text is required.", errors);
            RequireValue(choice.NextNodeId, choicePath + ".nextNodeId", "Choice next node id is required.", errors);

            if (!string.IsNullOrWhiteSpace(choice.Id) && !choiceIds.Add(choice.Id))
            {
                errors.Add(new QuestValidationError(
                    "choice.id.duplicate",
                    $"Choice id '{choice.Id}' must be unique within a node.",
                    choicePath + ".id"));
            }

            if (!string.IsNullOrWhiteSpace(choice.NextNodeId) && !nodes.ContainsKey(choice.NextNodeId))
            {
                errors.Add(new QuestValidationError(
                    "choice.next_node.missing",
                    $"Choice next node '{choice.NextNodeId}' does not exist.",
                    choicePath + ".nextNodeId"));
            }

            ValidateConditions(choice.Conditions ?? Array.Empty<ConditionDefinition>(), choicePath + ".conditions", errors);
            ValidateEffects(choice.Effects ?? Array.Empty<EffectDefinition>(), choicePath + ".effects", errors);
        }
    }

    private static void ValidateConditions(
        IReadOnlyList<ConditionDefinition> conditions,
        string path,
        List<QuestValidationError> errors)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            var condition = conditions[index];
            var conditionPath = $"{path}[{index}]";

            RequireValue(condition.Target, conditionPath + ".target", "Condition target is required.", errors);
            RequireValue(condition.Operator, conditionPath + ".operator", "Condition operator is required.", errors);
        }
    }

    private static void ValidateEffects(
        IReadOnlyList<EffectDefinition> effects,
        string path,
        List<QuestValidationError> errors)
    {
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            var effectPath = $"{path}[{index}]";

            RequireValue(effect.Type, effectPath + ".type", "Effect type is required.", errors);
            RequireValue(effect.Target, effectPath + ".target", "Effect target is required.", errors);
        }
    }

    private static void RequireText(IReadOnlyList<string> text, string path, List<QuestValidationError> errors)
    {
        if (text.Count == 0)
        {
            errors.Add(new QuestValidationError("node.text.empty", "Node text must contain at least one line.", path));
            return;
        }

        for (var lineIndex = 0; lineIndex < text.Count; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(text[lineIndex]))
            {
                errors.Add(new QuestValidationError(
                    "node.text.line_empty",
                    "Text line must not be empty.",
                    $"{path}[{lineIndex}]"));
            }
        }
    }

    private static void RequireValue(string? value, string path, string message, List<QuestValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new QuestValidationError("validation.required", message, path));
        }
    }
}
