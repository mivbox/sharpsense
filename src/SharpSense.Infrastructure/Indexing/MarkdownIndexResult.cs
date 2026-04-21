using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.Indexing;

public sealed record MarkdownIndexResult(
    IReadOnlyList<CodeNode> CodeNodes,
    IReadOnlyList<DependencyEdge> Edges);
