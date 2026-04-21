using TextQuest.Application.Abstractions;
using TextQuest.Domain.Models;
using System.Text.Json;

namespace TextQuest.Infrastructure;

/// <summary>
/// Сохраняет игровые состояния в JSON-файлы и восстанавливает их обратно.
/// </summary>
public sealed class FileSystemSaveStore : ISaveStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string savesDirectory;

    public FileSystemSaveStore(string? savesDirectory = null)
    {
        this.savesDirectory = string.IsNullOrWhiteSpace(savesDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TextQuest", "saves")
            : Path.GetFullPath(savesDirectory);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string saveId, GameState gameState, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(saveId);
        ArgumentNullException.ThrowIfNull(gameState);

        Directory.CreateDirectory(savesDirectory);

        var targetPath = GetSaveFilePath(saveId);
        var tempPath = targetPath + ".tmp";
        var payload = SaveGameState.FromDomain(gameState);

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken);
        }

        if (File.Exists(targetPath))
        {
            File.Move(tempPath, targetPath, overwrite: true);
            return;
        }

        File.Move(tempPath, targetPath);
    }

    /// <inheritdoc />
    public async Task<GameState?> LoadAsync(string saveId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(saveId);

        var targetPath = GetSaveFilePath(saveId);
        if (!File.Exists(targetPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(targetPath);
        var payload = await JsonSerializer.DeserializeAsync<SaveGameState>(stream, SerializerOptions, cancellationToken);

        if (payload is null)
        {
            throw new InvalidDataException($"Save '{saveId}' is empty or malformed.");
        }

        return payload.ToDomain();
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
