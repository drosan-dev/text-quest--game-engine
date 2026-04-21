using System.Diagnostics;

namespace TextQuest.Tests;

public sealed class CliIntegrationTests : IDisposable
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static readonly string BuildConfiguration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
    private static readonly string TargetFramework = new DirectoryInfo(AppContext.BaseDirectory).Name;
    private readonly string savesDirectory = Path.Combine(Path.GetTempPath(), $"textquest-cli-saves-{Guid.NewGuid():N}");

    [Fact]
    public async Task Cli_ShouldCompleteDemoQuestAndUseSaveLoadCommands()
    {
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");
        var process = StartCliProcess(questPath, savesDirectory);

        await process.StandardInput.WriteLineAsync("1");
        await process.StandardInput.WriteLineAsync("save slot-a");
        await process.StandardInput.WriteLineAsync("load slot-a");
        await process.StandardInput.WriteLineAsync("1");
        process.StandardInput.Close();

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Пробуждение в камере", standardOutput, StringComparison.Ordinal);
        Assert.Contains("Сохранение 'slot-a' записано.", standardOutput, StringComparison.Ordinal);
        Assert.Contains("Сохранение 'slot-a' загружено.", standardOutput, StringComparison.Ordinal);
        Assert.Contains("Замок поддается, и вы бесшумно исчезаете в коридоре.", standardOutput, StringComparison.Ordinal);
        Assert.Contains("Результат: victory", standardOutput, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"QuestLoaded\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"GameStarted\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"ChoiceApplied\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"BranchTransitionResolved\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"SaveWritten\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"SaveLoaded\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"level\":\"Information\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"level\":\"Debug\"", standardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_ShouldExitOnExitCommand()
    {
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");
        var process = StartCliProcess(questPath, savesDirectory);

        await process.StandardInput.WriteLineAsync("exit");
        process.StandardInput.Close();

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Выход из игры.", standardOutput, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"QuestLoaded\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"GameStarted\"", standardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_ShouldUseQuickSlotWhenSaveAndLoadIdsAreOmitted()
    {
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");
        var process = StartCliProcess(questPath, savesDirectory);

        await process.StandardInput.WriteLineAsync("save");
        await process.StandardInput.WriteLineAsync("load");
        await process.StandardInput.WriteLineAsync("exit");
        process.StandardInput.Close();

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Сохранение 'quick' записано.", standardOutput, StringComparison.Ordinal);
        Assert.Contains("Сохранение 'quick' загружено.", standardOutput, StringComparison.Ordinal);
        Assert.Contains("Выход из игры.", standardOutput, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"SaveWritten\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"SaveLoaded\"", standardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_ShouldReportMissingSaveAndContinueRunning()
    {
        var questPath = Path.Combine(RepositoryRoot, "content", "quests", "demo-quest.json");
        var process = StartCliProcess(questPath, savesDirectory);

        await process.StandardInput.WriteLineAsync("load missing-slot");
        await process.StandardInput.WriteLineAsync("exit");
        process.StandardInput.Close();

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Сохранение 'missing-slot' не найдено.", standardOutput, StringComparison.Ordinal);
        Assert.Contains("Выход из игры.", standardOutput, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"QuestLoaded\"", standardError, StringComparison.Ordinal);
        Assert.Contains("\"eventName\":\"GameStarted\"", standardError, StringComparison.Ordinal);
    }

    private static Process StartCliProcess(string questPath, string savesDirectory)
    {
        Directory.CreateDirectory(savesDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList =
            {
                Path.Combine(RepositoryRoot, "src", "TextQuest.Cli", "bin", BuildConfiguration, TargetFramework, "TextQuest.Cli.dll"),
                "--quest",
                questPath,
                "--saves-dir",
                savesDirectory,
            },
            WorkingDirectory = RepositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.Environment["TEXTQUEST_LOG_LEVEL"] = "Debug";

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start TextQuest CLI process.");
    }

    public void Dispose()
    {
        if (Directory.Exists(savesDirectory))
        {
            Directory.Delete(savesDirectory, recursive: true);
        }
    }
}
