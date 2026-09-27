using FluentResults;
using ModelContextProtocol.Server;
using SharpSense.Application.CommandExecution.ExecuteProcess.Models;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.GraphStats.GetGraphStats.Models;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Application.Memory.GetMemories.Models;
using SharpSense.Application.Memory.GetMemory.Models;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Cli.Mcp;

[McpServerToolType]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class SharpSenseMcpTools
{
    [McpServerTool(ReadOnly = true), Description("Read repository graph statistics, language and embedding coverage, the last successful index, indexing phase timings, and actionable diagnostics. Does not modify the graph. No node ID is required.")]
    public static async Task<GraphStatsSnapshot> graph_stats(
        IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot> handler,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.graph_stats");
        activity.AddTag("mcp.tool", "graph_stats");

        try
        {
            return await handler.Handle(new GetGraphStatsQuery(), ct);
        }
        catch (Exception exception)
        {
            activity.RecordExceptionAndErrorStatus(exception);
            throw;
        }
    }

    [McpServerTool, Description("Run a local command, index its streamed output with a transient full-text search index, and return compact reduced context blocks for the supplied query.")]
    public static async Task<string> ctx_execute(
        ICommandHandler<ExecuteProcessCommand, Result<CommandExecutionResult>> handler,
        [Description("The OS command to run without a shell. Quote the full string when it contains spaces.")] string command,
        [Description("Optional FTS query used to locate relevant output lines. When omitted or unmatched, only a compact summary is returned.")] string? query = null,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.ctx_execute");
        activity.AddTag("mcp.tool", "ctx_execute");
        activity.AddTag("execution.command", command);

        try
        {
            var result = await handler.Handle(new ExecuteProcessCommand(command, query), ct);

            if (result.IsFailed)
            {
                activity.AddTag("execution.success", false);

                return TokenObjectNotation.SerializeCommandExecutionFailure(
                    command,
                    GetErrorMessage(result.Errors));
            }

            activity.AddTag("execution.success", result.Value.Success);
            activity.AddTag("execution.exit_code", result.Value.ExitCode);
            activity.AddTag("execution.matched_line_count", result.Value.MatchedLineCount);
            activity.AddTag("execution.block_count", result.Value.BlockCount);
            activity.AddTag("execution.truncated", result.Value.Truncated);

            return TokenObjectNotation.SerializeCommandExecutionResult(result.Value);
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Run hybrid BM25 and semantic search against the indexed repository.")]
    public static async Task<string> semantic_search(
        IQueryHandler<HybridSearchQuery, HybridSearchResult> searchHandler,
        [Description("Free-text query to run against the indexed repository.")] string query,
        [Description("Maximum number of hits to return. Defaults to 10.")] int limit = 10,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.semantic_search");
        activity.AddTag("mcp.tool", "semantic_search");
        activity.AddTag("search.query", query);

        try
        {
            var normalizedLimit = Math.Clamp(limit, 1, 50);
            var result = await searchHandler.Handle(
                new HybridSearchQuery(query, normalizedLimit),
                ct);

            activity.AddTag("search.result.count", result.Hits.Length);

            return TokenObjectNotation.SerializeSemanticSearch(result.Hits);
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Attach persistent semantic memory to a persisted code node id. The intent classifies the memory so it can be filtered at retrieval time.")]
    public static async Task<string> attach_memory(
        ICommandHandler<AttachMemoryCommand, Result> handler,
        [Description("The persisted integer ID of the target node.")] int nodeId,
        [Description("The markdown-formatted memory payload to attach.")] string content,
        [Description("Optional tags used for filtering and classification.")] string[]? tags = null,
        [Description("The memory's intent classification. One of: Convention, Invariant, Todo, Warning, Decision. Defaults to Convention.")] SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent intent = SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent.Convention,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.attach_memory");
        activity.AddTag("mcp.tool", "attach_memory");
        activity.AddTag("memory.node_id", nodeId);
        activity.AddTag("memory.intent", intent.ToString());

        try
        {
            var result = await handler.Handle(
                new AttachMemoryCommand(nodeId, content, tags, intent),
                ct);

            activity.AddTag("memory.success", result.IsSuccess);

            return result.IsSuccess
                ? $"attached memory to node {nodeId} (intent={intent})"
                : $"attach_memory failed: {GetErrorMessage(result.Errors)}";
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Remove a previously attached semantic memory by its persistent Guid.")]
    public static async Task<string> delete_memory(
        ICommandHandler<DeleteMemoryCommand, Result> handler,
        [Description("The persistent Guid of the memory to remove.")] Guid memoryId,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.delete_memory");
        activity.AddTag("mcp.tool", "delete_memory");
        activity.AddTag("memory.id", memoryId);

        try
        {
            var result = await handler.Handle(new DeleteMemoryCommand(memoryId), ct);
            activity.AddTag("memory.success", result.IsSuccess);

            return result.IsSuccess
                ? $"deleted memory {memoryId}"
                : $"delete_memory failed: {GetErrorMessage(result.Errors)}";
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Fetch the full markdown content of a previously attached semantic memory by its persistent Guid. Use this after context or trace surfaces a memory id and you need the full text.")]
    public static async Task<string> get_memory(
        IQueryHandler<GetMemoryQuery, Result<MemoryNode>> handler,
        [Description("The persistent Guid of the memory to fetch.")] Guid memoryId,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.get_memory");
        activity.AddTag("mcp.tool", "get_memory");
        activity.AddTag("memory.id", memoryId);

        try
        {
            var result = await handler.Handle(new GetMemoryQuery(memoryId), ct);
            if (result.IsFailed)
            {
                activity.AddTag("memory.success", false);

                return $"get_memory failed: {GetErrorMessage(result.Errors)}";
            }

            activity.AddTag("memory.success", true);
            activity.AddTag("memory.stale", result.Value!.IsStale);

            return TokenObjectNotation.SerializeMemory(result.Value);
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Batch-fetch the full content of multiple memories by their persistent Guids. Returns one rendered memory block per id, in a single round-trip. Use this after a multi-step trace surfaces a list of memory ids.")]
    public static async Task<string> get_memories(
        IQueryHandler<GetMemoriesQuery, Result<IReadOnlyDictionary<Guid, MemoryNode>>> handler,
        [Description("The persistent Guids of the memories to fetch.")] Guid[] memoryIds,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.get_memories");
        activity.AddTag("mcp.tool", "get_memories");
        activity.AddTag("memory.count", memoryIds?.Length ?? 0);

        try
        {
            var result = await handler.Handle(new GetMemoriesQuery(memoryIds ?? []), ct);
            if (result.IsFailed)
            {
                activity.AddTag("memory.success", false);

                return $"get_memories failed: {GetErrorMessage(result.Errors)}";
            }

            activity.AddTag("memory.success", true);
            activity.AddTag("memory.returned", result.Value!.Count);

            return TokenObjectNotation.SerializeMemories(result.Value);
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool]
    [Description("Gets an instant 360-degree architectural snapshot of a node. Returns immediate callers, callees, and inheritance hierarchy for a persisted node ID. Use this to understand a node's immediate context and blast radius before deep tracing.")]
    public static async Task<string> context(
        IQueryHandler<GetNodeContextQuery, Context360Result> handler,
        IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>> memoryHandler,
        [Description("The persisted integer ID of the target node.")] int nodeId,
        [Description("Optional edge-category mask. Defaults to Structural.")] EdgeCategory edgeCategories = EdgeCategory.Structural,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.context");
        activity.AddTag("mcp.tool", "context");
        activity.AddTag("context.node_id", nodeId);
        activity.AddTag("context.edge_categories", edgeCategories);

        try
        {
            var result = await handler.Handle(
                new GetNodeContextQuery(
                    nodeId,
                    10),
                ct);
            var semanticContext = edgeCategories.HasFlag(EdgeCategory.Semantic)
                ? (await memoryHandler.Handle(new GetNodeMemoriesQuery(nodeId), ct)).Value ?? []
                : [];

            activity.AddTag("context.callers.count", result.Callers.Length);
            activity.AddTag("context.callees.count", result.Callees.Length);
            activity.AddTag("context.implementers.count", result.Implementers.Length);
            activity.AddTag("context.inherits.count", result.Inherits.Length);
            activity.AddTag("context.semantic.count", semanticContext.Length);

            return TokenObjectNotation.SerializeContext360(result, semanticContext);
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Trace dependencies for a specific Node ID. Use direction='caller' to see upstream blast radius. Use direction='callee' to see downstream execution path.")]
    public static async Task<string> trace_node(
        IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult> impactHandler,
        IQueryHandler<TraceQuery, CodeNodeResult[]> traceHandler,
        IMemoryRepository memoryRepository,
        ITraceNavigator traceNavigator,
        [Description("The exact Node ID to trace.")] string nodeId,
        [Description("Direction of the trace: 'caller' (upstream) or 'callee' (downstream).")] TraceDirection direction = TraceDirection.Callee,
        [Description("Optional edge-category mask. Defaults to Structural.")] EdgeCategory edgeCategories = EdgeCategory.Structural,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.trace_node");
        activity.AddTag("mcp.tool", "trace_node");
        activity.AddTag("trace.node_id", nodeId);

        try
        {
            activity.AddTag("trace.direction", direction);
            activity.AddTag("trace.edge_categories", edgeCategories);
            var rootNode = await traceNavigator.GetRootNode(nodeId, ct);
            if (rootNode is null)
            {
                return string.Empty;
            }

            CodeNodeResult[] nodes;
            ImpactAnalysisResult? impactResult = null;

            switch (direction)
            {
                case TraceDirection.Caller:
                    {
                        impactResult = await impactHandler.Handle(
                            new ImpactAnalysisQuery(nodeId),
                            ct);

                        activity.AddTag("trace.edge.count", impactResult.Dependencies.Length);
                        nodes = MapImpactedNodes(impactResult.ImpactedNodes);
                        break;
                    }
                case TraceDirection.Callee:
                    nodes = await traceHandler.Handle(
                        new TraceQuery(nodeId),
                        ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(direction),
                        direction,
                        "Direction must be either caller or callee.");
            }

            var memoriesByNodeId = edgeCategories.HasFlag(EdgeCategory.Semantic)
                ? await LoadTraceMemories(memoryRepository, rootNode, nodes, ct)
                : new Dictionary<int, MemoryNode[]>();

            activity.AddTag("trace.node.count", nodes.Length);
            activity.AddTag("trace.semantic.count", memoriesByNodeId.Values.Sum(static memories => memories.Length));

            return direction switch
            {
                TraceDirection.Caller => TokenObjectNotation.SerializeCallerTrace(rootNode, nodes, impactResult?.Dependencies ?? [], memoriesByNodeId),
                TraceDirection.Callee => TokenObjectNotation.SerializeCalleeTrace(rootNode, nodes, memoriesByNodeId),
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
            };
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Find direct derived classes or interface implementers for a persisted node ID.")]
    public static async Task<string> get_inheritors(
        IQueryHandler<GetInheritorsQuery, CodeNodeResult[]> inheritorsHandler,
        [Description("The integer ID of the target base class, abstract class, or interface.")] int nodeId,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.get_inheritors");
        activity.AddTag("mcp.tool", "get_inheritors");
        activity.AddTag("inheritors.node_id", nodeId);

        try
        {
            var result = await inheritorsHandler.Handle(
                new GetInheritorsQuery(nodeId),
                ct);

            activity.AddTag("inheritors.result.count", result.Length);

            return ToonOutputFormatter.Format(result);
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    private static Task<IReadOnlyDictionary<int, MemoryNode[]>> LoadTraceMemories(
        IMemoryRepository memoryRepository,
        CodeNodeResult rootNode,
        IReadOnlyList<CodeNodeResult> nodes,
        CancellationToken ct)
        => memoryRepository.GetNodeMemories(
            [rootNode.Id, .. nodes.Select(static node => node.Id)],
            intents: null,
            ct);

    private static CodeNodeResult[] MapImpactedNodes(IEnumerable<ImpactedCodeNode> impactedNodes)
        => [.. impactedNodes.Select(static node => new CodeNodeResult(
            node.Id,
            node.CanonicalId,
            node.ProjectId,
            node.FullyQualifiedName,
            node.DisplayName,
            node.NodeType,
            node.RelativeFilePath,
            node.StartLine,
            node.EndLine,
            node.Summary))];

    private static string GetErrorMessage(IEnumerable<IError> errors)
        => string.Join("; ", errors.Select(static error => error.Message));
}
