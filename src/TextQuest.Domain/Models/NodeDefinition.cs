using TextQuest.Domain.Enums;

namespace TextQuest.Domain.Models;

public abstract record NodeDefinition(string Id, NodeType Type, IReadOnlyList<string> Text);
