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

    private sealed class DirectoryGroup
    {
        public List<string> FileOrder { get; } = [];
        public Dictionary<string, List<HybridSearchHit>> Files { get; } = new(StringComparer.Ordinal);
    }
}
