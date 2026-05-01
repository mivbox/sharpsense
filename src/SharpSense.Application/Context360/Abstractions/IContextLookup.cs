using SharpSense.Application.Context360.Models;

namespace SharpSense.Application.Context360.Abstractions;

/// <summary>
/// Reads the persisted target node and its immediate architectural breadth buckets from the knowledge graph so the
/// Application-layer Context360 orchestrator can assemble one shared CLI/MCP response without leaking EF Core details.
/// </summary>
public interface IContextLookup
{
    /// <summary>
    /// Loads the target node plus its immediate incoming and outgoing breadth buckets for a persisted node id.
    /// </summary>
    /// <param name="nodeId">The persisted integer code-node handle.</param>
    /// <param name="maxRelated">The maximum number of related rows to return per bucket.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<Context360LookupResult?> GetNodeContext(
        int nodeId,
        int maxRelated,
        CancellationToken ct);
}
