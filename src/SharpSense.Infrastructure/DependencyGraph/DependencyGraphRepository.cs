using Microsoft.EntityFrameworkCore;
using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Shared;

namespace SharpSense.Infrastructure.DependencyGraph;

public sealed class DependencyGraphRepository(
    SharpSenseDbContext context)
    : IDependencyGraphRepository
{
    public async Task<GraphResult> GetGraph(
        IReadOnlyList<int> directoryIds,
        bool includeBoundaryNodes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directoryIds);

        var selectedDirectoryIds = directoryIds
            .Where(static directoryId => directoryId > 0)
            .Distinct()
            .OrderBy(static directoryId => directoryId)
            .ToArray();
        if (selectedDirectoryIds.Length == 0)
        {
            return new GraphResult([], []);
        }

        var normalizedDirectoryIds = GetNormalizedSelectedDirectoryIds(selectedDirectoryIds);
        var selectedDescendantDirectoryIds = context.DirectoryClosures
            .AsNoTracking()
            .Where(closure => normalizedDirectoryIds.Contains(closure.AncestorDirectoryId))
            .Select(static closure => closure.DescendantDirectoryId)
            .Distinct();
        var selectedDocumentIds = context.Documents
            .AsNoTracking()
            .Where(document => selectedDescendantDirectoryIds.Contains(document.DirectoryId))
            .Select(static document => document.Id);

        var selectedProjectNodeIds = context.ProjectNodes
            .AsNoTracking()
            .Where(projectNode => selectedDocumentIds.Contains(projectNode.ProjectDocumentId))
            .Select(static projectNode => projectNode.Id);
        var selectedCodeNodeIds = context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => selectedDocumentIds.Contains(codeNode.DocumentId))
            .Select(static codeNode => codeNode.Id);
        var selectedNodeIds = selectedProjectNodeIds
            .Concat(selectedCodeNodeIds)
            .Distinct();

        var selectedProjectNodes = await CodeNodeNavigationQueries.ProjectProjectNodes(
                context,
                context.ProjectNodes
                    .AsNoTracking()
                    .Where(projectNode => selectedProjectNodeIds.Contains(projectNode.Id))
                    .OrderBy(projectNode => projectNode.Name)
                    .ThenBy(projectNode => projectNode.Id))
            .ToArrayAsync(ct);
        var selectedCodeNodes = await CodeNodeNavigationQueries.ProjectCodeNodes(
                context,
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => selectedCodeNodeIds.Contains(codeNode.Id))
                    .OrderBy(codeNode => codeNode.FullyQualifiedName)
                    .ThenBy(codeNode => codeNode.Id))
            .ToArrayAsync(ct);
        var internalEdges = await CodeNodeNavigationQueries.ProjectDependencyEdges(
                context,
                context.DependencyEdges
                    .AsNoTracking()
                    .Where(edge =>
                        selectedNodeIds.Contains(edge.CallerNodeId) &&
                        selectedNodeIds.Contains(edge.CalleeNodeId))
                    .OrderBy(edge => edge.CallerNodeId)
                    .ThenBy(edge => edge.CalleeNodeId)
                    .ThenBy(edge => edge.EdgeType))
            .ToArrayAsync(ct);
        var boundaryEdges = includeBoundaryNodes
            ? await CodeNodeNavigationQueries.ProjectDependencyEdges(
                    context,
                    GetBoundaryEdges(selectedNodeIds)
                        .OrderBy(edge => edge.CallerNodeId)
                        .ThenBy(edge => edge.CalleeNodeId)
                        .ThenBy(edge => edge.EdgeType))
                .ToArrayAsync(ct)
            : [];
        var externalNodeIds = includeBoundaryNodes
            ? GetExternalNodeIds(selectedNodeIds)
            : null;
        var externalProjectNodes = externalNodeIds is null
            ? []
            : await CodeNodeNavigationQueries.ProjectProjectNodes(
                    context,
                    context.ProjectNodes
                        .AsNoTracking()
                        .Where(projectNode => externalNodeIds.Contains(projectNode.Id))
                        .OrderBy(projectNode => projectNode.Name)
                        .ThenBy(projectNode => projectNode.Id))
                .ToArrayAsync(ct);
        var externalCodeNodes = externalNodeIds is null
            ? []
            : await CodeNodeNavigationQueries.ProjectCodeNodes(
                    context,
                    context.CodeNodes
                        .AsNoTracking()
                        .Where(codeNode => externalNodeIds.Contains(codeNode.Id))
                        .OrderBy(codeNode => codeNode.FullyQualifiedName)
                        .ThenBy(codeNode => codeNode.Id))
                .ToArrayAsync(ct);
        GraphNode[] nodes =
        [
            .. selectedProjectNodes.Select(DependencyGraphMapper.ToSelectedGraphNode),
            .. selectedCodeNodes.Select(DependencyGraphMapper.ToSelectedGraphNode),
            .. externalProjectNodes.Select(DependencyGraphMapper.ToExternalGraphNode),
            .. externalCodeNodes.Select(DependencyGraphMapper.ToExternalGraphNode)
        ];
        var nodeIds = nodes
            .Select(static node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        GraphEdge[] edges =
        [
            .. internalEdges
                .Where(edge => nodeIds.Contains(edge.CallerId) && nodeIds.Contains(edge.CalleeId))
                .Select(DependencyGraphMapper.ToInternalGraphEdge),
            .. boundaryEdges
                .Where(edge => nodeIds.Contains(edge.CallerId) && nodeIds.Contains(edge.CalleeId))
                .Select(DependencyGraphMapper.ToBoundaryGraphEdge)
        ];

        return new GraphResult(nodes, edges);
    }

    private IQueryable<int> GetNormalizedSelectedDirectoryIds(IReadOnlyList<int> selectedDirectoryIds)
    {
        var coveredIds = context.DirectoryClosures
            .AsNoTracking()
            .Where(closure =>
                selectedDirectoryIds.Contains(closure.AncestorDirectoryId) &&
                selectedDirectoryIds.Contains(closure.DescendantDirectoryId) &&
                closure.Depth > 0)
            .Select(static closure => closure.DescendantDirectoryId)
            .Distinct()
            ;

        return context.Directories
            .AsNoTracking()
            .Where(directory =>
                selectedDirectoryIds.Contains(directory.Id) &&
                !coveredIds.Contains(directory.Id))
            .Select(static directory => directory.Id);
    }

    private IQueryable<DependencyEdgeRecord> GetBoundaryEdges(IQueryable<int> selectedNodeIds)
        => GetOutboundBoundaryEdges(selectedNodeIds)
            .Concat(GetInboundBoundaryEdges(selectedNodeIds));

    private IQueryable<DependencyEdgeRecord> GetOutboundBoundaryEdges(IQueryable<int> selectedNodeIds)
        => context.DependencyEdges
            .AsNoTracking()
            .Where(edge =>
                selectedNodeIds.Contains(edge.CallerNodeId) &&
                !selectedNodeIds.Contains(edge.CalleeNodeId));

    private IQueryable<DependencyEdgeRecord> GetInboundBoundaryEdges(IQueryable<int> selectedNodeIds)
        => context.DependencyEdges
            .AsNoTracking()
            .Where(edge =>
                !selectedNodeIds.Contains(edge.CallerNodeId) &&
                selectedNodeIds.Contains(edge.CalleeNodeId));

    private IQueryable<int> GetExternalNodeIds(IQueryable<int> selectedNodeIds)
        => GetOutboundBoundaryEdges(selectedNodeIds)
            .Select(static edge => edge.CalleeNodeId)
            .Concat(GetInboundBoundaryEdges(selectedNodeIds).Select(static edge => edge.CallerNodeId))
            .Distinct();
}
