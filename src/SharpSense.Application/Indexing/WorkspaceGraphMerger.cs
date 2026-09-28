using FluentResults;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Errors;

namespace SharpSense.Application.Indexing;

internal static class WorkspaceGraphMerger
{
    public static Result<ExtractedNodes> Merge(IReadOnlyList<ExtractedNodes> contributions)
    {
        var projects = new Dictionary<string, IndexedProject>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, IndexedCodeNode>(StringComparer.Ordinal);
        var outgoingEdges = new Dictionary<string, HashSet<IndexedDependency>>(StringComparer.Ordinal);

        foreach (var contribution in contributions)
        {
            var contributionEdges = contribution.Edges.ToLookup(static edge => edge.CallerId, StringComparer.Ordinal);
            foreach (var project in contribution.Projects)
            {
                if (projects.TryGetValue(project.Id, out var existing) && existing != project)
                {
                    return Conflict("project", project.Id);
                }

                projects[project.Id] = project;
            }

            foreach (var node in contribution.CodeNodes)
            {
                var edges = contributionEdges[node.CanonicalId].ToHashSet();
                if (nodes.TryGetValue(node.CanonicalId, out var existing) &&
                    ((existing with
                    {
                        VectorEmbedding = null
                    }) != (node with
                    {
                        VectorEmbedding = null
                    }) ||
                     !outgoingEdges[node.CanonicalId].SetEquals(edges)))
                {
                    return Conflict("declaration", node.CanonicalId);
                }

                nodes[node.CanonicalId] = node;
                outgoingEdges[node.CanonicalId] = edges;
            }
        }

        return Result.Ok(new ExtractedNodes(
            [.. projects.Values.OrderBy(static project => project.Name, StringComparer.Ordinal)
                .ThenBy(static project => project.Id, StringComparer.Ordinal)],
            [.. nodes.Values.OrderBy(static node => node.FullyQualifiedName, StringComparer.Ordinal)
                .ThenBy(static node => node.CanonicalId, StringComparer.Ordinal)],
            [.. contributions.SelectMany(static contribution => contribution.Edges)
                .Distinct()
                .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
                .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
                .ThenBy(static edge => edge.EdgeType)],
            [.. contributions.SelectMany(static contribution => contribution.Diagnostics)
                .Distinct(StringComparer.Ordinal)]));
    }

    private static Result<ExtractedNodes> Conflict(string kind, string identity)
        => Result.Fail(new ServiceError(
            ServiceErrorCode.FailedPrecondition,
            $"Workspace sources produced conflicting definitions for {kind} '{identity}'. " +
            "Remove or disambiguate overlapping sources before indexing. Existing index was preserved."));
}
