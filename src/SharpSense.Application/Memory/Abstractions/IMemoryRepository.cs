using FluentResults;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Memory.Abstractions;

/// <summary>
/// Persists and reads semantic memory for the current logical identity of a code node. The single repository
/// contract covers attach, delete, list-by-node, batch fetch by id, and intent-filtered reads so callers do
/// not have to thread a reader/writer pair through DI. Memories are immutable once attached: only
/// <see cref="AttachMemory"/> and <see cref="DeleteMemory"/> are supported; no update or replace member
/// exists on this contract.
/// </summary>
public interface IMemoryRepository
{
    /// <summary>
    /// Attaches a semantic memory payload to the supplied persisted code-node id after resolving the node to its
    /// current fully-qualified name and body hash. The <paramref name="intent"/> classifies the memory so it
    /// can be filtered at retrieval time.
    /// </summary>
    Task<Result<MemoryNode>> AttachMemory(
        int nodeId,
        string content,
        string[]? tags,
        MemoryIntent intent,
        CancellationToken ct);

    /// <summary>
    /// Removes a previously attached memory by its persistent <see cref="Guid"/> identifier. Returns a failed
    /// <see cref="Result"/> when no memory exists for the supplied id; never throws for expected domain failures.
    /// </summary>
    Task<Result> DeleteMemory(Guid memoryId, CancellationToken ct);

    /// <summary>
    /// Loads the semantic memories attached to the supplied current code-node ids and returns them keyed by the
    /// requesting node id after stale-state evaluation against the node's current body hash. When
    /// <paramref name="intents"/> is supplied, only memories whose intent matches one of the supplied values
    /// are returned.
    /// </summary>
    Task<IReadOnlyDictionary<int, MemoryNode[]>> GetNodeMemories(
        IReadOnlyCollection<int> nodeIds,
        IReadOnlyCollection<MemoryIntent>? intents,
        CancellationToken ct);

    /// <summary>
    /// Loads a single memory by its persistent <see cref="Guid"/>. Returns <see langword="null"/> when no memory
    /// exists for the supplied id so callers can distinguish "not found" from "errored" without exceptions.
    /// </summary>
    Task<MemoryNode?> GetMemory(Guid memoryId, CancellationToken ct);

    /// <summary>
    /// Loads a batch of memories by their persistent <see cref="Guid"/> identifiers. The returned dictionary
    /// contains every supplied id; missing ids map to <see langword="null"/>. Use this for multi-step trace
    /// content retrieval so a chatty agent pays one round-trip per batch instead of one per id.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, MemoryNode?>> GetMemories(
        IReadOnlyCollection<Guid> memoryIds,
        CancellationToken ct);
}
