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
    /// Проверяет, что authoring loader корректно компилирует демонстрационный квест.
    /// </summary>
    [Fact]
    public async Task AuthoringLoadAsync_ShouldCompileDemoQuest()
    {
        var loader = new AuthoringQuestLoader();
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.author.yml");

        var definition = await loader.LoadAsync(questPath);

        Assert.Equal("demo_cell", definition.QuestId);
        Assert.Equal("intro", definition.StartNodeId);
        Assert.Equal(5, definition.Nodes.Count);

        var introNode = Assert.IsType<TextQuest.Domain.Models.TextNodeDefinition>(definition.Nodes["intro"]);
        Assert.NotEmpty(introNode.Choices);
        var introChoice = introNode.Choices[0];
        Assert.NotNull(introChoice.Effects);
        Assert.Equal("search_straw", introChoice.Id);
        Assert.Equal("set_flag", Assert.Single(introChoice.Effects!).Type);
    }

    /// <summary>
    /// Проверяет, что authoring YAML и runtime JSON дают одинаковую доменную модель для демо-квеста.
    /// </summary>
    [Fact]
    public async Task AuthoringLoadAsync_ShouldMatchRuntimeJsonDemoQuest()
    {
        var authoringLoader = new AuthoringQuestLoader();
        var runtimeLoader = new JsonQuestLoader();
        var authoringQuestPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.author.yml");
        var runtimeQuestPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");

        var authoringDefinition = await authoringLoader.LoadAsync(authoringQuestPath);
        var runtimeDefinition = await runtimeLoader.LoadAsync(runtimeQuestPath);

        Assert.Equal(runtimeDefinition.QuestId, authoringDefinition.QuestId);
        Assert.Equal(runtimeDefinition.Version, authoringDefinition.Version);
        Assert.Equal(runtimeDefinition.Title, authoringDefinition.Title);
        Assert.Equal(runtimeDefinition.StartNodeId, authoringDefinition.StartNodeId);
        Assert.Equal(runtimeDefinition.InitialVariables, authoringDefinition.InitialVariables);
        Assert.Equal(runtimeDefinition.InitialFlags, authoringDefinition.InitialFlags);
        Assert.Equal(runtimeDefinition.Nodes.Keys.OrderBy(key => key, StringComparer.Ordinal), authoringDefinition.Nodes.Keys.OrderBy(key => key, StringComparer.Ordinal));

        var runtimeIntroNode = Assert.IsType<TextQuest.Domain.Models.TextNodeDefinition>(runtimeDefinition.Nodes["intro"]);
        var authoringIntroNode = Assert.IsType<TextQuest.Domain.Models.TextNodeDefinition>(authoringDefinition.Nodes["intro"]);
        Assert.Equal(runtimeIntroNode.Choices.Select(choice => choice.Id), authoringIntroNode.Choices.Select(choice => choice.Id));

        var runtimeDecisionNode = Assert.IsType<TextQuest.Domain.Models.DecisionNodeDefinition>(runtimeDefinition.Nodes["corridor_decision"]);
        var authoringDecisionNode = Assert.IsType<TextQuest.Domain.Models.DecisionNodeDefinition>(authoringDefinition.Nodes["corridor_decision"]);
        Assert.Equal(runtimeDecisionNode.Choices.Select(choice => choice.Id), authoringDecisionNode.Choices.Select(choice => choice.Id));
    }

    /// <summary>
    /// Проверяет, что authoring loader требует явный choice id.
    /// </summary>
    [Fact]
    public async Task AuthoringLoadAsync_ShouldRejectChoiceWithoutExplicitId()
    {
        const string quest = """
        questId: stable-ids
        version: 1.0.0
        title: Stable Ids
        start: intro
        scenes:
          intro:
            text: Hello
            choices:
              - text: Осмотреть стол
                goto: end
          end:
            text: Done
            result: ok
        """;

        var loader = new AuthoringQuestLoader();
        var questPath = await CreateTempQuestFileAsync(quest);

        var exception = await Assert.ThrowsAsync<QuestValidationException>(() => loader.LoadAsync(questPath));

        Assert.Contains(exception.Errors, error => error.Code == "choice.id.required");
    }

    /// <summary>
    /// Проверяет, что quoted negation в YAML корректно компилируется.
    /// </summary>
    [Fact]
    public async Task AuthoringLoadAsync_ShouldCompileQuotedNegationCondition()
    {
        const string quest = """
        questId: negation-demo
        version: 1.0.0
        title: Negation Demo
        start: intro
        flags:
          hasKey: false
        scenes:
          intro:
            text: Hello
            choices:
              - id: wait
                text: Wait
                when: '!hasKey'
                goto: end
          end:
            text: Done
            result: ok
        """;

        var loader = new AuthoringQuestLoader();
        var questPath = await CreateTempQuestFileAsync(quest);

        var definition = await loader.LoadAsync(questPath);

        var introNode = Assert.IsType<TextQuest.Domain.Models.TextNodeDefinition>(definition.Nodes["intro"]);
        var choice = Assert.Single(introNode.Choices);
        var condition = Assert.Single(choice.Conditions!);
        Assert.Equal("hasKey", condition.Target);
        Assert.Equal("==", condition.Operator);
        Assert.Equal(false, condition.Value);
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

    /// <summary>
    /// Проверяет, что authoring loader отклоняет невалидный sugar для effect.
    /// </summary>
    [Fact]
    public async Task AuthoringLoadAsync_ShouldRejectInvalidEffectExpression()
    {
        const string invalidQuest = """
        questId: broken-authoring
        version: 1.0.0
        title: Broken Authoring
        start: intro
        vars:
          resolve: 0
        scenes:
          intro:
            text: Hello
            choices:
              - id: go
                text: Go
                do: resolve += nope
                goto: end
          end:
            text: Done
            result: ok
        """;

        var loader = new AuthoringQuestLoader();
        var questPath = await CreateTempQuestFileAsync(invalidQuest);

        var exception = await Assert.ThrowsAsync<QuestValidationException>(() => loader.LoadAsync(questPath));

        Assert.Contains(exception.Errors, error => error.Code == "effect.expression.invalid");
    }

    /// <summary>
    /// Проверяет, что опечатки в ключах authoring YAML не игнорируются молча.
    /// </summary>
    [Fact]
    public async Task AuthoringLoadAsync_ShouldRejectUnknownYamlProperty()
    {
        const string invalidQuest = """
        questId: typo-demo
        version: 1.0.0
        title: Typo Demo
        start: intro
        scenes:
          intro:
            tpye: end
            text: Done
            result: ok
        """;

        var loader = new AuthoringQuestLoader();
        var questPath = await CreateTempQuestFileAsync(invalidQuest);

        var exception = await Assert.ThrowsAsync<QuestValidationException>(() => loader.LoadAsync(questPath));

        Assert.Contains(exception.Errors, error => error.Code == "authoring.syntax");
        Assert.Contains("tpye", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> CreateTempQuestFileAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"textquest-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
