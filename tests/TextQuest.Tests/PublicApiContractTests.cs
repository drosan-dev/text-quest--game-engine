using TextQuest.Application;
using TextQuest.Application.Abstractions;
using TextQuest.Content.Json;
using TextQuest.Infrastructure;

namespace TextQuest.Tests;

public sealed class PublicApiContractTests : IDisposable
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private readonly string savesDirectory = Path.Combine(Path.GetTempPath(), $"textquest-contract-saves-{Guid.NewGuid():N}");

    [Fact]
    public async Task AdapterFlow_ShouldBeDriveableThroughPublicContractsOnly()
    {
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");
        IQuestLoader loader = new JsonQuestLoader();
        ITextQuestRuntime runtime = new TextQuestRuntime();
        ISaveStore saveStore = new FileSystemSaveStore(savesDirectory);

        var completedSession = await RunAdapterFlowAsync(loader, runtime, saveStore, questPath, "contract-slot");

        Assert.True(completedSession.PresentableState.IsCompleted);
        Assert.Equal("victory", completedSession.PresentableState.Result);
        Assert.Equal("escape", completedSession.GameState.CurrentNodeId);
    }

    [Fact]
    public async Task SaveLoadContract_ShouldRoundTripStateForRestoreFlow()
    {
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");
        IQuestLoader loader = new JsonQuestLoader();
        ITextQuestRuntime runtime = new TextQuestRuntime();
        ISaveStore saveStore = new FileSystemSaveStore(savesDirectory);
        var definition = await loader.LoadAsync(questPath);
        var startedSession = await runtime.StartNewGameAsync(definition);
        var savedSession = await runtime.ApplyChoiceAsync(definition, startedSession.GameState, startedSession.PresentableState.Choices[0].Id);

        await saveStore.SaveAsync("restore-slot", savedSession.GameState);
        var restoredState = await saveStore.LoadAsync("restore-slot");

        Assert.NotNull(restoredState);

        var restoredSession = await runtime.RestoreAsync(definition, restoredState!);

        Assert.Equal(savedSession.GameState.CurrentNodeId, restoredSession.GameState.CurrentNodeId);
        Assert.Equal(savedSession.GameState.Flags["hasKey"], restoredSession.GameState.Flags["hasKey"]);
        Assert.Equal(savedSession.PresentableState.CurrentNodeId, restoredSession.PresentableState.CurrentNodeId);
        Assert.Contains(restoredSession.PresentableState.Choices, choice => choice.Id == "unlock_door");
    }

    private static async Task<TextQuest.Application.Models.RuntimeSession> RunAdapterFlowAsync(
        IQuestLoader loader,
        ITextQuestRuntime runtime,
        ISaveStore saveStore,
        string questPath,
        string saveId)
    {
        var definition = await loader.LoadAsync(questPath);
        var session = await runtime.StartNewGameAsync(definition);

        session = await runtime.ApplyChoiceAsync(definition, session.GameState, session.PresentableState.Choices[0].Id);
        await saveStore.SaveAsync(saveId, session.GameState);

        var restoredState = await saveStore.LoadAsync(saveId);
        Assert.NotNull(restoredState);

        session = await runtime.RestoreAsync(definition, restoredState!);
        session = await runtime.ApplyChoiceAsync(definition, session.GameState, session.PresentableState.Choices[0].Id);

        return session;
    }

    public void Dispose()
    {
        if (Directory.Exists(savesDirectory))
        {
            Directory.Delete(savesDirectory, recursive: true);
        }
    }
}
