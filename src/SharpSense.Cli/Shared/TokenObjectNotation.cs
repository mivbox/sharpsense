using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using System.Text;

namespace SharpSense.Cli.Shared;

internal static class TokenObjectNotation
{
    public static string SerializeSemanticSearch(IEnumerable<HybridSearchHit> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return SerializeGroupedNodes(results
            .Select(static result => new ToonNode(
                result.Id,
                result.NodeType,
                result.DisplayName,
                result.RelativeFilePath,
                result.StartLine,
                result.EndLine)));
    }

    public static string SerializeCalleeTrace(
        CodeNodeResult rootNode,
        IReadOnlyList<CodeNodeResult> callees,
        IReadOnlyDictionary<int, MemoryNode[]>? memoriesByNodeId = null,
        IReadOnlyList<ImpactedDependencyEdge>? dependencies = null,
        bool truncated = false)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        ArgumentNullException.ThrowIfNull(callees);

        var builder = new StringBuilder();
        AppendTraceLine(builder, 0, rootNode, memoriesByNodeId);

        if (callees.Count > 0 && dependencies is { Count: > 0 })
        {
            AppendCalleeTree(builder, rootNode, callees, dependencies, memoriesByNodeId);
        }
        else
        {
            foreach (var callee in callees)
            {
                builder.AppendLine();
                AppendTraceLine(builder, 1, callee, memoriesByNodeId);
            }
        }

        if (truncated)
        {
            builder.AppendLine()
                .AppendLine()
                .Append("truncated: true (reached 1000-node limit; reduce maxDepth to narrow results)");
        }

        AppendMemorySummary(builder, rootNode, callees, memoriesByNodeId);

        return builder.ToString();
    }

    public static string SerializeCallerTrace(
        CodeNodeResult rootNode,
        IReadOnlyList<CodeNodeResult> callers,
        IReadOnlyList<ImpactedDependencyEdge> dependencies,
        IReadOnlyDictionary<int, MemoryNode[]>? memoriesByNodeId = null)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        ArgumentNullException.ThrowIfNull(callers);
        ArgumentNullException.ThrowIfNull(dependencies);

        if (callers.Count == 0 || dependencies.Count == 0)
        {
            return SerializeCalleeTrace(rootNode, [], memoriesByNodeId);
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
        var currentPath = new List<CodeNodeResult>
        {
            rootNode
        };
        var visitedCanonicalIds = new HashSet<string>(StringComparer.Ordinal)
        {
            rootNode.CanonicalId
        };

        BuildCallerChains(
            rootNode.CanonicalId,
            nodeByCanonicalId,
            callersByCalleeId,
            currentPath,
            visitedCanonicalIds,
            chains);

        if (chains.Count == 0)
        {
            return SerializeCalleeTrace(rootNode, [], memoriesByNodeId);
        }

        var builder = new StringBuilder();

        for (var chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            if (chainIndex > 0)
            {
                builder.AppendLine()
                    .AppendLine();
            }

            var chain = chains[chainIndex];

            for (var nodeIndex = 0; nodeIndex < chain.Length; nodeIndex++)
            {
                if (nodeIndex > 0)
                {
                    builder.AppendLine();
                }

                AppendTraceLine(builder, nodeIndex, chain[nodeIndex], memoriesByNodeId);
            }
        }

        var allCallerNodes = chains
            .SelectMany(static chain => chain)
            .ToArray();
        AppendMemorySummary(builder, rootNode, allCallerNodes, memoriesByNodeId);

        return builder.ToString();
    }

    public static string SerializeContext360(
        Context360Result result,
        IReadOnlyList<MemoryNode>? semanticContext = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.TargetNode);
        ArgumentNullException.ThrowIfNull(result.Callers);
        ArgumentNullException.ThrowIfNull(result.Implementers);
        ArgumentNullException.ThrowIfNull(result.Callees);
        ArgumentNullException.ThrowIfNull(result.Inherits);
        ArgumentNullException.ThrowIfNull(result.Parents);
        ArgumentNullException.ThrowIfNull(result.Children);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.TargetNode.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.TargetNode.RelativeFilePath);

        var includedSections = result.IncludedSections is { Length: > 0 }
            ? result.IncludedSections.ToHashSet()
            : Enum.GetValues<ContextSection>().ToHashSet();
        var omittedSections = Enum.GetValues<ContextSection>()
            .Where(section => !includedSections.Contains(section))
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("node:");
        builder.Append("  id: ")
            .Append(result.TargetNode.Id)
            .AppendLine();
        builder.Append("  name: ")
            .Append(result.TargetNode.Name)
            .AppendLine();
        builder.Append("  kind: ")
            .Append(GetNodeTypeShorthand(result.TargetNode.Kind))
            .AppendLine();
        builder.Append("  file: ")
            .Append(result.TargetNode.RelativeFilePath)
            .Append(':')
            .Append(result.TargetNode.StartLine)
            .Append('-')
            .Append(result.TargetNode.EndLine);

        if (includedSections.Contains(ContextSection.Callers) || includedSections.Contains(ContextSection.Implementers))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("incoming:");
            if (includedSections.Contains(ContextSection.Callers))
            {
                AppendContextBucket(builder, "callers", result.Callers, result.Totals?.Callers);
            }

            if (includedSections.Contains(ContextSection.Implementers))
            {
                AppendContextBucket(builder, "implementers", result.Implementers, result.Totals?.Implementers);
            }
        }

        if (includedSections.Contains(ContextSection.Callees) || includedSections.Contains(ContextSection.Inherits))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("outgoing:");
            if (includedSections.Contains(ContextSection.Callees))
            {
                AppendContextBucket(builder, "callees", result.Callees, result.Totals?.Callees);
            }

            if (includedSections.Contains(ContextSection.Inherits))
            {
                AppendContextBucket(builder, "inherits", result.Inherits, result.Totals?.Inherits);
            }
        }

        if (includedSections.Contains(ContextSection.Parents) || includedSections.Contains(ContextSection.Children))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("structural:");
            if (includedSections.Contains(ContextSection.Parents))
            {
                AppendContextBucket(builder, "parents", result.Parents, result.Totals?.Parents);
            }

            if (includedSections.Contains(ContextSection.Children))
            {
                AppendContextBucket(builder, "children", result.Children, result.Totals?.Children);
            }
        }

        if (omittedSections.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("omitted_sections: [")
                .Append(string.Join(", ", omittedSections))
                .Append(']');
        }

        if (semanticContext is { Count: > 0 })
        {
            var staleCount = semanticContext.Count(static memory => memory.IsStale);
            if (omittedSections.Length == 0)
            {
                builder.AppendLine()
                    .Append("  memories: ");
            }
            else
            {
                builder.AppendLine()
                    .AppendLine()
                    .Append("memories: ");
            }

            builder.Append(semanticContext.Count)
                .Append(" (")
                .Append(staleCount)
                .Append(" stale; use get_memory <id> to fetch content)");
        }

        if (semanticContext is { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("semantic_context:");
            builder.AppendLine("  memories:");

            foreach (var memory in semanticContext)
            {
                builder.Append("    - [")
                    .Append(GetNodeTypeShorthand(NodeType.Memory))
                    .Append("] id=")
                    .Append(memory.Id)
                    .Append(" intent=")
                    .Append(memory.Intent)
                    .Append(" stale=")
                    .Append(memory.IsStale ? "true" : "false")
                    .Append(" tags=")
                    .Append(FormatTags(memory.Tags));
                if (memory.IsStale)
                {
                    builder.Append(" hint=\"verify memory against current source before replacing or deleting\"");
                }

                builder.AppendLine();
            }

            builder.Length -= Environment.NewLine.Length;
        }

        return builder.ToString();
    }

    public static string SerializeMemory(MemoryNode memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        return SerializeMemoryBlock(memory, prefix: string.Empty);
    }

    public static string SerializeMemories(IReadOnlyDictionary<Guid, MemoryNode> memories)
    {
        ArgumentNullException.ThrowIfNull(memories);

        if (memories.Count == 0)
        {
            return "memories: 0";
        }

        var builder = new StringBuilder()
            .Append("memories: ")
            .Append(memories.Count)
            .AppendLine();
        foreach (var memory in memories.Values.OrderBy(static m => m.CreatedAt, Comparer<DateTimeOffset>.Default))
        {
            builder.Append(SerializeMemoryBlock(memory, prefix: "  "));
            builder.AppendLine();
        }
        builder.Length -= Environment.NewLine.Length;

        return builder.ToString();
    }

    public static string SerializeMemoryList(ListMemoriesResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.Items);

        var builder = new StringBuilder()
            .Append("memories: ")
            .Append(result.Items.Length)
            .Append(" of ")
            .Append(result.TotalCount)
            .Append(" (stale: ")
            .Append(result.StaleCount)
            .Append(", has_more: ")
            .Append(result.HasMore ? "true" : "false")
            .Append(')');

        foreach (var memory in result.Items)
        {
            builder.AppendLine()
                .Append("- [")
                .Append(GetNodeTypeShorthand(NodeType.Memory))
                .Append("] id=")
                .Append(memory.Id)
                .Append(" node=")
                .Append(memory.TargetCodeNodeId)
                .Append(" intent=")
                .Append(memory.Intent)
                .Append(" stale=")
                .Append(memory.IsStale ? "true" : "false")
                .Append(" tags=")
                .Append(FormatTags(memory.Tags))
                .Append(" target=")
                .Append(memory.TargetFullyQualifiedName)
                .Append(" @ ")
                .Append(memory.TargetRelativeFilePath);
            if (!string.IsNullOrWhiteSpace(memory.ContentPreview))
            {
                builder.AppendLine()
                    .Append("  preview: ")
                    .Append(memory.ContentPreview);
            }
        }

        return builder.ToString();
    }

    private static string SerializeMemoryBlock(MemoryNode memory, string prefix)
    {
        var builder = new StringBuilder()
            .Append(prefix)
            .Append("- memory:")
            .AppendLine()
            .Append(prefix)
            .Append("  id: ")
            .Append(memory.Id)
            .AppendLine()
            .Append(prefix)
            .Append("  target: ")
            .Append(memory.TargetFullyQualifiedName)
            .AppendLine()
            .Append(prefix)
            .Append("  intent: ")
            .Append(memory.Intent)
            .AppendLine()
            .Append(prefix)
            .Append("  stale: ")
            .Append(memory.IsStale ? "true" : "false")
            .AppendLine()
            .Append(prefix)
            .Append("  tags: ")
            .Append(FormatTags(memory.Tags))
            .AppendLine()
            .Append(prefix)
            .Append("  created_at: ")
            .Append(memory.CreatedAt.ToString("O"))
            .AppendLine()
            .Append(prefix)
            .Append("  content: |")
            .AppendLine();
        foreach (var line in memory.Content.Split('\n'))
        {
            builder.Append(prefix)
                .Append("    ")
                .Append(line.TrimEnd('\r'))
                .AppendLine();
        }
        if (memory.IsStale)
        {
            builder.AppendLine()
                .Append(prefix)
                .Append("  hint: verify memory against current source before replacing or deleting");
        }

        return builder.ToString();
    }

    public static string SerializeCommandExecutionResult(CommandExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.Blocks);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Command);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.WorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Status);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Summary);

        var builder = new StringBuilder();
        builder.Append("command: ")
            .AppendLine(result.Command);
        builder.Append("status: ")
            .AppendLine(result.Status);
        builder.Append("exit_code: ")
            .AppendLine(result.ExitCode.ToString());
        builder.Append("working_directory: ")
            .AppendLine(result.WorkingDirectory);

        if (!string.IsNullOrWhiteSpace(result.Query))
        {
            builder.Append("query: ")
                .AppendLine(result.Query);
        }

        builder.AppendLine("metrics:");
        builder.Append("  captured_lines: ")
            .AppendLine(result.TotalLines.ToString());
        builder.Append("  matched_lines: ")
            .AppendLine(result.MatchedLineCount.ToString());
        builder.Append("  block_count: ")
            .AppendLine(result.BlockCount.ToString());
        builder.Append("  truncated: ")
            .AppendLine(result.Truncated.ToString()
                .ToLowerInvariant());
        builder.Append("summary: ")
            .AppendLine(result.Summary);

        if (result.Blocks.Length == 0)
        {
            builder.Length -= Environment.NewLine.Length;

            return builder.ToString();
        }

        builder.AppendLine("output:");

        foreach (var block in result.Blocks)
        {
            builder.Append("  - span: ")
                .Append(block.StartLine)
                .Append('-')
                .AppendLine(block.EndLine.ToString());
            builder.AppendLine("    text: |");

            using var reader = new StringReader(block.Text);
            while (reader.ReadLine() is { } line)
            {
                builder.Append("      ")
                    .AppendLine(line);
            }
        }

        builder.Length -= Environment.NewLine.Length;

        return builder.ToString();
    }

    public static string SerializeCommandExecutionFailure(
        string command,
        string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return "command: " + command + Environment.NewLine +
            "status: error" + Environment.NewLine +
            "error_message: " + errorMessage;
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
            builder.Append(directoryPath)
                .AppendLine(":");

            foreach (var fileName in directoryGroup.FileOrder)
            {
                builder.Append("  ")
                    .Append(fileName)
                    .AppendLine(":");

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
        CodeNodeResult node,
        IReadOnlyDictionary<int, MemoryNode[]>? memoriesByNodeId = null)
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
            builder.Append(' ', depth * 2)
                .Append("-> ");
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

        AppendTraceMemoryMetadata(builder, depth, node.Id, memoriesByNodeId);
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
            chains.Add(
                [
                    .. currentPath.AsEnumerable()
                        .Reverse()
                ]);

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

    internal static string GetNodeTypeShorthand(NodeType nodeType) =>
        nodeType switch
        {
            NodeType.Method => "M",
            NodeType.Class => "C",
            NodeType.Component => "UI",
            NodeType.Interface => "I",
            NodeType.Property => "P",
            NodeType.Field => "F",
            NodeType.Document => "D",
            NodeType.Memory => "*",
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

    private static void AppendMemorySummary(
        StringBuilder builder,
        CodeNodeResult rootNode,
        IReadOnlyList<CodeNodeResult> nodes,
        IReadOnlyDictionary<int, MemoryNode[]>? memoriesByNodeId)
    {
        if (memoriesByNodeId is null)
        {
            return;
        }

        var allMemoryIds = new HashSet<Guid>();
        var staleMemoryIds = new HashSet<Guid>();
        CollectMemoryIds(memoriesByNodeId, rootNode.Id, allMemoryIds, staleMemoryIds);
        foreach (var node in nodes)
        {
            CollectMemoryIds(memoriesByNodeId, node.Id, allMemoryIds, staleMemoryIds);
        }

        if (allMemoryIds.Count == 0)
        {
            return;
        }

        builder.AppendLine()
            .AppendLine()
            .Append("memories: ")
            .Append(allMemoryIds.Count)
            .Append(" (")
            .Append(staleMemoryIds.Count)
            .Append(" stale; use get_memory <id> to fetch content)");
    }

    private static void CollectMemoryIds(
        IReadOnlyDictionary<int, MemoryNode[]> memoriesByNodeId,
        int nodeId,
        HashSet<Guid> allIds,
        HashSet<Guid> staleIds)
    {
        if (!memoriesByNodeId.TryGetValue(nodeId, out var memories))
        {
            return;
        }

        foreach (var memory in memories)
        {
            allIds.Add(memory.Id);
            if (memory.IsStale)
            {
                staleIds.Add(memory.Id);
            }
        }
    }

    private static void AppendTraceMemoryMetadata(
        StringBuilder builder,
        int depth,
        int nodeId,
        IReadOnlyDictionary<int, MemoryNode[]>? memoriesByNodeId)
    {
        if (memoriesByNodeId is null ||
            !memoriesByNodeId.TryGetValue(nodeId, out var memories) ||
            memories.Length == 0)
        {
            return;
        }

        foreach (var memory in memories)
        {
            builder.AppendLine();
            builder.Append(' ', (depth + 1) * 2)
                .Append("semantic: [")
                .Append(GetNodeTypeShorthand(NodeType.Memory))
                .Append("] id=")
                .Append(memory.Id)
                .Append(" intent=")
                .Append(memory.Intent)
                .Append(" stale=")
                .Append(memory.IsStale ? "true" : "false")
                .Append(" tags=")
                .Append(FormatTags(memory.Tags));
            if (memory.IsStale)
            {
                builder.Append(" hint=\"verify memory against current source before replacing or deleting\"");
            }
        }
    }

    private static string FormatTags(IReadOnlyList<string> tags)
        => tags.Count == 0
            ? "[]"
            : $"[{string.Join(", ", tags)}]";

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

    private static void AppendContextBucket(
        StringBuilder builder,
        string name,
        IReadOnlyList<Context360RelatedNode> items,
        int? total)
    {
        builder.AppendLine()
            .Append("  ")
            .Append(name);
        if (total is { } totalCount && totalCount > items.Count)
        {
            builder.Append(" (")
                .Append(items.Count)
                .Append(" of ")
                .Append(totalCount)
                .Append(')');
        }

        builder.Append(": ")
            .Append(FormatContext360RelatedNodes(items));
    }

    private static void AppendCalleeTree(
        StringBuilder builder,
        CodeNodeResult rootNode,
        IReadOnlyList<CodeNodeResult> callees,
        IReadOnlyList<ImpactedDependencyEdge> dependencies,
        IReadOnlyDictionary<int, MemoryNode[]>? memoriesByNodeId)
    {
        var nodeByCanonicalId = callees
            .Concat([rootNode])
            .GroupBy(static node => node.CanonicalId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var calleesByCallerId = dependencies
            .Where(edge =>
                nodeByCanonicalId.ContainsKey(edge.CallerId) &&
                nodeByCanonicalId.ContainsKey(edge.CalleeId))
            .GroupBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group => group
                    .Select(static edge => edge.CalleeId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(calleeId => nodeByCanonicalId[calleeId].FullyQualifiedName, StringComparer.Ordinal)
                    .ThenBy(calleeId => nodeByCanonicalId[calleeId].Id)
                    .ToArray(),
                StringComparer.Ordinal);
        var distances = ComputeShortestDistances(rootNode.CanonicalId, calleesByCallerId);
        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            rootNode.CanonicalId
        };

        AppendCalleeChildren(
            builder,
            rootNode.CanonicalId,
            1,
            nodeByCanonicalId,
            calleesByCallerId,
            distances,
            visited,
            memoriesByNodeId);

        foreach (var remaining in callees
            .Where(callee => visited.Add(callee.CanonicalId))
            .OrderBy(static callee => callee.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static callee => callee.Id))
        {
            builder.AppendLine();
            AppendTraceLine(builder, 1, remaining, memoriesByNodeId);
        }
    }

    private static Dictionary<string, int> ComputeShortestDistances(
        string rootCanonicalId,
        IReadOnlyDictionary<string, string[]> calleesByCallerId)
    {
        var distances = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [rootCanonicalId] = 0
        };
        var queue = new Queue<string>();
        queue.Enqueue(rootCanonicalId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!calleesByCallerId.TryGetValue(current, out var children))
            {
                continue;
            }

            var nextDistance = distances[current] + 1;
            foreach (var child in children)
            {
                if (distances.TryAdd(child, nextDistance))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return distances;
    }

    private static void AppendCalleeChildren(
        StringBuilder builder,
        string callerCanonicalId,
        int depth,
        IReadOnlyDictionary<string, CodeNodeResult> nodeByCanonicalId,
        IReadOnlyDictionary<string, string[]> calleesByCallerId,
        IReadOnlyDictionary<string, int> distances,
        HashSet<string> visited,
        IReadOnlyDictionary<int, MemoryNode[]>? memoriesByNodeId)
    {
        if (!calleesByCallerId.TryGetValue(callerCanonicalId, out var childIds))
        {
            return;
        }

        foreach (var childId in childIds)
        {
            if (!distances.TryGetValue(childId, out var shortestDepth) || shortestDepth != depth)
            {
                continue;
            }

            if (!visited.Add(childId))
            {
                continue;
            }

            builder.AppendLine();
            AppendTraceLine(builder, depth, nodeByCanonicalId[childId], memoriesByNodeId);
            AppendCalleeChildren(
                builder,
                childId,
                depth + 1,
                nodeByCanonicalId,
                calleesByCallerId,
                distances,
                visited,
                memoriesByNodeId);
        }
    }

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
