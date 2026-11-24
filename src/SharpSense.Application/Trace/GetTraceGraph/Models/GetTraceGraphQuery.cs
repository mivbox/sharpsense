using SharpSense.Application.Trace.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Trace.GetTraceGraph.Models;

/// <summary>Requests the graph reachable from an indexed node, within the supplied depth and optional edge filter.</summary>
public sealed record GetTraceGraphQuery(
    int NodeId,
    TraceDirection Direction = TraceDirection.Callee,
    int MaxDepth = 3,
    EdgeType[]? IncludedEdgeTypes = null);
