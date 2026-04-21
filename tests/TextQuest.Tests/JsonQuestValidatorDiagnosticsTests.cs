using TextQuest.Application.Models;
using TextQuest.Content.Json;
using TextQuest.Domain.Models;

namespace TextQuest.Tests;

public sealed class JsonQuestValidatorDiagnosticsTests
{
    [Fact]
    public void Validate_ShouldIncludeDiagnosticContextForChoiceProblems()
    {
        var validator = new JsonQuestValidator();
        var definition = new QuestDefinition(
            "quest",
            "1.0.0",
            "Quest",
            "intro",
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["resolve"] = 0,
            },
            new Dictionary<string, bool>(StringComparer.Ordinal),
            new Dictionary<string, NodeDefinition>(StringComparer.Ordinal)
            {
                ["intro"] = new TextNodeDefinition(
                    "intro",
                    ["Hello"],
                    [
                        new ChoiceDefinition(
                            "go",
                            "Go",
                            "missing",
                            [new ConditionDefinition("resolve", "==", true)],
                            [new EffectDefinition("add", "resolve", true)])
                    ]),
                ["end"] = new EndNodeDefinition("end", ["Done"], "ok"),
            });

        var result = validator.Validate(definition);

        Assert.False(result.IsValid);

        var nextNodeError = Assert.Single(result.Errors.Where(error => error.Code == "choice.next_node.missing"));
        Assert.Equal("intro", nextNodeError.NodeId);
        Assert.Equal("go", nextNodeError.ChoiceId);
        Assert.Equal("$.nodes['intro'].choices[0].nextNodeId", nextNodeError.Path);
        Assert.Equal("existing node id", nextNodeError.Expected);
        Assert.Equal("'missing'", nextNodeError.Actual);

        var conditionValueError = Assert.Single(result.Errors.Where(error => error.Code == "condition.value.invalid_for_variable"));
        Assert.Equal("$.nodes['intro'].choices[0].conditions[0].value", conditionValueError.Path);
        Assert.Equal("integer", conditionValueError.Expected);
        Assert.Equal("true", conditionValueError.Actual);

        var effectValueError = Assert.Single(result.Errors.Where(error => error.Code == "effect.value.invalid_for_variable"));
        Assert.Equal("$.nodes['intro'].choices[0].effects[0].value", effectValueError.Path);
        Assert.Equal("integer", effectValueError.Expected);
        Assert.Equal("true", effectValueError.Actual);
    }

    [Fact]
    public void QuestValidationException_ShouldGroupProblemsIntoSingleReport()
    {
        var errors = new[]
        {
            new QuestValidationError(
                "choice.next_node.missing",
                "Choice next node 'missing' does not exist.",
                "$.nodes['intro'].choices[0].nextNodeId",
                "intro",
                "go",
                "existing node id",
                "'missing'"),
            new QuestValidationError(
                "condition.value.invalid_for_variable",
                "Condition value for variable 'resolve' must be an integer.",
                "$.nodes['intro'].choices[0].conditions[0].value",
                "intro",
                "go",
                "integer",
                "true"),
            new QuestValidationError(
                "quest.start_node.missing",
                "Start node 'missing-start' does not exist.",
                "$.startNodeId",
                Expected: "existing node id",
                Actual: "'missing-start'")
        };

        var exception = new QuestValidationException(errors);

        Assert.Contains("Quest validation failed with 3 problem(s):", exception.Message, StringComparison.Ordinal);
        Assert.Contains("- nodeId='intro', choiceId='go'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("path=$.nodes['intro'].choices[0].nextNodeId; Choice next node 'missing' does not exist.; expected=existing node id; actual='missing'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("path=$.nodes['intro'].choices[0].conditions[0].value; Condition value for variable 'resolve' must be an integer.; expected=integer; actual=true", exception.Message, StringComparison.Ordinal);
        Assert.Contains("- path=$.startNodeId", exception.Message, StringComparison.Ordinal);
        Assert.Contains("path=$.startNodeId; Start node 'missing-start' does not exist.; expected=existing node id; actual='missing-start'", exception.Message, StringComparison.Ordinal);
    }
}
