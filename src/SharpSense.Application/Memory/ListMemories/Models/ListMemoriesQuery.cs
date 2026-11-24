using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Memory.ListMemories.Models;

/// <summary>
/// Queries persisted memories across the selected workspace with optional intent, tag, staleness, text, and paging
/// filters.
/// </summary>
public sealed record ListMemoriesQuery(
    MemoryIntent[]? Intents = null,
    string[]? Tags = null,
    bool StaleOnly = false,
    string? SearchText = null,
    int Limit = 50,
    int Offset = 0);
