using FluentResults;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpSense.Application.CommandExecution.ExecuteProcess.Models;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.GraphStats.GetGraphStats.Models;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Application.Memory.GetMemories.Models;
using SharpSense.Application.Memory.GetMemory.Models;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.GetTraceGraph.Models;
using SharpSense.Application.Trace.Models;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Result = FluentResults.Result;

namespace SharpSense.Cli.Mcp;

[McpServerToolType]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class SharpSenseMcpTools
{
    [McpServerTool(ReadOnly = true), Description("Read workspace graph statistics, language and embedding coverage, the last successful index, indexing phase timings, and actionable diagnostics. Does not modify the graph. No node ID is required.")]
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

    [McpServerTool, Description("Run an authorized build, test, or diagnostic command expected to produce large output and return bounded matching excerpts. Use semantic_search to discover code and ordinary source tools for small file reads.")]
    public static async Task<CallToolResult> ctx_execute(
        ICommandHandler<ExecuteProcessCommand, Result<CommandExecutionResult>> handler,
        [Description("Executable followed by arguments. No implicit shell is used. Quote individual arguments containing spaces; pipelines, redirection, and chaining require an explicit shell.")] string command,
        [Description("Optional FTS expression selecting relevant output lines, for example error OR warning OR failed. This is not a natural-language instruction. Missing or unmatched queries return a compact summary.")] string? query = null,
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

                return TextResult(
                    TokenObjectNotation.SerializeCommandExecutionFailure(
                        command,
                        GetErrorMessage(result.Errors)),
                    isError: true);
            }

            activity.AddTag("execution.success", result.Value.Success);
            activity.AddTag("execution.exit_code", result.Value.ExitCode);
            activity.AddTag("execution.matched_line_count", result.Value.MatchedLineCount);
            activity.AddTag("execution.block_count", result.Value.BlockCount);
            activity.AddTag("execution.truncated", result.Value.Truncated);

            return TextResult(TokenObjectNotation.SerializeCommandExecutionResult(result.Value));
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool(ReadOnly = true), Description("Find code implementing a behavior or concept when its symbol name or file location is unknown. Use for discovery during debugging, feature work, refactoring, and review. Returns source locations and node IDs for inspection. Use ordinary text search for exact strings and exhaustive occurrence checks.")]
    public static async Task<CallToolResult> semantic_search(
        IQueryHandler<HybridSearchQuery, Result<HybridSearchResult>> searchHandler,
        [Description("A concise behavior, concept, or distinctive symbol to locate. Plain text only; FTS operators and column selectors are not supported.")] string query,
        [Description("Maximum number of hits to return (1-50). Defaults to 10.")] int limit = 10,
        [Description("Optional project name or canonical project ID to scope results.")] string? project = null,
        [Description("Optional node types to include (Class, Interface, Method, Property, Field, Document, Component).")] NodeType[]? nodeTypes = null,
        [Description("When true, persisted memory content and tags also contribute to ranking.")] bool includeMemories = false,
        [Description("Optional memory tag filters applied when includeMemories is true.")] string[]? tags = null,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.semantic_search");
        activity.AddTag("mcp.tool", "semantic_search");
        activity.AddTag("search.query", query);

        try
        {
            var normalizedLimit = Math.Clamp(limit, 1, 50);
            var result = await searchHandler.Handle(
                new HybridSearchQuery(
                    query,
                    normalizedLimit,
                    project,
                    nodeTypes,
                    includeMemories,
                    tags),
                ct);

            if (result.IsFailed)
            {
                activity.SetError();

                return TextResult($"semantic_search failed: {GetErrorMessage(result.Errors)}", isError: true);
            }

            activity.AddTag("search.result.count", result.Value.Hits.Length);

            return TextResult(TokenObjectNotation.SerializeSemanticSearch(result.Value.Hits));
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Attach persistent semantic memory to a persisted code node id. Returns the created memory GUID. The intent classifies the memory so it can be filtered at retrieval time.")]
    public static async Task<CallToolResult> attach_memory(
        ICommandHandler<AttachMemoryCommand, Result<MemoryNode>> handler,
        [Description("The persisted integer ID of the target node.")] int nodeId,
        [Description("The markdown-formatted memory payload to attach.")] string content,
        [Description("Optional tags used for filtering and classification.")] string[]? tags = null,
        [Description("The memory's intent classification. One of: Convention, Invariant, Todo, Warning, Decision. Defaults to Convention.")] MemoryIntent intent = MemoryIntent.Convention,
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
            if (result.IsSuccess)
            {
                activity.AddTag("memory.id", result.Value.Id);
            }

            return TextResult(
                result.IsSuccess
                    ? $"attached memory {result.Value.Id} to node {nodeId} (intent={result.Value.Intent}, stale={(result.Value.IsStale ? "true" : "false")})"
                    : $"attach_memory failed: {GetErrorMessage(result.Errors)}",
                isError: result.IsFailed);
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool(ReadOnly = true), Description("List persisted semantic memories across the workspace with optional intent, tag, staleness, text, and paging filters. Returns metadata and short previews; fetch full Markdown with get_memory or get_memories.")]
    public static async Task<CallToolResult> list_memories(
        IQueryHandler<ListMemoriesQuery, Result<ListMemoriesResult>> handler,
        [Description("Optional intent filter: Convention, Invariant, Todo, Warning, or Decision.")] MemoryIntent[]? intents = null,
        [Description("Optional tags; all supplied tags must match.")] string[]? tags = null,
        [Description("When true, returns only memories whose target node hash has changed.")] bool staleOnly = false,
        [Description("Optional case-insensitive text matched against content, target symbol, path, and tags.")] string? searchText = null,
        [Description("Maximum number of memories to return (1-200). Defaults to 50.")] int limit = 50,
        [Description("Number of matching memories to skip for pagination. Defaults to 0.")] int offset = 0,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.list_memories");
        activity.AddTag("mcp.tool", "list_memories");
        activity.AddTag("memory.stale_only", staleOnly);

        try
        {
            var result = await handler.Handle(
                new ListMemoriesQuery(intents, tags, staleOnly, searchText, limit, offset),
                ct);
            if (result.IsFailed)
            {
                activity.SetError();

                return TextResult($"list_memories failed: {GetErrorMessage(result.Errors)}", isError: true);
            }

            activity.AddTag("memory.returned", result.Value.Items.Length);
            activity.AddTag("memory.total", result.Value.TotalCount);
            activity.AddTag("memory.stale_count", result.Value.StaleCount);

            return TextResult(TokenObjectNotation.SerializeMemoryList(result.Value));
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Remove a previously attached semantic memory by its persistent Guid.")]
    public static async Task<CallToolResult> delete_memory(
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

            return TextResult(
                result.IsSuccess
                    ? $"deleted memory {memoryId}"
                    : $"delete_memory failed: {GetErrorMessage(result.Errors)}",
                isError: result.IsFailed);
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool(ReadOnly = true), Description("Fetch the full markdown content of a previously attached semantic memory by its persistent Guid. Use this after context or trace surfaces a memory id and you need the full text.")]
    public static async Task<CallToolResult> get_memory(
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

                return TextResult($"get_memory failed: {GetErrorMessage(result.Errors)}", isError: true);
            }

            activity.AddTag("memory.success", true);
            activity.AddTag("memory.stale", result.Value.IsStale);

            return TextResult(TokenObjectNotation.SerializeMemory(result.Value));
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool(ReadOnly = true), Description("Batch-fetch the full content of multiple memories by their persistent Guids. Returns one rendered memory block per id, in a single round-trip. Use this after a multi-step trace surfaces a list of memory ids.")]
    public static async Task<CallToolResult> get_memories(
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

                return TextResult($"get_memories failed: {GetErrorMessage(result.Errors)}", isError: true);
            }

            activity.AddTag("memory.success", true);
            activity.AddTag("memory.returned", result.Value.Count);

            return TextResult(TokenObjectNotation.SerializeMemories(result.Value));
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Gets an instant 360-degree architectural snapshot of a node. Returns immediate callers, callees, and inheritance hierarchy for a persisted node ID, reporting totals when a bucket is truncated. Use sections to narrow output.")]
    public static async Task<CallToolResult> context(
        IQueryHandler<GetNodeContextQuery, Result<Context360Result>> handler,
        IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>> memoryHandler,
        [Description("The persisted integer ID of the target node.")] int nodeId,
        [Description("Optional edge-category mask: None, Structural, Semantic or All. Defaults to Structural; use All to include memory metadata.")] EdgeCategory edgeCategories = EdgeCategory.Structural,
        [Description("Maximum related nodes per section (1-50). Defaults to 10.")] int limit = 10,
        [Description("Optional subset of sections to return: Callers, Implementers, Callees, Inherits, Parents, Children. Omit for all sections.")] ContextSection[]? sections = null,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.context");
        activity.AddTag("mcp.tool", "context");
        activity.AddTag("context.node_id", nodeId);
        activity.AddTag("context.edge_categories", edgeCategories);

        try
        {
            var normalizedLimit = Math.Clamp(limit, 1, 50);
            var result = await handler.Handle(
                new GetNodeContextQuery(
                    nodeId,
                    normalizedLimit,
                    sections),
                ct);
            if (result.IsFailed)
            {
                activity.SetError();

                return TextResult($"context failed: {GetErrorMessage(result.Errors)}", isError: true);
            }

            var nodeContext = result.Value;
            MemoryNode[] semanticContext = [];
            if (edgeCategories.HasFlag(EdgeCategory.Semantic))
            {
                var memoriesResult = await memoryHandler.Handle(new GetNodeMemoriesQuery(nodeId), ct);
                if (memoriesResult.IsSuccess)
                {
                    semanticContext = memoriesResult.Value;
                }
            }

            activity.AddTag("context.callers.count", nodeContext.Callers.Length);
            activity.AddTag("context.callees.count", nodeContext.Callees.Length);
            activity.AddTag("context.implementers.count", nodeContext.Implementers.Length);
            activity.AddTag("context.inherits.count", nodeContext.Inherits.Length);
            activity.AddTag("context.semantic.count", semanticContext.Length);

            return TextResult(TokenObjectNotation.SerializeContext360(nodeContext, semanticContext));
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool(ReadOnly = true), Description("Trace dependencies for a persisted integer Node ID. Use direction='caller' for upstream blast radius (default depth 3) or direction='callee' for downstream dependencies (default depth 1). Static relationships do not establish execution order.")]
    public static async Task<CallToolResult> trace_node(
        IQueryHandler<GetTraceGraphQuery, Result<TraceGraphResult>> traceGraphHandler,
        IMemoryRepository memoryRepository,
        [Description("The persisted integer ID of the target node to trace.")] int nodeId,
        [Description("Direction of the trace: 'caller' (upstream) or 'callee' (downstream). Defaults to 'callee'.")] TraceDirection direction = TraceDirection.Callee,
        [Description("Optional edge-category mask: None, Structural, Semantic or All. Defaults to Structural; use All to include memory metadata.")] EdgeCategory edgeCategories = EdgeCategory.Structural,
        [Description("Maximum traversal depth (1-10). Defaults to 3 for caller traces and 1 for callee traces.")] int? maxDepth = null,
        [Description("When true, include ParentOf structural containment edges in addition to functional edges.")] bool includeStructural = false,
        [Description("Optional explicit edge types to traverse (overrides includeStructural).")] EdgeType[]? includedEdgeTypes = null,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.trace_node");
        activity.AddTag("mcp.tool", "trace_node");
        activity.AddTag("trace.node_id", nodeId);

        try
        {
            activity.AddTag("trace.direction", direction);
            activity.AddTag("trace.edge_categories", edgeCategories);

            var resolvedDepth = maxDepth ?? (direction == TraceDirection.Caller ? 3 : 1);
            var normalizedDepth = Math.Clamp(resolvedDepth, 1, 10);
            var resolvedEdgeTypes = includedEdgeTypes is { Length: > 0 }
                ? includedEdgeTypes
                : includeStructural
                    ? KnowledgeGraphEdgeTypes.All
                    : null;

            var graphResult = await traceGraphHandler.Handle(
                new GetTraceGraphQuery(
                    nodeId,
                    direction,
                    normalizedDepth,
                    resolvedEdgeTypes),
                ct);
            if (graphResult.IsFailed)
            {
                activity.SetError();

                return TextResult($"trace_node failed: {GetErrorMessage(graphResult.Errors)}", isError: true);
            }

            var graph = graphResult.Value;
            var memoriesByNodeId = edgeCategories.HasFlag(EdgeCategory.Semantic)
                ? await LoadTraceMemories(memoryRepository, graph.Root, graph.Nodes, ct)
                : new Dictionary<int, MemoryNode[]>();

            activity.AddTag("trace.node.count", graph.Nodes.Length);
            activity.AddTag("trace.edge.count", graph.Dependencies.Length);
            activity.AddTag("trace.truncated", graph.Truncated);
            activity.AddTag("trace.semantic.count", memoriesByNodeId.Values.Sum(static memories => memories.Length));

            var output = direction switch
            {
                TraceDirection.Caller => TokenObjectNotation.SerializeCallerTrace(
                    graph.Root,
                    graph.Nodes,
                    graph.Dependencies,
                    memoriesByNodeId),
                TraceDirection.Callee => TokenObjectNotation.SerializeCalleeTrace(
                    graph.Root,
                    graph.Nodes,
                    memoriesByNodeId,
                    graph.Dependencies,
                    graph.Truncated),
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
            };

            return TextResult(output);
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool(ReadOnly = true), Description("Find direct derived classes or interface implementers for a persisted node ID.")]
    public static async Task<CallToolResult> get_inheritors(
        IQueryHandler<GetInheritorsQuery, CodeNodeResult[]> inheritorsHandler,
        ITraceNavigator traceNavigator,
        [Description("The integer ID of the target base class, abstract class, or interface.")] int nodeId,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.get_inheritors");
        activity.AddTag("mcp.tool", "get_inheritors");
        activity.AddTag("inheritors.node_id", nodeId);

        try
        {
            if (nodeId <= 0)
            {
                activity.SetError();

                return TextResult("get_inheritors failed: NodeId must be greater than zero.", isError: true);
            }

            var rootNode = await traceNavigator.GetRootNode(nodeId.ToString(CultureInfo.InvariantCulture), ct);
            if (rootNode is null)
            {
                activity.SetError();

                return TextResult($"get_inheritors failed: No persisted node exists for id {nodeId}.", isError: true);
            }

            var result = await inheritorsHandler.Handle(
                new GetInheritorsQuery(nodeId),
                ct);

            activity.AddTag("inheritors.result.count", result.Length);

            return TextResult(ToonOutputFormatter.Format(result));
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


    private static CallToolResult TextResult(string text, bool isError = false)
        => new()
        {
            IsError = isError,
            Content =
            [
                new TextContentBlock
                {
                    Text = text
                }
            ]
        };

    private static string GetErrorMessage(IEnumerable<IError> errors)
        => string.Join("; ", errors.Select(static error => error.Message));
}
