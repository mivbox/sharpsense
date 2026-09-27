using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.CodeAnalysis;

internal sealed record KnowledgeGraphExtractionPayload(
    string TargetPath,
    IReadOnlyList<ProjectNode> Projects,
    IReadOnlyList<CodeNode> CodeNodes,
    IReadOnlyList<DependencyEdge> Edges,
    IReadOnlyList<string> Diagnostics);
