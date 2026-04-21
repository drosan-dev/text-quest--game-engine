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
    /// Проверяет, что application-слой зависит только от доменной модели и frontend-контрактов.
    /// </summary>
    [Fact]
    public void ApplicationProject_ShouldDependOnlyOnAllowedProjects()
    {
        var allowedReferences = new[]
        {
            "TextQuest.Domain.csproj",
            "TextQuest.Frontends.Contracts.csproj",
        };

        var references = ReadProjectReferences(Path.Combine(RepositoryRoot, "src", "TextQuest.Application", "TextQuest.Application.csproj"));

        Assert.Equal(allowedReferences.Length, references.Count);
        Assert.All(references, reference => Assert.Contains(allowedReferences, allowed => reference.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Проверяет, что проект frontend-контрактов остается независимым от остальных слоев.
    /// </summary>
    [Fact]
    public void FrontendContractsProject_ShouldNotReferenceOtherProjects()
    {
        var references = ReadProjectReferences(Path.Combine(RepositoryRoot, "src", "TextQuest.Frontends.Contracts", "TextQuest.Frontends.Contracts.csproj"));

        Assert.Empty(references);
    }

    /// <summary>
     /// Проверяет, что CLI-проект зависит только от допустимых application-level адаптеров и контрактов.
     /// </summary>
    [Fact]
    public void CliProject_ShouldDependOnlyOnAllowedProjects()
    {
        // Arrange
        var allowedReferences = new[]
        {
            "TextQuest.Application.csproj",
            "TextQuest.Content.Json.csproj",
            "TextQuest.Infrastructure.csproj",
        };

        // Act
        var references = ReadProjectReferences(Path.Combine(RepositoryRoot, "src", "TextQuest.Cli", "TextQuest.Cli.csproj"));

        // Assert
        Assert.Equal(allowedReferences.Length, references.Count);
        Assert.All(references, reference => Assert.Contains(allowedReferences, allowed => reference.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Проверяет, что JSON-адаптер контента не зависит от infrastructure или CLI.
    /// </summary>
    [Fact]
    public void ContentJsonProject_ShouldNotReferenceCliOrInfrastructure()
    {
        var references = ReadProjectReferences(Path.Combine(RepositoryRoot, "src", "TextQuest.Content.Json", "TextQuest.Content.Json.csproj"));

        Assert.DoesNotContain(references, reference => reference.Contains("TextQuest.Cli", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, reference => reference.Contains("TextQuest.Infrastructure", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Проверяет, что файловое хранилище зависит только от application/domain и не знает о CLI или JSON-контенте.
    /// </summary>
    [Fact]
    public void InfrastructureProject_ShouldNotReferenceCliOrContentJson()
    {
        var references = ReadProjectReferences(Path.Combine(RepositoryRoot, "src", "TextQuest.Infrastructure", "TextQuest.Infrastructure.csproj"));

        Assert.DoesNotContain(references, reference => reference.Contains("TextQuest.Cli", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, reference => reference.Contains("TextQuest.Content.Json", StringComparison.OrdinalIgnoreCase));
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
