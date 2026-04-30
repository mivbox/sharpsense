using Microsoft.EntityFrameworkCore;
using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Shared;
using System.Runtime.CompilerServices;

namespace SharpSense.Infrastructure.DependencyGraph;

public sealed class DependencyGraphRepository(
    SharpSenseDbContext context)
    : IDependencyGraphRepository
{
    public IAsyncEnumerable<GraphNode> GetGraphNodes(
        IReadOnlyList<int> directoryIds,
        CancellationToken ct)
        => GetGraphNodes(directoryIds, includeBoundaryNodes: true, ct);

    public IAsyncEnumerable<GraphEdge> GetGraphEdges(
        IReadOnlyList<int> directoryIds,
        CancellationToken ct)
        => GetGraphEdges(directoryIds, includeBoundaryNodes: true, ct);

    private async IAsyncEnumerable<GraphNode> GetGraphNodes(
        IReadOnlyList<int> directoryIds,
        bool includeBoundaryNodes,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directoryIds);

        var selectedDirectoryIds = GetSelectedDirectoryIds(directoryIds);
        if (selectedDirectoryIds.Length == 0)
        {
            yield break;
        }

        var scopedNodeIds = CreateScopedNodeIds(selectedDirectoryIds);

        await foreach (var projectNode in CodeNodeNavigationQueries.ProjectProjectNodes(
                context,
                context.ProjectNodes
                    .AsNoTracking()
                    .Where(projectNode => scopedNodeIds.SelectedProjectNodeIds.Contains(projectNode.Id))
                    .OrderBy(projectNode => projectNode.Name)
                    .ThenBy(projectNode => projectNode.Id))
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            yield return DependencyGraphMapper.ToSelectedGraphNode(projectNode);
        }

        await foreach (var codeNode in CodeNodeNavigationQueries.ProjectCodeNodes(
                context,
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => scopedNodeIds.SelectedCodeNodeIds.Contains(codeNode.Id))
                    .OrderBy(codeNode => codeNode.FullyQualifiedName)
                    .ThenBy(codeNode => codeNode.Id))
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            yield return DependencyGraphMapper.ToSelectedGraphNode(codeNode);
        }

        if (!includeBoundaryNodes)
        {
            yield break;
        }

        var externalNodeIds = GetExternalNodeIds(scopedNodeIds.SelectedNodeIds);

        await foreach (var projectNode in CodeNodeNavigationQueries.ProjectProjectNodes(
                context,
                context.ProjectNodes
                    .AsNoTracking()
                    .Where(projectNode => externalNodeIds.Contains(projectNode.Id))
                    .OrderBy(projectNode => projectNode.Name)
                    .ThenBy(projectNode => projectNode.Id))
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            yield return DependencyGraphMapper.ToExternalGraphNode(projectNode);
        }

        await foreach (var codeNode in CodeNodeNavigationQueries.ProjectCodeNodes(
                context,
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => externalNodeIds.Contains(codeNode.Id))
                    .OrderBy(codeNode => codeNode.FullyQualifiedName)
                    .ThenBy(codeNode => codeNode.Id))
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            yield return DependencyGraphMapper.ToExternalGraphNode(codeNode);
        }
    }

    private async IAsyncEnumerable<GraphEdge> GetGraphEdges(
        IReadOnlyList<int> directoryIds,
        bool includeBoundaryNodes,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directoryIds);

        var selectedDirectoryIds = GetSelectedDirectoryIds(directoryIds);
        if (selectedDirectoryIds.Length == 0)
        {
            yield break;
        }

        var scopedNodeIds = CreateScopedNodeIds(selectedDirectoryIds);

        await foreach (var edge in CodeNodeNavigationQueries.ProjectDependencyEdges(
                context,
                context.DependencyEdges
                    .AsNoTracking()
                    .Where(edge =>
                        scopedNodeIds.SelectedNodeIds.Contains(edge.CallerNodeId) &&
                        scopedNodeIds.SelectedNodeIds.Contains(edge.CalleeNodeId))
                    .OrderBy(edge => edge.CallerNodeId)
                    .ThenBy(edge => edge.CalleeNodeId)
                    .ThenBy(edge => edge.EdgeType))
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            yield return DependencyGraphMapper.ToInternalGraphEdge(edge);
        }

        if (!includeBoundaryNodes)
        {
            yield break;
        }

        var externalNodeIds = GetExternalNodeIds(scopedNodeIds.SelectedNodeIds);
        var validBoundaryNodeIds = scopedNodeIds.SelectedNodeIds
            .Concat(
                context.ProjectNodes
                    .AsNoTracking()
                    .Where(projectNode => externalNodeIds.Contains(projectNode.Id))
                    .Select(static projectNode => projectNode.Id))
            .Concat(
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => externalNodeIds.Contains(codeNode.Id))
                    .Select(static codeNode => codeNode.Id))
            .Distinct();

        await foreach (var edge in CodeNodeNavigationQueries.ProjectDependencyEdges(
                context,
                GetBoundaryEdges(scopedNodeIds.SelectedNodeIds)
                    .Where(edge =>
                        validBoundaryNodeIds.Contains(edge.CallerNodeId) &&
                        validBoundaryNodeIds.Contains(edge.CalleeNodeId))
                    .OrderBy(edge => edge.CallerNodeId)
                    .ThenBy(edge => edge.CalleeNodeId)
                    .ThenBy(edge => edge.EdgeType))
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            yield return DependencyGraphMapper.ToBoundaryGraphEdge(edge);
        }
    }

    private static int[] GetSelectedDirectoryIds(IReadOnlyList<int> directoryIds)
        => directoryIds
            .Where(static directoryId => directoryId > 0)
            .Distinct()
            .OrderBy(static directoryId => directoryId)
            .ToArray();

    private ScopedNodeIds CreateScopedNodeIds(IReadOnlyList<int> selectedDirectoryIds)
    {
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

        return new ScopedNodeIds(
            selectedProjectNodeIds,
            selectedCodeNodeIds,
            selectedProjectNodeIds.Concat(selectedCodeNodeIds).Distinct());
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

    private sealed record ScopedNodeIds(
        IQueryable<int> SelectedProjectNodeIds,
        IQueryable<int> SelectedCodeNodeIds,
        IQueryable<int> SelectedNodeIds);
}
