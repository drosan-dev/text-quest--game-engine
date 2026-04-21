using TextQuest.Application.Abstractions;
using TextQuest.Application.Models;
using TextQuest.Domain.Enums;
using TextQuest.Domain.Models;
using TextQuest.Frontends.Contracts;

namespace TextQuest.Application;

/// <summary>
/// Исполняет квест: создаёт сессии, применяет выборы игрока и нормализует переходы по веткам.
/// </summary>
public sealed class TextQuestRuntime : ITextQuestRuntime
{
    /// <inheritdoc />
    public Task<RuntimeSession> StartNewGameAsync(QuestDefinition definition, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(definition);

        var gameState = new GameState(
            definition.QuestId,
            definition.Version,
            definition.StartNodeId,
            CloneVariables(definition.InitialVariables),
            CloneFlags(definition.InitialFlags),
            [definition.StartNodeId],
            [],
            GameStatus.InProgress);

        return Task.FromResult(CreateSession(definition, gameState));
    }

    /// <inheritdoc />
    public Task<RuntimeSession> ApplyChoiceAsync(
        QuestDefinition definition,
        GameState gameState,
        string choiceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(gameState);

        if (string.IsNullOrWhiteSpace(choiceId))
        {
            throw new ArgumentException("Choice id must not be empty.", nameof(choiceId));
        }

        var normalizedState = NormalizeState(definition, EnsureQuestMatches(definition, gameState));
        var currentNode = GetNode(definition, normalizedState.CurrentNodeId);

        if (normalizedState.Status == GameStatus.Completed || currentNode is EndNodeDefinition)
        {
            throw new InvalidOperationException("Cannot apply a choice after the quest is completed.");
        }

        var availableChoices = GetAvailableChoices(currentNode, normalizedState);
        var selectedChoice = availableChoices.FirstOrDefault(choice => string.Equals(choice.Id, choiceId, StringComparison.Ordinal));

        if (selectedChoice is null)
        {
            throw new InvalidChoiceException(choiceId, currentNode.Id);
        }

        var updatedState = new GameState(
            normalizedState.QuestId,
            normalizedState.QuestVersion,
            selectedChoice.NextNodeId,
            ApplyEffectsToVariables(normalizedState.Variables, selectedChoice.Effects),
            ApplyEffectsToFlags(normalizedState.Flags, selectedChoice.Effects),
            Append(normalizedState.VisitedNodeIds, selectedChoice.NextNodeId),
            Append(normalizedState.DecisionHistory, new DecisionRecord(currentNode.Id, selectedChoice.Id)),
            GameStatus.InProgress);

        return Task.FromResult(CreateSession(definition, updatedState));
    }

    /// <inheritdoc />
    public Task<RuntimeSession> RestoreAsync(
        QuestDefinition definition,
        GameState gameState,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(gameState);

        return Task.FromResult(CreateSession(definition, EnsureQuestMatches(definition, gameState)));
    }

    private static RuntimeSession CreateSession(QuestDefinition definition, GameState gameState)
    {
        var normalizedState = NormalizeState(definition, gameState);
        var node = GetNode(definition, normalizedState.CurrentNodeId);

        return new RuntimeSession(
            normalizedState,
            new PresentableState(
                definition.QuestId,
                definition.Title,
                node.Id,
                node.Text,
                CreateChoiceViewModels(node, normalizedState),
                normalizedState.Status == GameStatus.Completed,
                node is EndNodeDefinition endNode ? endNode.Result : null));
    }

    private static GameState NormalizeState(QuestDefinition definition, GameState gameState)
    {
        var currentState = gameState;

        for (var step = 0; step <= definition.Nodes.Count; step++)
        {
            var currentNode = GetNode(definition, currentState.CurrentNodeId);

            if (currentNode is not BranchNodeDefinition branchNode)
            {
                return currentState with
                {
                    Status = currentNode is EndNodeDefinition ? GameStatus.Completed : GameStatus.InProgress,
                };
            }

            var nextNodeId = ResolveBranch(branchNode, currentState);
            currentState = currentState with
            {
                CurrentNodeId = nextNodeId,
                VisitedNodeIds = Append(currentState.VisitedNodeIds, nextNodeId),
                Status = GameStatus.InProgress,
            };
        }

        throw new InvalidOperationException("Branch resolution exceeded the number of quest nodes. Possible branch cycle detected.");
    }

    private static NodeDefinition GetNode(QuestDefinition definition, string nodeId)
    {
        if (!definition.Nodes.TryGetValue(nodeId, out var node))
        {
            throw new InvalidOperationException($"Node '{nodeId}' does not exist in quest '{definition.QuestId}'.");
        }

        return node;
    }

    private static GameState EnsureQuestMatches(QuestDefinition definition, GameState gameState)
    {
        if (!string.Equals(definition.QuestId, gameState.QuestId, StringComparison.Ordinal)
            || !string.Equals(definition.Version, gameState.QuestVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Game state does not belong to the provided quest definition.");
        }

        return gameState;
    }

    private static string ResolveBranch(BranchNodeDefinition branchNode, GameState gameState)
    {
        foreach (var branch in branchNode.Branches)
        {
            if (branch.Conditions.All(condition => EvaluateCondition(condition, gameState.Variables, gameState.Flags)))
            {
                return branch.NextNodeId;
            }
        }

        return branchNode.DefaultNextNodeId;
    }

    private static IReadOnlyList<ChoiceDefinition> GetAvailableChoices(NodeDefinition node, GameState gameState)
    {
        var allChoices = node switch
        {
            TextNodeDefinition textNode => textNode.Choices,
            DecisionNodeDefinition decisionNode => decisionNode.Choices,
            _ => Array.Empty<ChoiceDefinition>(),
        };

        return allChoices
            .Where(choice => (choice.Conditions ?? Array.Empty<ConditionDefinition>())
                .All(condition => EvaluateCondition(condition, gameState.Variables, gameState.Flags)))
            .ToArray();
    }

    private static IReadOnlyList<ChoiceViewModel> CreateChoiceViewModels(NodeDefinition node, GameState gameState)
    {
        return GetAvailableChoices(node, gameState)
            .Select(choice => new ChoiceViewModel(choice.Id, choice.Text))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, int> ApplyEffectsToVariables(
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyList<EffectDefinition>? effects)
    {
        var updatedVariables = CloneVariables(variables);

        foreach (var effect in effects ?? Array.Empty<EffectDefinition>())
        {
            switch (effect.Type)
            {
                case "add":
                    updatedVariables[effect.Target] = GetVariableValue(updatedVariables, effect.Target) + ConvertToInt32(effect.Value, effect.Target);
                    break;

                case "set_variable":
                    updatedVariables[effect.Target] = ConvertToInt32(effect.Value, effect.Target);
                    break;

                case "set_flag":
                    break;

                default:
                    throw new InvalidOperationException($"Effect type '{effect.Type}' is not supported.");
            }
        }

        return updatedVariables;
    }

    private static IReadOnlyDictionary<string, bool> ApplyEffectsToFlags(
        IReadOnlyDictionary<string, bool> flags,
        IReadOnlyList<EffectDefinition>? effects)
    {
        var updatedFlags = CloneFlags(flags);

        foreach (var effect in effects ?? Array.Empty<EffectDefinition>())
        {
            switch (effect.Type)
            {
                case "set_flag":
                    updatedFlags[effect.Target] = ConvertToBoolean(effect.Value, effect.Target);
                    break;

                case "add":
                case "set_variable":
                    break;

                default:
                    throw new InvalidOperationException($"Effect type '{effect.Type}' is not supported.");
            }
        }

        return updatedFlags;
    }

    private static bool EvaluateCondition(
        ConditionDefinition condition,
        IReadOnlyDictionary<string, int> variables,
        IReadOnlyDictionary<string, bool> flags)
    {
        if (flags.TryGetValue(condition.Target, out var flagValue))
        {
            var expected = ConvertToBoolean(condition.Value, condition.Target);

            return condition.Operator switch
            {
                "==" => flagValue == expected,
                "!=" => flagValue != expected,
                _ => throw new InvalidOperationException($"Operator '{condition.Operator}' is not supported for flag '{condition.Target}'."),
            };
        }

        if (variables.TryGetValue(condition.Target, out var variableValue))
        {
            var expected = ConvertToInt32(condition.Value, condition.Target);

            return condition.Operator switch
            {
                "==" => variableValue == expected,
                "!=" => variableValue != expected,
                ">" => variableValue > expected,
                ">=" => variableValue >= expected,
                "<" => variableValue < expected,
                "<=" => variableValue <= expected,
                _ => throw new InvalidOperationException($"Operator '{condition.Operator}' is not supported for variable '{condition.Target}'."),
            };
        }

        throw new InvalidOperationException($"Condition target '{condition.Target}' does not exist in the game state.");
    }

    private static Dictionary<string, int> CloneVariables(IReadOnlyDictionary<string, int> variables)
    {
        return variables.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, bool> CloneFlags(IReadOnlyDictionary<string, bool> flags)
    {
        return flags.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static IReadOnlyList<T> Append<T>(IReadOnlyList<T> source, T value)
    {
        var result = new T[source.Count + 1];
        for (var index = 0; index < source.Count; index++)
        {
            result[index] = source[index];
        }

        result[^1] = value;
        return result;
    }

    private static int GetVariableValue(IReadOnlyDictionary<string, int> variables, string target)
    {
        if (!variables.TryGetValue(target, out var value))
        {
            throw new InvalidOperationException($"Variable '{target}' does not exist in the game state.");
        }

        return value;
    }

    private static int ConvertToInt32(object? value, string target)
    {
        return value switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            decimal decimalValue => decimal.ToInt32(decimalValue),
            _ => throw new InvalidOperationException($"Value for '{target}' must be an integer."),
        };
    }

    private static bool ConvertToBoolean(object? value, string target)
    {
        return value switch
        {
            bool boolValue => boolValue,
            _ => throw new InvalidOperationException($"Value for '{target}' must be a boolean."),
        };
    }
}
