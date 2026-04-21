using TextQuest.Application;
using TextQuest.Application.Models;
using TextQuest.Content.Json;
using TextQuest.Domain.Enums;
using TextQuest.Domain.Models;

namespace TextQuest.Tests;

public sealed class TextQuestRuntimeTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public async Task StartNewGameAsync_ShouldReturnIntroPresentation()
    {
        var definition = await LoadDemoQuestAsync();
        var runtime = new TextQuestRuntime();

        var session = await runtime.StartNewGameAsync(definition);

        Assert.Equal("intro", session.GameState.CurrentNodeId);
        Assert.Equal(GameStatus.InProgress, session.GameState.Status);
        Assert.Equal(new[] { "intro" }, session.GameState.VisitedNodeIds);
        Assert.Equal(2, session.PresentableState.Choices.Count);
        Assert.False(session.PresentableState.IsCompleted);
    }

    [Fact]
    public async Task ApplyChoiceAsync_ShouldCompleteDemoQuestWithVictoryBranch()
    {
        var definition = await LoadDemoQuestAsync();
        var runtime = new TextQuestRuntime();

        var startedSession = await runtime.StartNewGameAsync(definition);
        var corridorSession = await runtime.ApplyChoiceAsync(definition, startedSession.GameState, "search_straw");
        var completedSession = await runtime.ApplyChoiceAsync(definition, corridorSession.GameState, "unlock_door");

        Assert.Equal("escape", completedSession.GameState.CurrentNodeId);
        Assert.Equal(GameStatus.Completed, completedSession.GameState.Status);
        Assert.True(completedSession.GameState.Flags["hasKey"]);
        Assert.Equal(new[] { "intro", "corridor_decision", "outcome_branch", "escape" }, completedSession.GameState.VisitedNodeIds);
        Assert.Equal("victory", completedSession.PresentableState.Result);
        Assert.True(completedSession.PresentableState.IsCompleted);
        Assert.Empty(completedSession.PresentableState.Choices);
    }

    [Fact]
    public async Task ApplyChoiceAsync_ShouldHideUnavailableChoicesBasedOnState()
    {
        var definition = await LoadDemoQuestAsync();
        var runtime = new TextQuestRuntime();

        var startedSession = await runtime.StartNewGameAsync(definition);
        var corridorSession = await runtime.ApplyChoiceAsync(definition, startedSession.GameState, "call_guard");

        Assert.Equal("corridor_decision", corridorSession.GameState.CurrentNodeId);
        Assert.Equal(1, corridorSession.GameState.Variables["resolve"]);
        Assert.Contains(corridorSession.PresentableState.Choices, choice => choice.Id == "force_door");
        Assert.DoesNotContain(corridorSession.PresentableState.Choices, choice => choice.Id == "unlock_door");
    }

    [Fact]
    public async Task ApplyChoiceAsync_ShouldFollowDefaultBranchWhenConditionsDoNotMatch()
    {
        var definition = await LoadDemoQuestAsync();
        var runtime = new TextQuestRuntime();

        var startedSession = await runtime.StartNewGameAsync(definition);
        var corridorSession = await runtime.ApplyChoiceAsync(definition, startedSession.GameState, "call_guard");
        var completedSession = await runtime.ApplyChoiceAsync(definition, corridorSession.GameState, "force_door");

        Assert.Equal("captured", completedSession.GameState.CurrentNodeId);
        Assert.Equal(GameStatus.Completed, completedSession.GameState.Status);
        Assert.False(completedSession.GameState.Flags["hasKey"]);
        Assert.Equal(1, completedSession.GameState.Variables["resolve"]);
        Assert.Equal(new[] { "intro", "corridor_decision", "outcome_branch", "captured" }, completedSession.GameState.VisitedNodeIds);
        Assert.Equal("defeat", completedSession.PresentableState.Result);
        Assert.True(completedSession.PresentableState.IsCompleted);
    }

    [Fact]
    public async Task ApplyChoiceAsync_ShouldApplyVariableAndFlagEffectsWithoutAmbiguousSetType()
    {
        const string questJson = """
        {
          "questId": "effects-demo",
          "version": "1.0.0",
          "title": "Effects Demo",
          "startNodeId": "intro",
          "variables": {
            "resolve": 0
          },
          "flags": {
            "hasKey": false
          },
          "nodes": [
            {
              "id": "intro",
              "type": "text",
              "text": ["Hello"],
              "choices": [
                {
                  "id": "go",
                  "text": "Go",
                  "effects": [
                    {
                      "type": "set_variable",
                      "target": "resolve",
                      "value": 2
                    },
                    {
                      "type": "set_flag",
                      "target": "hasKey",
                      "value": true
                    }
                  ],
                  "nextNodeId": "end"
                }
              ]
            },
            {
              "id": "end",
              "type": "end",
              "text": ["Done"],
              "result": "ok"
            }
          ]
        }
        """;

        var definition = await LoadQuestFromJsonAsync(questJson);
        var runtime = new TextQuestRuntime();

        var startedSession = await runtime.StartNewGameAsync(definition);
        var completedSession = await runtime.ApplyChoiceAsync(definition, startedSession.GameState, "go");

        Assert.Equal(2, completedSession.GameState.Variables["resolve"]);
        Assert.True(completedSession.GameState.Flags["hasKey"]);
        Assert.Equal(GameStatus.Completed, completedSession.GameState.Status);
    }

    [Fact]
    public async Task ApplyChoiceAsync_ShouldRejectUnavailableChoice()
    {
        var definition = await LoadDemoQuestAsync();
        var runtime = new TextQuestRuntime();

        var startedSession = await runtime.StartNewGameAsync(definition);
        var corridorSession = await runtime.ApplyChoiceAsync(definition, startedSession.GameState, "call_guard");

        var exception = await Assert.ThrowsAsync<InvalidChoiceException>(() => runtime.ApplyChoiceAsync(definition, corridorSession.GameState, "unlock_door"));

        Assert.Equal("unlock_door", exception.ChoiceId);
        Assert.Equal("corridor_decision", exception.NodeId);
    }

    [Fact]
    public async Task RestoreAsync_ShouldRebuildPresentationFromExistingState()
    {
        var definition = await LoadDemoQuestAsync();
        var runtime = new TextQuestRuntime();
        var restoredState = new GameState(
            definition.QuestId,
            definition.Version,
            "corridor_decision",
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["resolve"] = 0,
            },
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["hasKey"] = true,
            },
            new[] { "intro", "corridor_decision" },
            new[] { new DecisionRecord("intro", "search_straw") },
            GameStatus.InProgress);

        var session = await runtime.RestoreAsync(definition, restoredState);

        Assert.Equal("corridor_decision", session.PresentableState.CurrentNodeId);
        Assert.Contains(session.PresentableState.Choices, choice => choice.Id == "unlock_door");
        Assert.DoesNotContain(session.PresentableState.Choices, choice => choice.Id == "force_door");
    }

    private static Task<QuestDefinition> LoadDemoQuestAsync()
    {
        var loader = new JsonQuestLoader();
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");
        return loader.LoadAsync(questPath);
    }

    private static async Task<QuestDefinition> LoadQuestFromJsonAsync(string content)
    {
        var loader = new JsonQuestLoader();
        var path = Path.Combine(Path.GetTempPath(), $"textquest-runtime-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, content);
        return await loader.LoadAsync(path);
    }
}
