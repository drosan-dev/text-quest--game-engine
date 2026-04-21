using TextQuest.Application;
using TextQuest.Content.Json;
using TextQuest.Domain.Enums;
using TextQuest.Domain.Models;
using TextQuest.Infrastructure;
using System.Text.Json;

namespace TextQuest.Tests;

public sealed class FileSystemSaveStoreTests : IDisposable
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private readonly string savesDirectory = Path.Combine(Path.GetTempPath(), $"textquest-saves-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_ShouldRoundTripGameState()
    {
        var store = new FileSystemSaveStore(savesDirectory);
        var gameState = new GameState(
            "demo_cell",
            "1.0.0",
            "corridor_decision",
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["resolve"] = 2,
            },
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["hasKey"] = true,
            },
            ["intro", "corridor_decision"],
            [new DecisionRecord("intro", "search_straw")],
            GameStatus.InProgress);

        await store.SaveAsync("slot-1", gameState);

        var loadedState = await store.LoadAsync("slot-1");

        Assert.NotNull(loadedState);
        AssertEquivalent(gameState, loadedState);
    }

    [Fact]
    public async Task LoadAsync_ShouldReturnNullWhenSaveDoesNotExist()
    {
        var store = new FileSystemSaveStore(savesDirectory);

        var loadedState = await store.LoadAsync("missing-slot");

        Assert.Null(loadedState);
    }

    [Fact]
    public async Task LoadAsync_ShouldRejectMalformedSaveFile()
    {
        Directory.CreateDirectory(savesDirectory);
        await File.WriteAllTextAsync(Path.Combine(savesDirectory, "broken.json"), "{\"questId\": ");

        var store = new FileSystemSaveStore(savesDirectory);

        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync("broken"));
    }

    [Fact]
    public async Task LoadAsync_ShouldRejectSaveWithMissingRequiredFields()
    {
        Directory.CreateDirectory(savesDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(savesDirectory, "incomplete.json"),
            """
            {
              "questId": "",
              "questVersion": "1.0.0",
              "currentNodeId": "intro",
              "variables": {},
              "flags": {},
              "visitedNodeIds": [],
              "decisionHistory": [],
              "status": "InProgress"
            }
            """);

        var store = new FileSystemSaveStore(savesDirectory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync("incomplete"));

        Assert.Contains("questId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveLoadRestoreFlow_ShouldContinueQuestFromSameNodeAndState()
    {
        var loader = new JsonQuestLoader();
        var runtime = new TextQuestRuntime();
        var store = new FileSystemSaveStore(savesDirectory);
        var definition = await loader.LoadAsync(Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json"));

        var startedSession = await runtime.StartNewGameAsync(definition);
        var savedSession = await runtime.ApplyChoiceAsync(definition, startedSession.GameState, "search_straw");
        await store.SaveAsync("demo-progress", savedSession.GameState);
        var loadedState = await store.LoadAsync("demo-progress");
        var restoredSession = await runtime.RestoreAsync(definition, loadedState!);
        var completedSession = await runtime.ApplyChoiceAsync(definition, restoredSession.GameState, "unlock_door");

        AssertEquivalent(savedSession.GameState, loadedState!);
        Assert.Equal("corridor_decision", restoredSession.GameState.CurrentNodeId);
        Assert.True(restoredSession.GameState.Flags["hasKey"]);
        Assert.Contains(restoredSession.PresentableState.Choices, choice => choice.Id == "unlock_door");
        Assert.Equal("escape", completedSession.GameState.CurrentNodeId);
        Assert.Equal(GameStatus.Completed, completedSession.GameState.Status);
    }

    private static void AssertEquivalent(GameState expected, GameState actual)
    {
        Assert.Equal(expected.QuestId, actual.QuestId);
        Assert.Equal(expected.QuestVersion, actual.QuestVersion);
        Assert.Equal(expected.CurrentNodeId, actual.CurrentNodeId);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Variables.OrderBy(pair => pair.Key), actual.Variables.OrderBy(pair => pair.Key));
        Assert.Equal(expected.Flags.OrderBy(pair => pair.Key), actual.Flags.OrderBy(pair => pair.Key));
        Assert.Equal(expected.VisitedNodeIds, actual.VisitedNodeIds);
        Assert.Equal(expected.DecisionHistory, actual.DecisionHistory);
    }

    public void Dispose()
    {
        if (Directory.Exists(savesDirectory))
        {
            Directory.Delete(savesDirectory, recursive: true);
        }
    }
}
