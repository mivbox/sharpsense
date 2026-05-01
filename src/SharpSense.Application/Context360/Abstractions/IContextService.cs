using SharpSense.Application.Context360.Models;

namespace SharpSense.Application.Context360.Abstractions;

/// <summary>
/// Assembles the immediate architectural breadth around a persisted node id so CLI and MCP routes can share the exact
/// same Context360 read logic without duplicating orchestration in route code.
/// </summary>
public interface IContextService
{
    /// <summary>
    /// Returns the target node plus capped incoming and outgoing breadth buckets for the current request.
    /// </summary>
    /// <param name="nodeId">The persisted integer code-node handle.</param>
    /// <param name="maxRelated">The maximum number of related rows to return per bucket.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<Context360Result> GetNodeContext(
        int nodeId,
        int maxRelated,
        CancellationToken ct);
}
