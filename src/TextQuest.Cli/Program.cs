using Microsoft.Extensions.Logging;
using TextQuest.Application;
using TextQuest.Application.Abstractions;
using TextQuest.Application.Models;
using TextQuest.Content.Json;
using TextQuest.Frontends.Contracts;
using TextQuest.Infrastructure;
using System.Text.Json;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    using var loggerFactory = CreateLoggerFactory();
    var logger = loggerFactory.CreateLogger("TextQuest.Cli.Program");

    try
    {
        var options = ParseOptions(args);
        IQuestLoader loader = CreateQuestLoader(options.QuestFormat, loggerFactory);
        var textRenderer = new TextRenderer();
        ITextQuestRuntime runtime = new TextQuestRuntime(textRenderer, loggerFactory.CreateLogger<TextQuestRuntime>());
        ISaveStore saveStore = new FileSystemSaveStore(options.SavesDirectory, loggerFactory.CreateLogger<FileSystemSaveStore>());
        var ui = TerminalUi.Create();

        var definition = await loader.LoadAsync(options.QuestPath);
        var session = await runtime.StartNewGameAsync(definition);

        var status = TerminalStatus.None;
        var selectedIndex = 0;

        using var uiSession = ui.BeginSession();

        while (true)
        {
            if (ui.IsInteractive)
            {
                ui.RenderScreen(session.PresentableState, selectedIndex, status);

                if (session.PresentableState.IsCompleted)
                {
                    status = new TerminalStatus(TerminalStatusKind.Success, $"Квест завершен. Результат: {session.PresentableState.Result ?? "unknown"} (Q для выхода)");
                    ui.RenderScreen(session.PresentableState, selectedIndex, status);
                    while (true)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                        {
                            return 0;
                        }
                    }
                }

                var action = ui.ReadAction(session.PresentableState, ref selectedIndex);
                switch (action.Kind)
                {
                    case TerminalActionKind.NavigationChanged:
                        status = TerminalStatus.None;
                        continue;

                    case TerminalActionKind.Exit:
                        return 0;

                    case TerminalActionKind.Save:
                    {
                        var saveId = action.Argument ?? "quick";
                        await saveStore.SaveAsync(saveId, session.GameState);
                        status = new TerminalStatus(TerminalStatusKind.Success, $"Сохранение '{saveId}' записано.");
                        continue;
                    }

                    case TerminalActionKind.Load:
                    {
                        var saveId = action.Argument ?? "quick";
                        var savedState = await saveStore.LoadAsync(saveId);
                        if (savedState is null)
                        {
                            status = new TerminalStatus(TerminalStatusKind.Warning, $"Сохранение '{saveId}' не найдено.");
                            continue;
                        }

                        session = await runtime.RestoreAsync(definition, savedState);
                        selectedIndex = 0;
                        status = new TerminalStatus(TerminalStatusKind.Success, $"Сохранение '{saveId}' загружено.");
                        continue;
                    }

                    case TerminalActionKind.Choose:
                    {
                        if (action.Argument is null)
                        {
                            status = new TerminalStatus(TerminalStatusKind.Error, "Некорректный выбор.");
                            continue;
                        }

                        var beforeNodeId = session.PresentableState.CurrentNodeId;
                        var beforeChoices = session.PresentableState.Choices;
                        var chosenText = beforeChoices.FirstOrDefault(choice => string.Equals(choice.Id, action.Argument, StringComparison.Ordinal))?.Text
                            ?? action.Argument;

                        session = await runtime.ApplyChoiceAsync(definition, session.GameState, action.Argument);

                        var afterNodeId = session.PresentableState.CurrentNodeId;
                        if (!string.Equals(beforeNodeId, afterNodeId, StringComparison.Ordinal))
                        {
                            selectedIndex = 0;
                        }
                        else
                        {
                            selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, session.PresentableState.Choices.Count - 1));
                        }

                        status = new TerminalStatus(TerminalStatusKind.Success, $"Сделано: {chosenText}");
                        continue;
                    }
                }

                continue;
            }

            ui.RenderStateLineMode(session.PresentableState);

            if (session.PresentableState.IsCompleted)
            {
                ui.WriteSuccess($"Квест завершен. Результат: {session.PresentableState.Result ?? "unknown"}");
                return 0;
            }

            ui.WritePrompt();
            var input = Console.ReadLine();
            if (input is null)
            {
                return 0;
            }

            var command = input.Trim();
            if (command.Length == 0)
            {
                ui.WriteInfo("Введите номер выбора или команду: save [id], load [id], exit.");
                continue;
            }

            if (string.Equals(command, "exit", StringComparison.OrdinalIgnoreCase))
            {
                ui.WriteInfo("Выход из игры.");
                return 0;
            }

            if (TryParseSaveCommand(command, out var saveIdLineMode))
            {
                await saveStore.SaveAsync(saveIdLineMode, session.GameState);
                ui.WriteSuccess($"Сохранение '{saveIdLineMode}' записано.");
                continue;
            }

            if (TryParseLoadCommand(command, out saveIdLineMode))
            {
                var savedState = await saveStore.LoadAsync(saveIdLineMode);
                if (savedState is null)
                {
                    ui.WriteWarning($"Сохранение '{saveIdLineMode}' не найдено.");
                    continue;
                }

                session = await runtime.RestoreAsync(definition, savedState);
                ui.WriteSuccess($"Сохранение '{saveIdLineMode}' загружено.");
                continue;
            }

            if (!int.TryParse(command, out var choiceNumber))
            {
                ui.WriteWarning("Неверный ввод. Используйте номер выбора или команды save/load/exit.");
                continue;
            }

            if (choiceNumber < 1 || choiceNumber > session.PresentableState.Choices.Count)
            {
                ui.WriteWarning("Выбор вне диапазона доступных вариантов.");
                continue;
            }

            var selectedChoice = session.PresentableState.Choices[choiceNumber - 1];
            session = await runtime.ApplyChoiceAsync(definition, session.GameState, selectedChoice.Id);
        }
    }
    catch (Exception exception) when (exception is QuestValidationException or InvalidChoiceException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or InvalidDataException or JsonException)
    {
        logger.LogError(exception, "CLI execution failed");
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

static ILoggerFactory CreateLoggerFactory()
{
    var minimumLevel = ParseLogLevel(Environment.GetEnvironmentVariable("TEXTQUEST_LOG_LEVEL")) ?? LogLevel.Warning;

    return LoggerFactory.Create(builder =>
    {
        builder.SetMinimumLevel(minimumLevel);
        builder.AddProvider(new StructuredConsoleLoggerProvider());
    });
}

static LogLevel? ParseLogLevel(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level)
        ? level
        : null;
}

static CliOptions ParseOptions(string[] args)
{
    string? questPath = null;
    string? savesDirectory = null;
    var questFormat = QuestInputFormat.RuntimeJson;

    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--quest":
                questPath = ReadOptionValue(args, ref index, "--quest");
                break;

            case "--saves-dir":
                savesDirectory = ReadOptionValue(args, ref index, "--saves-dir");
                break;

            case "--quest-format":
                questFormat = ParseQuestFormat(ReadOptionValue(args, ref index, "--quest-format"));
                break;

            default:
                throw new ArgumentException($"Unknown argument '{args[index]}'. Supported arguments: --quest <path>, --quest-format <runtime-json|authoring-yaml|timeline-yaml>, --saves-dir <path>.");
        }
    }

    return new CliOptions(
        questPath is null ? GetDefaultQuestPath(questFormat) : Path.GetFullPath(questPath),
        savesDirectory is null ? null : Path.GetFullPath(savesDirectory),
        questFormat);
}

static IQuestLoader CreateQuestLoader(QuestInputFormat format, ILoggerFactory loggerFactory)
{
    return format switch
    {
        QuestInputFormat.RuntimeJson => new JsonQuestLoader(loggerFactory.CreateLogger<JsonQuestLoader>()),
        QuestInputFormat.AuthoringYaml => new AuthoringQuestLoader(loggerFactory.CreateLogger<AuthoringQuestLoader>()),
        QuestInputFormat.TimelineYaml => new TimelineCampaignLoader(loggerFactory.CreateLogger<TimelineCampaignLoader>()),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported quest input format."),
    };
}

static QuestInputFormat ParseQuestFormat(string value)
{
    return value.Trim().ToLowerInvariant() switch
    {
        "runtime-json" or "runtime" or "json" => QuestInputFormat.RuntimeJson,
        "authoring-yaml" or "authoring" or "author" => QuestInputFormat.AuthoringYaml,
        "timeline-yaml" or "timeline" => QuestInputFormat.TimelineYaml,
        _ => throw new ArgumentException($"Unsupported quest format '{value}'. Supported values: runtime-json, authoring-yaml, timeline-yaml."),
    };
}

static string ReadOptionValue(string[] args, ref int index, string optionName)
{
    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
    {
        throw new ArgumentException($"Argument '{optionName}' requires a value.");
    }

    index++;
    return args[index];
}

static string GetDefaultQuestPath(QuestInputFormat format)
{
    var fileName = format switch
    {
        QuestInputFormat.AuthoringYaml => "demo-quest.author.yml",
        QuestInputFormat.TimelineYaml => Path.Combine("demo-campaign", "campaign.yml"),
        _ => "demo-quest.json",
    };

    var folder = format == QuestInputFormat.TimelineYaml ? "timeline" : "quests";
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "content", folder, fileName));
}

static bool TryParseSaveCommand(string input, out string saveId)
{
    return TryParseNamedCommand(input, "save", out saveId);
}

static bool TryParseLoadCommand(string input, out string saveId)
{
    return TryParseNamedCommand(input, "load", out saveId);
}

static bool TryParseNamedCommand(string input, string commandName, out string argument)
{
    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0 || !string.Equals(parts[0], commandName, StringComparison.OrdinalIgnoreCase))
    {
        argument = string.Empty;
        return false;
    }

    argument = parts.Length > 1 ? parts[1] : "quick";
    return true;
}

internal enum QuestInputFormat
{
    RuntimeJson,
    AuthoringYaml,
    TimelineYaml,
}

internal sealed record CliOptions(string QuestPath, string? SavesDirectory, QuestInputFormat QuestFormat);
