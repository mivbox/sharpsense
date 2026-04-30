using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Inheritors.Abstractions;

/// <summary>
/// Resolves direct derived classes or interface implementers for a persisted node so MCP and CLI read-side features
/// can answer inheritance questions without re-running Roslyn analysis.
/// </summary>
public interface IInheritorFinder
{
    /// <summary>
    /// Returns the direct class inheritors or implementers for the supplied persisted class or interface node id using
    /// the current inheritance edge semantics stored in the knowledge graph.
    /// </summary>
    /// <param name="query">The persisted base-class node lookup.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<CodeNodeResult[]> GetInheritors(
        GetInheritorsQuery query,
        CancellationToken ct);
}
