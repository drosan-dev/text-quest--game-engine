using System.Xml.Linq;

namespace TextQuest.Tests;

public sealed class ArchitectureSmokeTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void DemoQuestFixture_ShouldExist()
    {
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");

        Assert.True(File.Exists(questPath), $"Expected demo quest fixture at '{questPath}'.");
    }

    [Fact]
    public void DomainProject_ShouldNotReferenceCliProject()
    {
        var references = ReadProjectReferences(Path.Combine(RepositoryRoot, "src", "TextQuest.Domain", "TextQuest.Domain.csproj"));

        Assert.DoesNotContain(references, reference => reference.Contains("TextQuest.Cli", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CliProject_ShouldDependOnlyOnApplicationAndContracts()
    {
        var references = ReadProjectReferences(Path.Combine(RepositoryRoot, "src", "TextQuest.Cli", "TextQuest.Cli.csproj"));

        Assert.Equal(2, references.Count);
        Assert.Contains(references, reference => reference.EndsWith("TextQuest.Application.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, reference => reference.EndsWith("TextQuest.Frontends.Contracts.csproj", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);

        return document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();
    }
}
