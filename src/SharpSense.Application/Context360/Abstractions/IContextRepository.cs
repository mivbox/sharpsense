using SharpSense.Application.Context360.Models;

namespace SharpSense.Application.Context360.Abstractions;

/// <summary>
/// Reads the persisted target node and its immediate architectural breadth buckets from the knowledge graph so the
/// Application layer can consume a fully shaped Context360 response without leaking EF Core details or persistence
/// entities past the Dependency Inversion boundary.
/// </summary>
public interface IContextRepository
{
    /// <summary>
     /// Loads the target node plus its immediate incoming and outgoing breadth buckets for a persisted node id.
     /// </summary>
    /// <param name="nodeId">The persisted integer code-node handle.</param>
    /// <param name="maxRelated">The maximum number of related rows to return per bucket.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<Context360Result?> GetNodeContext(
        int nodeId,
        int maxRelated,
        CancellationToken ct);
}
