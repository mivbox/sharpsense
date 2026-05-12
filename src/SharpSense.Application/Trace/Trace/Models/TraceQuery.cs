using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Trace.Trace.Models;

public sealed record TraceQuery(
    string Identifier,
    EdgeType[]? IncludedEdgeTypes = null);
