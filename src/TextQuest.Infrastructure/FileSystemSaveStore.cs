using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TextQuest.Application.Abstractions;
using TextQuest.Domain.Models;

namespace TextQuest.Infrastructure;

/// <summary>
/// Сохраняет игровые состояния в JSON-файлы и восстанавливает их обратно.
/// </summary>
public sealed class FileSystemSaveStore : ISaveStore
{
    private static readonly EventId SaveWriteStartedEvent = new(3000, "SaveWriteStarted");
    private static readonly EventId SaveWritePathResolvedEvent = new(3001, "SaveWritePathResolved");
    private static readonly EventId SaveWrittenEvent = new(3002, "SaveWritten");
    private static readonly EventId SaveWriteFailedEvent = new(3003, "SaveWriteFailed");
    private static readonly EventId SaveLoadStartedEvent = new(3004, "SaveLoadStarted");
    private static readonly EventId SaveLoadPathResolvedEvent = new(3005, "SaveLoadPathResolved");
    private static readonly EventId SaveNotFoundEvent = new(3006, "SaveNotFound");
    private static readonly EventId SaveMalformedEvent = new(3007, "SaveMalformed");
    private static readonly EventId SaveLoadedEvent = new(3008, "SaveLoaded");
    private static readonly EventId SaveLoadFailedEvent = new(3009, "SaveLoadFailed");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string savesDirectory;
    private readonly ILogger<FileSystemSaveStore> _logger;

    public FileSystemSaveStore(string? savesDirectory = null, ILogger<FileSystemSaveStore>? logger = null)
    {
        this.savesDirectory = string.IsNullOrWhiteSpace(savesDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TextQuest", "saves")
            : Path.GetFullPath(savesDirectory);
        _logger = logger ?? NullLogger<FileSystemSaveStore>.Instance;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string saveId, GameState gameState, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(saveId);
        ArgumentNullException.ThrowIfNull(gameState);

        _logger.LogInformation(SaveWriteStartedEvent, "Saving game state {SaveId} for quest {QuestId} at node {CurrentNodeId}", saveId, gameState.QuestId, gameState.CurrentNodeId);

        Directory.CreateDirectory(savesDirectory);

        var targetPath = GetSaveFilePath(saveId);
        var tempPath = targetPath + ".tmp";
        var payload = SaveGameState.FromDomain(gameState);

        _logger.LogDebug(SaveWritePathResolvedEvent, "Resolved save paths for {SaveId}: target {TargetPath}, temp {TempPath}", saveId, targetPath, tempPath);

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken);
            }

            if (File.Exists(targetPath))
            {
                File.Move(tempPath, targetPath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }

            _logger.LogInformation(SaveWrittenEvent, "Saved game state {SaveId} to {TargetPath}", saveId, targetPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(SaveWriteFailedEvent, exception, "Failed to save game state {SaveId} to {TargetPath}", saveId, targetPath);
            throw;
        }
        catch (IOException exception)
        {
            _logger.LogError(SaveWriteFailedEvent, exception, "Failed to save game state {SaveId} to {TargetPath}", saveId, targetPath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<GameState?> LoadAsync(string saveId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(saveId);

        var targetPath = GetSaveFilePath(saveId);
        _logger.LogInformation(SaveLoadStartedEvent, "Loading game state {SaveId}", saveId);
        _logger.LogDebug(SaveLoadPathResolvedEvent, "Resolved save path for {SaveId}: {TargetPath}", saveId, targetPath);

        if (!File.Exists(targetPath))
        {
            _logger.LogWarning(SaveNotFoundEvent, "Save {SaveId} was not found at {TargetPath}", saveId, targetPath);
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(targetPath);
            var payload = await JsonSerializer.DeserializeAsync<SaveGameState>(stream, SerializerOptions, cancellationToken);

            if (payload is null)
            {
                _logger.LogWarning(SaveMalformedEvent, "Save {SaveId} at {TargetPath} is empty or malformed", saveId, targetPath);
                throw new InvalidDataException($"Save '{saveId}' is empty or malformed.");
            }

            var gameState = payload.ToDomain();
            _logger.LogInformation(SaveLoadedEvent, "Loaded game state {SaveId} for quest {QuestId} at node {CurrentNodeId} with status {Status}", saveId, gameState.QuestId, gameState.CurrentNodeId, gameState.Status);
            return gameState;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(SaveMalformedEvent, exception, "Save {SaveId} at {TargetPath} contains malformed JSON", saveId, targetPath);
            throw;
        }
        catch (InvalidDataException exception)
        {
            _logger.LogWarning(SaveMalformedEvent, exception, "Save {SaveId} at {TargetPath} failed validation during load", saveId, targetPath);
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(SaveLoadFailedEvent, exception, "Failed to load game state {SaveId} from {TargetPath}", saveId, targetPath);
            throw;
        }
        catch (IOException exception)
        {
            _logger.LogError(SaveLoadFailedEvent, exception, "Failed to load game state {SaveId} from {TargetPath}", saveId, targetPath);
            throw;
        }
    }

    private string GetSaveFilePath(string saveId)
    {
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            if (saveId.Contains(invalidCharacter))
            {
                throw new ArgumentException($"Save id '{saveId}' contains invalid file name characters.", nameof(saveId));
            }
        }

        return Path.Combine(savesDirectory, saveId + ".json");
    }

    private sealed record SaveGameState(
        string QuestId,
        string QuestVersion,
        string CurrentNodeId,
        Dictionary<string, int> Variables,
        Dictionary<string, bool> Flags,
        int RandomSeed,
        Dictionary<string, int>? RandomSelections,
        string[] VisitedNodeIds,
        SaveDecisionRecord[] DecisionHistory,
        string Status)
    {
        public static SaveGameState FromDomain(GameState gameState)
        {
            return new SaveGameState(
                gameState.QuestId,
                gameState.QuestVersion,
                gameState.CurrentNodeId,
                gameState.Variables.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                gameState.Flags.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                gameState.RandomSeed,
                gameState.RandomSelections.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                gameState.VisitedNodeIds.ToArray(),
                gameState.DecisionHistory.Select(SaveDecisionRecord.FromDomain).ToArray(),
                gameState.Status.ToString());
        }

        public GameState ToDomain()
        {
            if (!Enum.TryParse<TextQuest.Domain.Enums.GameStatus>(Status, ignoreCase: false, out var status))
            {
                throw new InvalidDataException($"Save contains unsupported game status '{Status}'.");
            }

            return new GameState(
                RequireValue(QuestId, "questId"),
                RequireValue(QuestVersion, "questVersion"),
                RequireValue(CurrentNodeId, "currentNodeId"),
                new Dictionary<string, int>(Variables ?? new Dictionary<string, int>(), StringComparer.Ordinal),
                new Dictionary<string, bool>(Flags ?? new Dictionary<string, bool>(), StringComparer.Ordinal),
                RandomSeed,
                new Dictionary<string, int>(RandomSelections ?? new Dictionary<string, int>(), StringComparer.Ordinal),
                VisitedNodeIds ?? [],
                (DecisionHistory ?? []).Select(record => record.ToDomain()).ToArray(),
                status);
        }

        private static string RequireValue(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"Save is missing required field '{fieldName}'.");
            }

            return value;
        }
    }

    private sealed record SaveDecisionRecord(string NodeId, string ChoiceId)
    {
        public static SaveDecisionRecord FromDomain(DecisionRecord record) => new(record.NodeId, record.ChoiceId);

        public DecisionRecord ToDomain() => new(NodeId, ChoiceId);
    }
}
