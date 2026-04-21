using JetBrains.Annotations;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Features.Indexing.Contracts;

[PublicAPI]
public sealed record ExtractedNodes(
    IReadOnlyList<ProjectNode> Projects,
    IReadOnlyList<CodeNode> CodeNodes,
    IReadOnlyList<DependencyEdge> Edges,
    IReadOnlyList<string> Diagnostics);
