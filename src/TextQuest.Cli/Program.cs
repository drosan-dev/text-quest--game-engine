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

        var definition = await loader.LoadAsync(options.QuestPath);
        var session = await runtime.StartNewGameAsync(definition);

        while (true)
        {
            RenderState(session.PresentableState);

            if (session.PresentableState.IsCompleted)
            {
                Console.WriteLine($"\nКвест завершен. Результат: {session.PresentableState.Result ?? "unknown"}");
                return 0;
            }

            Console.Write("\n> ");
            var input = Console.ReadLine();
            if (input is null)
            {
                return 0;
            }

            var command = input.Trim();
            if (command.Length == 0)
            {
                Console.WriteLine("Введите номер выбора или команду: save [id], load [id], exit.");
                continue;
            }

            if (string.Equals(command, "exit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Выход из игры.");
                return 0;
            }

            if (TryParseSaveCommand(command, out var saveId))
            {
                await saveStore.SaveAsync(saveId, session.GameState);
                Console.WriteLine($"Сохранение '{saveId}' записано.");
                continue;
            }

            if (TryParseLoadCommand(command, out saveId))
            {
                var savedState = await saveStore.LoadAsync(saveId);
                if (savedState is null)
                {
                    Console.WriteLine($"Сохранение '{saveId}' не найдено.");
                    continue;
                }

                session = await runtime.RestoreAsync(definition, savedState);
                Console.WriteLine($"Сохранение '{saveId}' загружено.");
                continue;
            }

            if (!int.TryParse(command, out var choiceNumber))
            {
                Console.WriteLine("Неверный ввод. Используйте номер выбора или команды save/load/exit.");
                continue;
            }

            if (choiceNumber < 1 || choiceNumber > session.PresentableState.Choices.Count)
            {
                Console.WriteLine("Выбор вне диапазона доступных вариантов.");
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
                throw new ArgumentException($"Unknown argument '{args[index]}'. Supported arguments: --quest <path>, --quest-format <runtime-json|authoring-yaml>, --saves-dir <path>.");
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
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported quest input format."),
    };
}

static QuestInputFormat ParseQuestFormat(string value)
{
    return value.Trim().ToLowerInvariant() switch
    {
        "runtime-json" or "runtime" or "json" => QuestInputFormat.RuntimeJson,
        "authoring-yaml" or "authoring" or "author" => QuestInputFormat.AuthoringYaml,
        _ => throw new ArgumentException($"Unsupported quest format '{value}'. Supported values: runtime-json, authoring-yaml."),
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
    var fileName = format == QuestInputFormat.AuthoringYaml ? "demo-quest.author.yml" : "demo-quest.json";
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "content", "quests", fileName));
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

static void RenderState(PresentableState state)
{
    Console.WriteLine();
    Console.WriteLine(state.Title);
    Console.WriteLine(new string('=', state.Title.Length));

    foreach (var textBlock in state.TextBlocks)
    {
        Console.WriteLine(textBlock);
    }

    if (state.IsCompleted)
    {
        return;
    }

    Console.WriteLine();
    for (var index = 0; index < state.Choices.Count; index++)
    {
        var choice = state.Choices[index];
        Console.WriteLine($"{index + 1}. {choice.Text}");
    }

    Console.WriteLine("Команды: save [id], load [id], exit");
}

internal enum QuestInputFormat
{
    RuntimeJson,
    AuthoringYaml,
}

internal sealed record CliOptions(string QuestPath, string? SavesDirectory, QuestInputFormat QuestFormat);
