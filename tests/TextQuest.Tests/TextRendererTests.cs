using TextQuest.Application;
using TextQuest.Domain.Enums;
using TextQuest.Domain.Models;

namespace TextQuest.Tests;

public sealed class TextRendererTests
{
    private readonly TextRenderer _renderer = new();

    [Fact]
    public void Render_ShouldSubstituteVariables()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int> { ["resolve"] = 5 },
            new Dictionary<string, bool>(),
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "Your resolve is {{resolve}}.";
        var result = _renderer.Render(template, gameState);

        Assert.Equal("Your resolve is 5.", result);
    }

    [Fact]
    public void Render_ShouldEvaluateConditionalTrue()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int>(),
            new Dictionary<string, bool> { ["hasKey"] = true },
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "You have [if hasKey]a key[endif].";
        var result = _renderer.Render(template, gameState);

        Assert.Equal("You have a key.", result);
    }

    [Fact]
    public void Render_ShouldEvaluateConditionalFalse()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int>(),
            new Dictionary<string, bool> { ["hasKey"] = false },
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "You have [if hasKey]a key[endif].";
        var result = _renderer.Render(template, gameState);

        Assert.Equal("You have .", result);
    }

    [Fact]
    public void Render_ShouldEvaluateNegatedConditionalTrue()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int>(),
            new Dictionary<string, bool> { ["hasKey"] = false },
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "You have [if !hasKey]no key[endif].";
        var result = _renderer.Render(template, gameState);

        Assert.Equal("You have no key.", result);
    }

    [Fact]
    public void Render_ShouldHandleMultipleTemplates()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int> { ["resolve"] = 10 },
            new Dictionary<string, bool> { ["hasKey"] = true, ["hasSword"] = false },
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "Resolve: {{resolve}}. [if hasKey]Key found.[endif] [if !hasSword]No sword.[endif]";
        var result = _renderer.Render(template, gameState);

        Assert.Equal("Resolve: 10. Key found. No sword.", result);
    }

    [Fact]
    public void Render_ShouldThrowForMissingVariable()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int>(),
            new Dictionary<string, bool>(),
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "Value: {{missing}}.";

        Assert.Throws<InvalidOperationException>(() => _renderer.Render(template, gameState));
    }

    [Fact]
    public void Render_ShouldThrowForMissingFlag()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int>(),
            new Dictionary<string, bool>(),
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "[if missing]text[endif]";

        Assert.Throws<InvalidOperationException>(() => _renderer.Render(template, gameState));
    }

    [Fact]
    public void Render_ShouldHandleNestedTemplatesInConditionals()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int> { ["resolve"] = 5 },
            new Dictionary<string, bool> { ["hasKey"] = true },
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "[if hasKey]You have {{resolve}} resolve points.[endif]";
        var result = _renderer.Render(template, gameState);

        Assert.Equal("You have 5 resolve points.", result);
    }

    [Fact]
    public void Render_ShouldHandleMultipleNestedConditionals()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int> { ["resolve"] = 10 },
            new Dictionary<string, bool> { ["hasKey"] = true, ["hasSword"] = false },
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "[if hasKey]Key: yes[endif][if hasSword] Sword: yes[endif][if !hasSword] No sword[endif]. Resolve: {{resolve}}.";
        var result = _renderer.Render(template, gameState);

        Assert.Equal("Key: yes No sword. Resolve: 10.", result);
    }

    [Fact]
    public void Render_ShouldHandleEmptyConditionalContent()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int>(),
            new Dictionary<string, bool> { ["flag"] = true },
            ["node1"],
            [],
            GameStatus.InProgress);

        var template = "Before[if flag][endif]After";
        var result = _renderer.Render(template, gameState);

        Assert.Equal("BeforeAfter", result);
    }

    [Fact]
    public void Render_ShouldThrowForExcessiveNestingDepth()
    {
        var gameState = new GameState(
            "test",
            "1.0",
            "node1",
            new Dictionary<string, int>(),
            new Dictionary<string, bool> { ["a"] = true },
            ["node1"],
            [],
            GameStatus.InProgress);

        // Create a deeply nested template with 11 levels
        var template = string.Concat(Enumerable.Repeat("[if a]", 11)) + "text" + string.Concat(Enumerable.Repeat("[endif]", 11));

        Assert.Throws<InvalidOperationException>(() => _renderer.Render(template, gameState));
    }
}