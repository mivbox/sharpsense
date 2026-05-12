using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed partial class EdgeExtractor
{
    private static readonly string _serviceCollectionInterfaceName = nameof(IServiceCollection);
    private static readonly string _serviceCollectionNamespace = typeof(IServiceCollection).Namespace!;
    private static readonly string _serviceProviderInterfaceName = nameof(IServiceProvider);
    private static readonly string _serviceProviderNamespace = typeof(IServiceProvider).Namespace!;

    public IReadOnlyList<DependencyEdge> Extract(
        Solution solution,
        IReadOnlyList<Project> orderedProjects,
        IReadOnlyDictionary<ProjectId, string> projectIds,
        IReadOnlyList<DeclaredSymbolContext> declaredSymbols,
        IReadOnlyDictionary<string, string> symbolNodeIds)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(orderedProjects);
        ArgumentNullException.ThrowIfNull(projectIds);
        ArgumentNullException.ThrowIfNull(declaredSymbols);
        ArgumentNullException.ThrowIfNull(symbolNodeIds);

        using var edgeActivity = SharpSenseTraceSpan.Start("roslyn.build-dependency-edges");
        var edgeKeys = new HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)>();
        var nodeResolver = new SymbolNodeResolver(solution, projectIds, symbolNodeIds);

        AddProjectReferenceEdges(orderedProjects, projectIds, edgeKeys);
        AddTypeDependencyEdges(declaredSymbols, nodeResolver, edgeKeys);
        AddStructuralHierarchyEdges(declaredSymbols, nodeResolver, edgeKeys);
        AddMemberDependencyEdges(declaredSymbols, nodeResolver, edgeKeys);

        var edges = edgeKeys
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .Select(
                static edge => new DependencyEdge
                {
                    CallerId = edge.CallerId,
                    CalleeId = edge.CalleeId,
                    EdgeType = edge.EdgeType
                })
            .ToArray();
        edgeActivity.AddTag("index.dependency.count", edges.Length);

        return edges;
    }

    public IReadOnlyList<DependencyEdge> ExtractIncremental(
        Solution solution,
        IReadOnlyDictionary<ProjectId, string> projectIds,
        IReadOnlyList<DeclaredSymbolContext> declaredSymbols,
        IReadOnlyDictionary<string, string> symbolNodeIds)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(projectIds);
        ArgumentNullException.ThrowIfNull(declaredSymbols);
        ArgumentNullException.ThrowIfNull(symbolNodeIds);

        using var edgeActivity = SharpSenseTraceSpan.Start("roslyn.build-dependency-edges");
        var edgeKeys = new HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)>();
        var nodeResolver = new SymbolNodeResolver(solution, projectIds, symbolNodeIds);

        AddTypeDependencyEdges(declaredSymbols, nodeResolver, edgeKeys);
        AddStructuralHierarchyEdges(declaredSymbols, nodeResolver, edgeKeys);
        AddMemberDependencyEdges(declaredSymbols, nodeResolver, edgeKeys);

        var edges = edgeKeys
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .Select(
                static edge => new DependencyEdge
                {
                    CallerId = edge.CallerId,
                    CalleeId = edge.CalleeId,
                    EdgeType = edge.EdgeType
                })
            .ToArray();
        edgeActivity.AddTag("index.dependency.count", edges.Length);

        return edges;
    }

    private static void AddProjectReferenceEdges(
        IReadOnlyList<Project> orderedProjects,
        IReadOnlyDictionary<ProjectId, string> projectIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        foreach (var project in orderedProjects)
        {
            if (!projectIds.TryGetValue(project.Id, out var callerId))
            {
                continue;
            }

            foreach (var reference in project.ProjectReferences)
            {
                if (!projectIds.TryGetValue(reference.ProjectId, out var calleeId))
                {
                    continue;
                }

                edgeKeys.Add((callerId, calleeId, EdgeType.ProjectReference));
            }
        }
    }
}
