using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.CodeAnalysis;

public sealed record KnowledgeGraphExtractionPayload(
    string SolutionPath,
    IReadOnlyList<ProjectNode> Projects,
    IReadOnlyList<CodeNode> CodeNodes,
    IReadOnlyList<DependencyEdge> Edges,
    IReadOnlyList<string> Diagnostics);
