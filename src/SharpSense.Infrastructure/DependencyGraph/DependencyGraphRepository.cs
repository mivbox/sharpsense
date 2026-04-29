using Microsoft.EntityFrameworkCore;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
using System.Linq.Expressions;

namespace SharpSense.Infrastructure.DependencyGraph;

public sealed class DependencyGraphRepository(
    SharpSenseDbContext context)
    : IDependencyGraphRepository
{
    public async Task<GraphResult> GetGraph(
        IReadOnlyList<string> paths,
        bool includeBoundaryNodes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var normalizedPaths = NormalizeSelectedPaths(paths);
        if (normalizedPaths.Length == 0)
        {
            return new GraphResult([], []);
        }

        var selectedProjectNodesQuery = GetSelectedProjectNodes(normalizedPaths)
            .AsNoTracking();
        var selectedCodeNodesQuery = GetSelectedCodeNodes(normalizedPaths)
            .AsNoTracking();
        var selectedNodeIdsQuery = GetSelectedNodeIds(selectedProjectNodesQuery, selectedCodeNodesQuery);
        var selectedProjectNodes = await selectedProjectNodesQuery
            .OrderBy(projectNode => projectNode.Name)
            .ThenBy(projectNode => projectNode.Id)
            .ToArrayAsync(ct);
        var selectedCodeNodes = await selectedCodeNodesQuery
            .OrderBy(codeNode => codeNode.FullyQualifiedName)
            .ThenBy(codeNode => codeNode.Id)
            .ToArrayAsync(ct);

        if (selectedProjectNodes.Length == 0 && selectedCodeNodes.Length == 0)
        {
            return new GraphResult([], []);
        }

        var internalEdges = await GetInternalEdges(selectedNodeIdsQuery)
            .AsNoTracking()
            .OrderBy(dependencyEdge => dependencyEdge.CallerId)
            .ThenBy(dependencyEdge => dependencyEdge.CalleeId)
            .ThenBy(dependencyEdge => dependencyEdge.EdgeType)
            .ToArrayAsync(ct);
        var externalNodeIdsQuery = includeBoundaryNodes
            ? GetExternalNodeIds(selectedNodeIdsQuery)
            : null;
        var boundaryEdges = includeBoundaryNodes
            ? await GetBoundaryEdges(selectedNodeIdsQuery)
                .AsNoTracking()
                .OrderBy(dependencyEdge => dependencyEdge.CallerId)
                .ThenBy(dependencyEdge => dependencyEdge.CalleeId)
                .ThenBy(dependencyEdge => dependencyEdge.EdgeType)
                .ToArrayAsync(ct)
            : [];
        var externalProjectNodes = externalNodeIdsQuery is null
            ? []
            : await context.ProjectNodes
                .AsNoTracking()
                .Where(projectNode => externalNodeIdsQuery.Contains(projectNode.Id))
                .OrderBy(projectNode => projectNode.Name)
                .ThenBy(projectNode => projectNode.Id)
                .ToArrayAsync(ct);
        var externalCodeNodes = externalNodeIdsQuery is null
            ? []
            : await context.CodeNodes
                .AsNoTracking()
                .Where(codeNode => externalNodeIdsQuery.Contains(codeNode.CanonicalId))
                .OrderBy(codeNode => codeNode.FullyQualifiedName)
                .ThenBy(codeNode => codeNode.Id)
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

        return new GraphResult(
            nodes,
            edges);
    }

    private IQueryable<ProjectNode> GetSelectedProjectNodes(IReadOnlyList<string> paths)
        => context.ProjectNodes.Where(BuildPathSelectionPredicate<ProjectNode>(candidate => candidate.RelativeFilePath, paths));

    private IQueryable<CodeNode> GetSelectedCodeNodes(IReadOnlyList<string> paths)
        => context.CodeNodes.Where(BuildPathSelectionPredicate<CodeNode>(candidate => candidate.RelativeFilePath, paths));

    private IQueryable<DependencyEdge> GetInternalEdges(IQueryable<string> selectedNodeIds)
        => context.DependencyEdges.Where(
            dependencyEdge =>
                selectedNodeIds.Contains(dependencyEdge.CallerId) &&
                selectedNodeIds.Contains(dependencyEdge.CalleeId));

    private IQueryable<DependencyEdge> GetBoundaryEdges(IQueryable<string> selectedNodeIds)
        => GetOutboundBoundaryEdges(selectedNodeIds)
            .Concat(GetInboundBoundaryEdges(selectedNodeIds));

    private IQueryable<DependencyEdge> GetOutboundBoundaryEdges(IQueryable<string> selectedNodeIds)
        => context.DependencyEdges.Where(
            dependencyEdge =>
                selectedNodeIds.Contains(dependencyEdge.CallerId) &&
                !selectedNodeIds.Contains(dependencyEdge.CalleeId));

    private IQueryable<DependencyEdge> GetInboundBoundaryEdges(IQueryable<string> selectedNodeIds)
        => context.DependencyEdges.Where(
            dependencyEdge =>
                !selectedNodeIds.Contains(dependencyEdge.CallerId) &&
                selectedNodeIds.Contains(dependencyEdge.CalleeId));

    private IQueryable<string> GetExternalNodeIds(IQueryable<string> selectedNodeIds)
        => GetOutboundBoundaryEdges(selectedNodeIds)
            .Select(static edge => edge.CalleeId)
            .Concat(GetInboundBoundaryEdges(selectedNodeIds).Select(static edge => edge.CallerId))
            .Distinct();

    private static IQueryable<string> GetSelectedNodeIds(
        IQueryable<ProjectNode> selectedProjectNodes,
        IQueryable<CodeNode> selectedCodeNodes)
        => selectedProjectNodes.Select(static projectNode => projectNode.Id)
            .Concat(selectedCodeNodes.Select(static codeNode => codeNode.CanonicalId));

    private static string[] NormalizeSelectedPaths(IReadOnlyList<string> paths)
    {
        var comparison = GetPathComparison();
        var normalized = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim().Replace('\\', '/').Trim('/'))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(GetPathComparer())
            .OrderBy(static path => path, GetPathComparer())
            .ToList();
        var filtered = new List<string>(normalized.Count);

        foreach (var path in normalized)
        {
            var hasSelectedAncestor = filtered.Any(
                selectedPath =>
                    string.Equals(selectedPath, path, comparison) ||
                    path.StartsWith($"{selectedPath}/", comparison));
            if (!hasSelectedAncestor)
            {
                filtered.Add(path);
            }
        }

        return [.. filtered];
    }

    private static Expression<Func<TNode, bool>> BuildPathSelectionPredicate<TNode>(
        Expression<Func<TNode, string>> pathSelector,
        IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(pathSelector);
        ArgumentNullException.ThrowIfNull(paths);

        var parameter = pathSelector.Parameters[0];
        Expression body = Expression.Constant(false);

        foreach (var path in paths)
        {
            var exactMatch = Expression.Equal(pathSelector.Body, Expression.Constant(path));
            var descendantMatch = Expression.Call(
                pathSelector.Body,
                typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!,
                Expression.Constant($"{path}/"));
            body = Expression.OrElse(body, Expression.OrElse(exactMatch, descendantMatch));
        }

        return Expression.Lambda<Func<TNode, bool>>(body, parameter);
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
