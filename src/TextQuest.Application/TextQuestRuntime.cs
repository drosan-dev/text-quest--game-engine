using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private static readonly EventId GameStartRequestedEvent = new(2000, "GameStartRequested");
    private static readonly EventId GameStartedEvent = new(2001, "GameStarted");
    private static readonly EventId ChoiceApplyRequestedEvent = new(2002, "ChoiceApplyRequested");
    private static readonly EventId ChoiceRejectedCompletedEvent = new(2003, "ChoiceRejectedCompleted");
    private static readonly EventId ChoiceRejectedUnavailableEvent = new(2004, "ChoiceRejectedUnavailable");
    private static readonly EventId ChoiceAppliedEvent = new(2005, "ChoiceApplied");
    private static readonly EventId BranchTransitionResolvedEvent = new(2006, "BranchTransitionResolved");
    private static readonly EventId GameRestoreRequestedEvent = new(2007, "GameRestoreRequested");
    private static readonly EventId GameRestoredEvent = new(2008, "GameRestored");
    private static readonly EventId QuestStateMismatchEvent = new(2009, "QuestStateMismatch");

    private readonly ILogger<TextQuestRuntime> _logger;
    private readonly ITextRenderer _textRenderer;
    private readonly TextPoolTemplateExpander _textPoolExpander;
    private readonly IRandomProvider _randomProvider;

    public TextQuestRuntime()
        : this(new TextRenderer(), new DeterministicRandomProvider(), null)
    {
    }

    public TextQuestRuntime(ITextRenderer textRenderer, ILogger<TextQuestRuntime>? logger = null)
        : this(textRenderer, new DeterministicRandomProvider(), logger)
    {
    }

    public TextQuestRuntime(ITextRenderer textRenderer, IRandomProvider randomProvider, ILogger<TextQuestRuntime>? logger = null)
    {
        _textRenderer = textRenderer ?? throw new ArgumentNullException(nameof(textRenderer));
        ArgumentNullException.ThrowIfNull(randomProvider);
        _randomProvider = randomProvider;
        _textPoolExpander = new TextPoolTemplateExpander(randomProvider);
        _logger = logger ?? NullLogger<TextQuestRuntime>.Instance;
    }

    /// <inheritdoc />
    public Task<RuntimeSession> StartNewGameAsync(QuestDefinition definition, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(definition);

        _logger.LogInformation(GameStartRequestedEvent, "Starting new game for quest {QuestId} version {QuestVersion} from node {StartNodeId}", definition.QuestId, definition.Version, definition.StartNodeId);

        var seed = _randomProvider.CreateSeed();

        var gameState = new GameState(
            definition.QuestId,
            definition.Version,
            definition.StartNodeId,
            CloneVariables(definition.InitialVariables),
            CloneFlags(definition.InitialFlags),
            seed,
            new Dictionary<string, int>(StringComparer.Ordinal),
            [definition.StartNodeId],
            [],
            GameStatus.InProgress);

        var session = CreateSession(definition, gameState);
        _logger.LogInformation(GameStartedEvent, "Started game for quest {QuestId} at node {CurrentNodeId} with {ChoiceCount} available choices", definition.QuestId, session.GameState.CurrentNodeId, session.PresentableState.Choices.Count);
        return Task.FromResult(session);
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

        var matchingState = EnsureQuestMatches(definition, gameState);
        var normalizedState = NormalizeState(definition, matchingState);
        var currentNode = GetNode(definition, normalizedState.CurrentNodeId);

        _logger.LogInformation(ChoiceApplyRequestedEvent, "Applying choice {ChoiceId} in quest {QuestId} from node {CurrentNodeId}", choiceId, definition.QuestId, currentNode.Id);

        if (normalizedState.Status == GameStatus.Completed || currentNode is EndNodeDefinition)
        {
            _logger.LogWarning(ChoiceRejectedCompletedEvent, "Rejected choice {ChoiceId} because quest {QuestId} is already completed at node {CurrentNodeId}", choiceId, definition.QuestId, currentNode.Id);
            throw new InvalidOperationException("Cannot apply a choice after the quest is completed.");
        }

        var availableChoices = GetAvailableChoices(currentNode, normalizedState);
        var selectedChoice = availableChoices.FirstOrDefault(choice => string.Equals(choice.Id, choiceId, StringComparison.Ordinal));

        if (selectedChoice is null)
        {
            _logger.LogWarning(ChoiceRejectedUnavailableEvent, "Rejected unavailable choice {ChoiceId} in quest {QuestId} at node {CurrentNodeId}", choiceId, definition.QuestId, currentNode.Id);
            throw new InvalidChoiceException(choiceId, currentNode.Id);
        }

        var updatedState = new GameState(
            normalizedState.QuestId,
            normalizedState.QuestVersion,
            selectedChoice.NextNodeId,
            ApplyEffectsToVariables(normalizedState.Variables, selectedChoice.Effects),
            ApplyEffectsToFlags(normalizedState.Flags, selectedChoice.Effects),
            normalizedState.RandomSeed,
            normalizedState.RandomSelections,
            Append(normalizedState.VisitedNodeIds, selectedChoice.NextNodeId),
            Append(normalizedState.DecisionHistory, new DecisionRecord(currentNode.Id, selectedChoice.Id)),
            GameStatus.InProgress);

        var session = CreateSession(definition, updatedState);
        _logger.LogInformation(ChoiceAppliedEvent, "Applied choice {ChoiceId} in quest {QuestId}: {FromNodeId} -> {ToNodeId} with status {Status}", selectedChoice.Id, definition.QuestId, currentNode.Id, session.GameState.CurrentNodeId, session.GameState.Status);
        return Task.FromResult(session);
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

        _logger.LogInformation(GameRestoreRequestedEvent, "Restoring game for quest {QuestId} version {QuestVersion} from node {CurrentNodeId}", gameState.QuestId, gameState.QuestVersion, gameState.CurrentNodeId);

        var session = CreateSession(definition, EnsureQuestMatches(definition, gameState));
        _logger.LogInformation(GameRestoredEvent, "Restored game for quest {QuestId} at node {CurrentNodeId} with status {Status}", session.GameState.QuestId, session.GameState.CurrentNodeId, session.GameState.Status);
        return Task.FromResult(session);
    }

    private RuntimeSession CreateSession(QuestDefinition definition, GameState gameState)
    {
        var normalizedState = NormalizeState(definition, gameState);
        var node = GetNode(definition, normalizedState.CurrentNodeId);

        var currentState = normalizedState;
        var renderedTextBlocks = new string[node.Text.Count];

        for (var index = 0; index < node.Text.Count; index++)
        {
            var (template, updatedState) = _textPoolExpander.Expand(node.Text[index], definition, currentState, $"node:{node.Id}:text:{index}");
            currentState = updatedState;
            renderedTextBlocks[index] = _textRenderer.Render(template, currentState);
        }

        var choices = CreateChoiceViewModels(node, currentState, definition, out currentState);

        return new RuntimeSession(
            currentState,
            new PresentableState(
                definition.QuestId,
                definition.Title,
                node.Id,
                renderedTextBlocks,
                choices,
                currentState.Status == GameStatus.Completed,
                node is EndNodeDefinition endNode ? endNode.Result : null));
    }

    private GameState NormalizeState(QuestDefinition definition, GameState gameState)
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
            _logger.LogDebug(BranchTransitionResolvedEvent, "Resolved branch transition in quest {QuestId}: {FromNodeId} -> {ToNodeId}", definition.QuestId, branchNode.Id, nextNodeId);
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

    private GameState EnsureQuestMatches(QuestDefinition definition, GameState gameState)
    {
        if (!string.Equals(definition.QuestId, gameState.QuestId, StringComparison.Ordinal)
            || !string.Equals(definition.Version, gameState.QuestVersion, StringComparison.Ordinal))
        {
            _logger.LogWarning(QuestStateMismatchEvent, "Game state quest identity mismatch. Expected {QuestId}/{QuestVersion}, got {StateQuestId}/{StateQuestVersion}", definition.QuestId, definition.Version, gameState.QuestId, gameState.QuestVersion);
            throw new InvalidOperationException("Game state does not belong to the provided quest definition.");
        }

        return gameState;
    }

    private string ResolveBranch(BranchNodeDefinition branchNode, GameState gameState)
    {
        foreach (var branch in branchNode.Branches)
        {
            if (branch.Conditions.All(condition => EvaluateCondition(condition, gameState.Variables, gameState.Flags)))
            {
                _logger.LogDebug(BranchTransitionResolvedEvent, "Matched conditional branch from {FromNodeId} to {ToNodeId}", branchNode.Id, branch.NextNodeId);
                return branch.NextNodeId;
            }
        }

        _logger.LogDebug(BranchTransitionResolvedEvent, "Falling back to default branch from {FromNodeId} to {ToNodeId}", branchNode.Id, branchNode.DefaultNextNodeId);
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

    private IReadOnlyList<ChoiceViewModel> CreateChoiceViewModels(
        NodeDefinition node,
        GameState gameState,
        QuestDefinition definition,
        out GameState updatedState)
    {
        var state = gameState;
        var viewModels = new List<ChoiceViewModel>();

        foreach (var choice in GetAvailableChoices(node, state))
        {
            var (template, nextState) = _textPoolExpander.Expand(choice.Text, definition, state, $"node:{node.Id}:choice:{choice.Id}");
            state = nextState;
            viewModels.Add(new ChoiceViewModel(choice.Id, _textRenderer.Render(template, state)));
        }

        updatedState = state;
        return viewModels.ToArray();
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
