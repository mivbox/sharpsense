using SharpSense.Application.Context360.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using System.Text;

namespace SharpSense.Cli.Shared;

public static class TokenObjectNotation
{
    public static string SerializeSemanticSearch(IEnumerable<HybridSearchHit> results)
    {
        ArgumentNullException.ThrowIfNull(results);

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
                        .Append(GetNodeTypeShorthand(result.NodeType))
                        .Append("] `")
                        .Append(result.Id)
                        .Append("` ")
                        .Append(SanitizeDisplayName(result.NodeType, result.DisplayName))
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

    internal static string FormatLineSpan(int startLine, int endLine)
        => startLine == endLine
            ? $"L{startLine}"
            : $"L{startLine}-{endLine}";

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

    internal static string SanitizeDisplayName(NodeType nodeType, string displayName)
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
        public Dictionary<string, List<HybridSearchHit>> Files { get; } = new(StringComparer.Ordinal);
    }
}
