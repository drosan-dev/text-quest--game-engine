using System.Xml.Linq;

namespace TextQuest.Tests;

public sealed class ArchitectureSmokeTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    /// <summary>
    /// Проверяет, что демонстрационный JSON-квест присутствует в каталоге с тестовыми данными.
    /// </summary>
    [Fact]
    public void DemoQuestFixture_ShouldExist()
    {
        // Arrange
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");

        // Act

        // Assert
        Assert.True(File.Exists(questPath), $"Expected demo quest fixture at '{questPath}'.");
    }

    /// <summary>
    /// Проверяет, что доменный проект не содержит зависимости на CLI-проект.
    /// </summary>
    [Fact]
    public void DomainProject_ShouldNotReferenceCliProject()
    {
        // Arrange

        // Act
        var references = ReadProjectReferences(Path.Combine(RepositoryRoot, "src", "TextQuest.Domain", "TextQuest.Domain.csproj"));

        // Assert
        Assert.DoesNotContain(references, reference => reference.Contains("TextQuest.Cli", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Проверяет, что CLI-проект зависит только от application- и contracts-проектов.
    /// </summary>
    [Fact]
    public void CliProject_ShouldDependOnlyOnApplicationAndContracts()
    {
        // Arrange

        // Act
        var references = ReadProjectReferences(Path.Combine(RepositoryRoot, "src", "TextQuest.Cli", "TextQuest.Cli.csproj"));

        // Assert
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
