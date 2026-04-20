using TextQuest.Application.Models;
using TextQuest.Content.Json;

namespace TextQuest.Tests;

public sealed class JsonQuestContentTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public async Task LoadAsync_ShouldLoadDemoQuest()
    {
        var loader = new JsonQuestLoader();
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");

        var definition = await loader.LoadAsync(questPath);

        Assert.Equal("demo_cell", definition.QuestId);
        Assert.Equal("intro", definition.StartNodeId);
        Assert.Equal(5, definition.Nodes.Count);
        Assert.Contains("escape", definition.Nodes.Keys);
    }

    [Fact]
    public async Task LoadAsync_ShouldRejectQuestWithMissingReferencedNode()
    {
        const string invalidQuest = """
        {
          "questId": "broken",
          "version": "1.0.0",
          "title": "Broken Quest",
          "startNodeId": "intro",
          "nodes": [
            {
              "id": "intro",
              "type": "text",
              "text": ["Hello"],
              "choices": [
                {
                  "id": "go",
                  "text": "Go",
                  "nextNodeId": "missing"
                }
              ]
            }
          ]
        }
        """;

        var loader = new JsonQuestLoader();
        var questPath = await CreateTempQuestFileAsync(invalidQuest);

        var exception = await Assert.ThrowsAsync<QuestValidationException>(() => loader.LoadAsync(questPath));

        Assert.Contains(exception.Errors, error => error.Code == "choice.next_node.missing");
        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ShouldRejectMalformedJson()
    {
        const string malformedJson = "{ \"questId\": \"broken\", \"nodes\": [ }";

        var loader = new JsonQuestLoader();
        var questPath = await CreateTempQuestFileAsync(malformedJson);

        var exception = await Assert.ThrowsAsync<QuestValidationException>(() => loader.LoadAsync(questPath));

        Assert.Contains(exception.Errors, error => error.Code == "json.syntax");
    }

    private static async Task<string> CreateTempQuestFileAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"textquest-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
