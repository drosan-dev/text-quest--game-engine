using TextQuest.Application.Models;
using TextQuest.Content.Json;

namespace TextQuest.Tests;

public sealed class JsonQuestContentTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    /// <summary>
    /// Проверяет, что загрузчик корректно читает демонстрационный квест из JSON-файла.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ShouldLoadDemoQuest()
    {
        // Arrange
        var loader = new JsonQuestLoader();
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");

        // Act
        var definition = await loader.LoadAsync(questPath);

        // Assert
        Assert.Equal("demo_cell", definition.QuestId);
        Assert.Equal("intro", definition.StartNodeId);
        Assert.Equal(5, definition.Nodes.Count);
        Assert.Contains("escape", definition.Nodes.Keys);
    }

    /// <summary>
    /// Проверяет, что загрузчик отклоняет квест со ссылкой выбора на отсутствующий узел.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ShouldRejectQuestWithMissingReferencedNode()
    {
        // Arrange
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

        // Act
        var exception = await Assert.ThrowsAsync<QuestValidationException>(() => loader.LoadAsync(questPath));

        // Assert
        Assert.Contains(exception.Errors, error => error.Code == "choice.next_node.missing");
        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Проверяет, что загрузчик отклоняет файл с синтаксически некорректным JSON.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ShouldRejectMalformedJson()
    {
        // Arrange
        const string malformedJson = "{ \"questId\": \"broken\", \"nodes\": [ }";

        var loader = new JsonQuestLoader();
        var questPath = await CreateTempQuestFileAsync(malformedJson);

        // Act
        var exception = await Assert.ThrowsAsync<QuestValidationException>(() => loader.LoadAsync(questPath));

        // Assert
        Assert.Contains(exception.Errors, error => error.Code == "json.syntax");
    }

    /// <summary>
    /// Проверяет, что загрузчик отклоняет квест с condition/effect, ссылающимися на неизвестное состояние.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ShouldRejectQuestWithUnknownConditionOrEffectTarget()
    {
        const string invalidQuest = """
        {
          "questId": "broken-targets",
          "version": "1.0.0",
          "title": "Broken Targets",
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
                  "conditions": [
                    {
                      "target": "missingFlag",
                      "operator": "==",
                      "value": true
                    }
                  ],
                  "effects": [
                    {
                      "type": "add",
                      "target": "missingVariable",
                      "value": 1
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

        var loader = new JsonQuestLoader();
        var questPath = await CreateTempQuestFileAsync(invalidQuest);

        var exception = await Assert.ThrowsAsync<QuestValidationException>(() => loader.LoadAsync(questPath));

        Assert.Contains(exception.Errors, error => error.Code == "condition.target.missing");
        Assert.Contains(exception.Errors, error => error.Code == "effect.target.variable_missing");
    }

    /// <summary>
    /// Проверяет, что загрузчик отклоняет квест с неподдерживаемым типом эффекта.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ShouldRejectQuestWithUnsupportedEffectType()
    {
        const string invalidQuest = """
        {
          "questId": "broken-effect-type",
          "version": "1.0.0",
          "title": "Broken Effect Type",
          "startNodeId": "intro",
          "variables": {
            "resolve": 0
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
                      "type": "set",
                      "target": "resolve",
                      "value": 1
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

        var loader = new JsonQuestLoader();
        var questPath = await CreateTempQuestFileAsync(invalidQuest);

        var exception = await Assert.ThrowsAsync<QuestValidationException>(() => loader.LoadAsync(questPath));

        Assert.Contains(exception.Errors, error => error.Code == "effect.type.invalid");
    }

    private static async Task<string> CreateTempQuestFileAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"textquest-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
