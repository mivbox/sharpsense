using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Models;

namespace SharpSense.Application.Trace.GetTraceGraph.Models;

/// <summary>A trace root, its reachable nodes and the dependencies between visible nodes.</summary>
/// <param name="Root">The indexed node from which traversal starts.</param>
/// <param name="Direction">The direction of traversal.</param>
/// <param name="Nodes">Reachable nodes reported by the traversal. Callee results exclude the root.</param>
/// <param name="Dependencies">Functional dependencies between visible nodes, including the root.</param>
/// <param name="Truncated">Whether callee traversal omitted nodes because it reached its node limit.</param>
public sealed record TraceGraphResult(
    CodeNodeResult Root,
    TraceDirection Direction,
    CodeNodeResult[] Nodes,
    ImpactedDependencyEdge[] Dependencies,
    bool Truncated = false);
