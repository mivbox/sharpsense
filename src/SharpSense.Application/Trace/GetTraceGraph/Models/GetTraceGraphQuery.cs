using SharpSense.Application.Trace.Models;

namespace SharpSense.Application.Trace.GetTraceGraph.Models;

/// <summary>Requests the graph reachable from an indexed node, within the supplied depth.</summary>
public sealed record GetTraceGraphQuery(int NodeId, TraceDirection Direction = TraceDirection.Callee, int MaxDepth = 3);
