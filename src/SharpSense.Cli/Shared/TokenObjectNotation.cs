using SharpSense.Application.Context360.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace SharpSense.Cli.Shared;

public static class TokenObjectNotation
{
    internal static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static string SerializeSemanticSearch(IEnumerable<HybridSearchHit> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return SerializeGroupedNodes(results.Select(static result => new ToonNode(
            result.Id,
            result.NodeType,
            result.DisplayName,
            result.RelativeFilePath,
            result.StartLine,
            result.EndLine)));
    }

    public static string SerializeCalleeTrace(
        CodeNodeResult rootNode,
        IReadOnlyList<CodeNodeResult> callees)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        ArgumentNullException.ThrowIfNull(callees);

        var builder = new StringBuilder();
        AppendTraceLine(builder, 0, rootNode);

        foreach (var callee in callees)
        {
            builder.AppendLine();
            AppendTraceLine(builder, 1, callee);
        }

        return builder.ToString();
    }

    public static string SerializeCallerTrace(
        CodeNodeResult rootNode,
        IReadOnlyList<CodeNodeResult> callers,
        IReadOnlyList<ImpactedDependencyEdge> dependencies)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        ArgumentNullException.ThrowIfNull(callers);
        ArgumentNullException.ThrowIfNull(dependencies);

        if (callers.Count == 0 || dependencies.Count == 0)
        {
            return SerializeCalleeTrace(rootNode, []);
        }

        var nodeByCanonicalId = callers
            .Concat([rootNode])
            .GroupBy(static node => node.CanonicalId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var callersByCalleeId = dependencies
            .Where(edge =>
                nodeByCanonicalId.ContainsKey(edge.CallerId) &&
                nodeByCanonicalId.ContainsKey(edge.CalleeId))
            .GroupBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group => group
                    .Select(static edge => edge.CallerId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(callerId => nodeByCanonicalId[callerId].FullyQualifiedName, StringComparer.Ordinal)
                    .ThenBy(callerId => nodeByCanonicalId[callerId].Id)
                    .ToArray(),
                StringComparer.Ordinal);
        var chains = new List<CodeNodeResult[]>();
        var currentPath = new List<CodeNodeResult> { rootNode };
        var visitedCanonicalIds = new HashSet<string>(StringComparer.Ordinal) { rootNode.CanonicalId };

        BuildCallerChains(
            rootNode.CanonicalId,
            nodeByCanonicalId,
            callersByCalleeId,
            currentPath,
            visitedCanonicalIds,
            chains);

        if (chains.Count == 0)
        {
            return SerializeCalleeTrace(rootNode, []);
        }

        var builder = new StringBuilder();

        for (var chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            if (chainIndex > 0)
            {
                builder.AppendLine().AppendLine();
            }

            var chain = chains[chainIndex];

            for (var nodeIndex = 0; nodeIndex < chain.Length; nodeIndex++)
            {
                if (nodeIndex > 0)
                {
                    builder.AppendLine();
                }

                AppendTraceLine(builder, nodeIndex, chain[nodeIndex]);
            }
        }

        return builder.ToString();
    }

    public static string SerializeContext360(Context360Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.TargetNode);
        ArgumentNullException.ThrowIfNull(result.Callers);
        ArgumentNullException.ThrowIfNull(result.Implementers);
        ArgumentNullException.ThrowIfNull(result.Callees);
        ArgumentNullException.ThrowIfNull(result.Inherits);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.TargetNode.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.TargetNode.RelativeFilePath);

        var builder = new StringBuilder();
        builder.AppendLine("node:");
        builder.Append("  id: ").Append(result.TargetNode.Id).AppendLine();
        builder.Append("  name: ").Append(result.TargetNode.Name).AppendLine();
        builder.Append("  kind: ").Append(GetNodeTypeShorthand(result.TargetNode.Kind)).AppendLine();
        builder.Append("  file: ")
            .Append(result.TargetNode.RelativeFilePath)
            .Append(':')
            .Append(result.TargetNode.StartLine)
            .Append('-')
            .Append(result.TargetNode.EndLine)
            .AppendLine();
        builder.AppendLine();
        builder.AppendLine("incoming:");
        builder.Append("  callers: ").Append(FormatContext360RelatedNodes(result.Callers)).AppendLine();
        builder.Append("  implementers: ").Append(FormatContext360RelatedNodes(result.Implementers)).AppendLine();
        builder.AppendLine();
        builder.AppendLine("outgoing:");
        builder.Append("  callees: ").Append(FormatContext360RelatedNodes(result.Callees)).AppendLine();
        builder.Append("  inherits: ").Append(FormatContext360RelatedNodes(result.Inherits));
        return builder.ToString();
    }

    public static string SerializeRefactorResult(RefactorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.ModifiedFilePaths);

        var builder = new StringBuilder();
        builder.Append("refactor_success: ")
            .Append(result.Success ? "true" : "false");

        if (!result.Success)
        {
            builder.AppendLine();
            builder.Append("error_message: ").Append(result.ErrorMessage);
            return builder.ToString();
        }

        builder.AppendLine();

        if (result.ModifiedFilePaths.Length == 0)
        {
            builder.Append("modified_files: []");
            return builder.ToString();
        }

        builder.AppendLine("modified_files:");

        foreach (var modifiedFilePath in result.ModifiedFilePaths)
        {
            builder.Append("  - ")
                .Append(modifiedFilePath)
                .AppendLine();
        }

        builder.Length -= Environment.NewLine.Length;
        return builder.ToString();
    }

    private static string SerializeGroupedNodes(IEnumerable<ToonNode> results)
    {
        var directoryGroups = new Dictionary<string, DirectoryGroup>(StringComparer.Ordinal);
        var directoryOrder = new List<string>();

        foreach (var result in results)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(result.RelativeFilePath);

            var directoryPath = GetDirectoryPath(result.RelativeFilePath);
            var fileName = Path.GetFileName(result.RelativeFilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            if (!directoryGroups.TryGetValue(directoryPath, out var directoryGroup))
            {
                directoryGroup = new DirectoryGroup();
                directoryGroups[directoryPath] = directoryGroup;
                directoryOrder.Add(directoryPath);
            }

            if (!directoryGroup.Files.TryGetValue(fileName, out var fileResults))
            {
                fileResults = [];
                directoryGroup.Files[fileName] = fileResults;
                directoryGroup.FileOrder.Add(fileName);
            }

            fileResults.Add(result);
        }

        var builder = new StringBuilder();

        for (var directoryIndex = 0; directoryIndex < directoryOrder.Count; directoryIndex++)
        {
            if (directoryIndex > 0)
            {
                builder.AppendLine();
            }

            var directoryPath = directoryOrder[directoryIndex];
            var directoryGroup = directoryGroups[directoryPath];
            builder.Append(directoryPath).AppendLine(":");

            foreach (var fileName in directoryGroup.FileOrder)
            {
                builder.Append("  ").Append(fileName).AppendLine(":");

                foreach (var result in directoryGroup.Files[fileName])
                {
                    builder
                        .Append("    - [")
                        .Append(GetNodeTypeShorthand(result.Kind))
                        .Append("] `")
                        .Append(result.Id)
                        .Append("` ")
                        .Append(SanitizeDisplayName(result.Kind, result.DisplayName))
                        .Append(' ')
                        .Append(FormatLineSpan(result.StartLine, result.EndLine))
                        .AppendLine();
                }
            }
        }

        if (builder.Length > 0)
        {
            builder.Length -= Environment.NewLine.Length;
        }

        return builder.ToString();
    }

    private static string FormatLineSpan(int startLine, int endLine)
        => startLine == endLine
            ? $"L{startLine}"
            : $"L{startLine}-{endLine}";

    private static void AppendTraceLine(
        StringBuilder builder,
        int depth,
        CodeNodeResult node)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(node.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(node.RelativeFilePath);

        if (depth == 0)
        {
            builder.Append("- ");
        }
        else
        {
            builder.Append(' ', depth * 2).Append("-> ");
        }

        builder
            .Append('[')
            .Append(GetNodeTypeShorthand(node.NodeType))
            .Append("] `")
            .Append(node.Id)
            .Append("` ")
            .Append(SanitizeDisplayName(node.NodeType, node.DisplayName))
            .Append(" @ ")
            .Append(node.RelativeFilePath)
            .Append(':')
            .Append(FormatLineSpan(node.StartLine, node.EndLine));
    }

    private static void BuildCallerChains(
        string currentCalleeId,
        IReadOnlyDictionary<string, CodeNodeResult> nodeByCanonicalId,
        IReadOnlyDictionary<string, string[]> callersByCalleeId,
        List<CodeNodeResult> currentPath,
        HashSet<string> visitedCanonicalIds,
        List<CodeNodeResult[]> chains)
    {
        if (!callersByCalleeId.TryGetValue(currentCalleeId, out var callerIds) || callerIds.Length == 0)
        {
            chains.Add([.. currentPath.AsEnumerable().Reverse()]);
            return;
        }

        foreach (var callerId in callerIds)
        {
            if (!visitedCanonicalIds.Add(callerId))
            {
                continue;
            }

            currentPath.Add(nodeByCanonicalId[callerId]);
            BuildCallerChains(
                callerId,
                nodeByCanonicalId,
                callersByCalleeId,
                currentPath,
                visitedCanonicalIds,
                chains);
            currentPath.RemoveAt(currentPath.Count - 1);
            visitedCanonicalIds.Remove(callerId);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    internal static string GetNodeTypeShorthand(NodeType nodeType) =>
        nodeType switch
        {
            NodeType.Method => "M",
            NodeType.Class => "C",
            NodeType.Interface => "I",
            NodeType.Property => "P",
            NodeType.Field => "F",
            NodeType.Document => "D",
            _ => throw new InvalidOperationException($"Unsupported node type '{nodeType}'.")
        };

    private static string SanitizeDisplayName(NodeType nodeType, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (nodeType is not NodeType.Method)
        {
            return displayName;
        }

        var parameterListStart = displayName.IndexOf('(');
        return parameterListStart < 0
            ? displayName
            : displayName[..parameterListStart].TrimEnd();
    }

    private static string GetDirectoryPath(string relativeFilePath)
    {
        var directoryPath = Path.GetDirectoryName(relativeFilePath);
        return string.IsNullOrWhiteSpace(directoryPath)
            ? "./"
            : NormalizePathSeparators(directoryPath) + "/";
    }

    private static string NormalizePathSeparators(string path)
        => path
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static string FormatContext360RelatedNodes(IReadOnlyList<Context360RelatedNode> nodes)
    {
        if (nodes.Count == 0)
        {
            return "[]";
        }

        var builder = new StringBuilder("[");

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(node.Name);

            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(node.Name)
                .Append(" (Id:")
                .Append(node.Id)
                .Append(')');
        }

        builder.Append(']');
        return builder.ToString();
    }

    private sealed class DirectoryGroup
    {
        public List<string> FileOrder { get; } = [];
        public Dictionary<string, List<ToonNode>> Files { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct ToonNode(
        int Id,
        NodeType Kind,
        string DisplayName,
        string RelativeFilePath,
        int StartLine,
        int EndLine);
}
