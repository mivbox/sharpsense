using SharpSense.Application.Indexing.Models;
using static SharpSense.Infrastructure.Indexing.GraphPaths;
using static SharpSense.Infrastructure.Indexing.PersistedGraphBuilder;

namespace SharpSense.Infrastructure.Indexing;

internal static class GraphSnapshot
{
    internal static ExtractedNodes CanonicalizeSnapshot(
        ExtractedNodes extractedNodes)
    {
        ArgumentNullException.ThrowIfNull(extractedNodes);

        var normalizedProjects = extractedNodes.Projects
            .Select(
                static project => project with
                {
                    RelativeFilePath = NormalizeRelativePath(project.RelativeFilePath)
                })
            .GroupBy(static project => project.Id, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ThenBy(static project => project.Id, StringComparer.Ordinal)
            .ToArray();
        var normalizedCodeNodes = extractedNodes.CodeNodes
            .Select(
                static codeNode => codeNode with
                {
                    RelativeFilePath = NormalizeRelativePath(codeNode.RelativeFilePath)
                })
            .GroupBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .ToArray();
        var callerNodeIds = normalizedProjects
            .Select(static project => project.Id)
            .Concat(normalizedCodeNodes.Select(static codeNode => codeNode.CanonicalId))
            .ToHashSet(StringComparer.Ordinal);
        var syntheticNodeIds = GetSyntheticNodeIds(extractedNodes.Edges);
        var persistedNodeIds = callerNodeIds.Concat(syntheticNodeIds)
            .ToHashSet(StringComparer.Ordinal);
        var normalizedEdges = extractedNodes.Edges
            .Where(edge => callerNodeIds.Contains(edge.CallerId) && persistedNodeIds.Contains(edge.CalleeId))
            .GroupBy(static edge => (edge.CallerId, edge.CalleeId, edge.EdgeType))
            .Select(static group => group.Last())
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .ToArray();

        return new ExtractedNodes(
            normalizedProjects,
            normalizedCodeNodes,
            normalizedEdges,
            extractedNodes.Diagnostics);
    }

    internal static string? DescribeSnapshotDifference(
        ExtractedNodes currentSnapshot,
        ExtractedNodes updatedSnapshot)
    {
        return DescribeProjectDifference(currentSnapshot.Projects, updatedSnapshot.Projects) ??
            DescribeCodeNodeDifference(currentSnapshot.CodeNodes, updatedSnapshot.CodeNodes) ??
                DescribeEdgeDifference(currentSnapshot.Edges, updatedSnapshot.Edges);
    }

    private static string? DescribeProjectDifference(
        IReadOnlyList<IndexedProject> currentProjects,
        IReadOnlyList<IndexedProject> updatedProjects)
    {
        if (currentProjects.Count != updatedProjects.Count)
        {
            return $"project count differs: persisted={currentProjects.Count}, updated={updatedProjects.Count}";
        }

        for (var index = 0; index < currentProjects.Count; index++)
        {
            var currentProject = currentProjects[index];
            var updatedProject = updatedProjects[index];
            if (!string.Equals(currentProject.Id, updatedProject.Id, StringComparison.Ordinal))
            {
                return $"project[{index}] id differs: persisted='{currentProject.Id}', updated='{updatedProject.Id}'";
            }

            if (!string.Equals(currentProject.Name, updatedProject.Name, StringComparison.Ordinal))
            {
                return $"project[{index}] name differs: persisted='{currentProject.Name}', updated='{updatedProject.Name}'";
            }

            if (!string.Equals(
                currentProject.RelativeFilePath,
                updatedProject.RelativeFilePath,
                StringComparison.Ordinal))
            {
                return $"project[{index}] relative path differs: persisted='{currentProject.RelativeFilePath}', updated='{updatedProject.RelativeFilePath}'";
            }

            if (!string.Equals(currentProject.ContentHash, updatedProject.ContentHash, StringComparison.Ordinal))
            {
                return $"project[{index}] content hash differs: persisted='{currentProject.ContentHash}', updated='{updatedProject.ContentHash}'";
            }
        }

        return null;
    }

    private static string? DescribeCodeNodeDifference(
        IReadOnlyList<IndexedCodeNode> currentCodeNodes,
        IReadOnlyList<IndexedCodeNode> updatedCodeNodes)
    {
        if (currentCodeNodes.Count != updatedCodeNodes.Count)
        {
            return $"code node count differs: persisted={currentCodeNodes.Count}, updated={updatedCodeNodes.Count}";
        }

        for (var index = 0; index < currentCodeNodes.Count; index++)
        {
            var currentCodeNode = currentCodeNodes[index];
            var updatedCodeNode = updatedCodeNodes[index];
            if (!string.Equals(currentCodeNode.CanonicalId, updatedCodeNode.CanonicalId, StringComparison.Ordinal))
            {
                return $"code node[{index}] canonical id differs: persisted='{currentCodeNode.CanonicalId}', updated='{updatedCodeNode.CanonicalId}'";
            }

            if (!string.Equals(currentCodeNode.ProjectId, updatedCodeNode.ProjectId, StringComparison.Ordinal))
            {
                return $"code node[{index}] project id differs: persisted='{currentCodeNode.ProjectId}', updated='{updatedCodeNode.ProjectId}'";
            }

            if (!string.Equals(
                currentCodeNode.FullyQualifiedName,
                updatedCodeNode.FullyQualifiedName,
                StringComparison.Ordinal))
            {
                return $"code node[{index}] fully qualified name differs: persisted='{currentCodeNode.FullyQualifiedName}', updated='{updatedCodeNode.FullyQualifiedName}'";
            }

            if (!string.Equals(currentCodeNode.DisplayName, updatedCodeNode.DisplayName, StringComparison.Ordinal))
            {
                return $"code node[{index}] display name differs: persisted='{currentCodeNode.DisplayName}', updated='{updatedCodeNode.DisplayName}'";
            }

            if (currentCodeNode.NodeType != updatedCodeNode.NodeType)
            {
                return $"code node[{index}] node type differs: persisted='{currentCodeNode.NodeType}', updated='{updatedCodeNode.NodeType}'";
            }

            if (!string.Equals(
                currentCodeNode.RelativeFilePath,
                updatedCodeNode.RelativeFilePath,
                StringComparison.Ordinal))
            {
                return $"code node[{index}] relative path differs: persisted='{currentCodeNode.RelativeFilePath}', updated='{updatedCodeNode.RelativeFilePath}'";
            }

            if (currentCodeNode.StartLine != updatedCodeNode.StartLine)
            {
                return $"code node[{index}] start line differs: persisted={currentCodeNode.StartLine}, updated={updatedCodeNode.StartLine}";
            }

            if (currentCodeNode.EndLine != updatedCodeNode.EndLine)
            {
                return $"code node[{index}] end line differs: persisted={currentCodeNode.EndLine}, updated={updatedCodeNode.EndLine}";
            }

            if (!string.Equals(currentCodeNode.Summary, updatedCodeNode.Summary, StringComparison.Ordinal))
            {
                return $"code node[{index}] summary differs: persisted='{currentCodeNode.Summary}', updated='{updatedCodeNode.Summary}'";
            }

            if (!string.Equals(currentCodeNode.SearchText, updatedCodeNode.SearchText, StringComparison.Ordinal))
            {
                return $"code node[{index}] search text differs: persisted='{currentCodeNode.SearchText}', updated='{updatedCodeNode.SearchText}'";
            }

            if (!string.Equals(currentCodeNode.BodyHash, updatedCodeNode.BodyHash, StringComparison.Ordinal))
            {
                return $"code node[{index}] body hash differs: persisted='{currentCodeNode.BodyHash}', updated='{updatedCodeNode.BodyHash}'";
            }

            if (!VectorsEqual(currentCodeNode.VectorEmbedding, updatedCodeNode.VectorEmbedding))
            {
                return $"code node[{index}] vector differs";
            }
        }

        return null;
    }

    private static string? DescribeEdgeDifference(
        IReadOnlyList<IndexedDependency> currentEdges,
        IReadOnlyList<IndexedDependency> updatedEdges)
    {
        if (currentEdges.Count != updatedEdges.Count)
        {
            return $"dependency edge count differs: persisted={currentEdges.Count}, updated={updatedEdges.Count}";
        }

        for (var index = 0; index < currentEdges.Count; index++)
        {
            var currentEdge = currentEdges[index];
            var updatedEdge = updatedEdges[index];
            if (!string.Equals(currentEdge.CallerId, updatedEdge.CallerId, StringComparison.Ordinal))
            {
                return $"edge[{index}] caller differs: persisted='{currentEdge.CallerId}', updated='{updatedEdge.CallerId}'";
            }

            if (!string.Equals(currentEdge.CalleeId, updatedEdge.CalleeId, StringComparison.Ordinal))
            {
                return $"edge[{index}] callee differs: persisted='{currentEdge.CalleeId}', updated='{updatedEdge.CalleeId}'";
            }

            if (currentEdge.EdgeType != updatedEdge.EdgeType)
            {
                return $"edge[{index}] type differs: persisted='{currentEdge.EdgeType}', updated='{updatedEdge.EdgeType}'";
            }

            if (!string.Equals(currentEdge.Metadata, updatedEdge.Metadata, StringComparison.Ordinal))
            {
                return $"edge[{index}] metadata differs: persisted='{currentEdge.Metadata}', updated='{updatedEdge.Metadata}'";
            }
        }

        return null;
    }

    private static bool VectorsEqual(
        float[]? currentVector,
        float[]? updatedVector)
    {
        return currentVector is null && updatedVector is null ||
            currentVector is not null && updatedVector is not null && currentVector.SequenceEqual(updatedVector);
    }
}
