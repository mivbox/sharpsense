using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Trace.Abstractions;

/// <summary>Reads trace nodes and dependencies from the selected workspace.</summary>
public interface ITraceNavigator
{
    /// <summary>Resolves a persisted node ID or symbol identifier, returning null when absent.</summary>
    Task<CodeNodeResult?> GetRootNode(string identifier, CancellationToken ct);

    /// <summary>Returns immediate downstream nodes using the query's edge filter.</summary>
    Task<CodeNodeResult[]> GetCallees(TraceQuery query, CancellationToken ct);

    /// <summary>Returns dependencies between the supplied visible trace nodes matching the optional edge filter.</summary>
    Task<ImpactedDependencyEdge[]> GetDependencies(
        IReadOnlyCollection<CodeNodeResult> nodes,
        EdgeType[]? includedEdgeTypes,
        CancellationToken ct);
}
