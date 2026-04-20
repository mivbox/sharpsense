using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.Indexing;

public static class IndexingMapper
{
    public static IndexedProject ToIndexedProject(ProjectNode source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new IndexedProject(
            source.Id,
            source.Name,
            source.RelativeFilePath,
            source.ContentHash);
    }

    public static IndexedProject[] ToIndexedProjects(IEnumerable<ProjectNode> source)
        => [.. source.Select(ToIndexedProject)];

    public static IndexedCodeNode ToIndexedCodeNode(CodeNode source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new IndexedCodeNode(
            source.Id,
            source.ProjectId,
            source.FullyQualifiedName,
            source.NodeType,
            source.RelativeFilePath,
            source.StartLine,
            source.EndLine,
            source.Summary);
    }

    public static IndexedCodeNode[] ToIndexedCodeNodes(IEnumerable<CodeNode> source)
        => [.. source.Select(ToIndexedCodeNode)];

    public static IndexedDependency ToIndexedDependency(DependencyEdge source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new IndexedDependency(
            source.CallerId,
            source.CalleeId,
            source.EdgeType);
    }

    public static IndexedDependency[] ToIndexedDependencies(IEnumerable<DependencyEdge> source)
        => [.. source.Select(ToIndexedDependency)];
}
