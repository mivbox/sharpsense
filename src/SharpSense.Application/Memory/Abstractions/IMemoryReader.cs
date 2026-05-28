using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Memory.Abstractions;

/// <summary>
/// Reads persistent semantic memory entries for one or more current code-node handles so higher-level slices can
/// enrich structural read models without coupling themselves to EF Core, SQLite JSON parsing, or the FQDN-to-node-id
/// bridging rules.
/// </summary>
public interface IMemoryReader
{
    /// <summary>
    /// Loads the semantic memories attached to the supplied current code-node ids and returns them keyed by the
    /// requesting node id after stale-state evaluation against the node's current body hash.
    /// </summary>
    Task<IReadOnlyDictionary<int, MemoryNode[]>> GetNodeMemories(
        IReadOnlyCollection<int> nodeIds,
        CancellationToken ct);
}
