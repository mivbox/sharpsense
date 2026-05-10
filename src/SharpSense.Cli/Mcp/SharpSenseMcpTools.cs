using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Cli.Shared;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Cli.Mcp;

[McpServerToolType]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class SharpSenseMcpTools
{
    [McpServerTool, Description("Run a local command, index its streamed output with a transient full-text search index, and return compact reduced context blocks for the supplied query.")]
    public static async Task<string> ctx_execute(
        ICommandProcessRunner processRunner,
        IExecuteLogIndexFactory executeLogIndexFactory,
        IOptions<SharpSenseCliOptions> cliOptions,
        [Description("The OS command to run without a shell. Quote the full string when it contains spaces.")] string command,
        [Description("Optional FTS query used to locate relevant output lines. When omitted or unmatched, only a compact summary is returned.")] string? query = null,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.ctx_execute");
        activity.AddTag("mcp.tool", "ctx_execute");
        activity.AddTag("execution.command", command);

        try
        {
            var result = await CommandExecutionReducer.Execute(
                new CommandExecutionRequest(command, query),
                processRunner,
                executeLogIndexFactory,
                cliOptions.Value.RepositoryRoot,
                ct);

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
            var result = await searchHandler
                .Handle(
                    new HybridSearchQuery(query, normalizedLimit),
                    ct)
                ;

            activity.AddTag("search.result.count", result.Hits.Length);
            return TokenObjectNotation.SerializeSemanticSearch(result.Hits);
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
        IContextService contextService,
        [Description("The persisted integer ID of the target node.")] int nodeId,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.context");
        activity.AddTag("mcp.tool", "context");
        activity.AddTag("context.node_id", nodeId);

        try
        {
            var result = await contextService.GetNodeContext(
                nodeId,
                10,
                ct);

            activity.AddTag("context.callers.count", result.Callers.Length);
            activity.AddTag("context.callees.count", result.Callees.Length);
            activity.AddTag("context.implementers.count", result.Implementers.Length);
            activity.AddTag("context.inherits.count", result.Inherits.Length);
            return TokenObjectNotation.SerializeContext360(result);
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
        ITraceNavigator traceNavigator,
        [Description("The exact Node ID to trace.")] string nodeId,
        [Description("Direction of the trace: 'caller' (upstream) or 'callee' (downstream).")] TraceDirection direction = TraceDirection.Callee,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.trace_node");
        activity.AddTag("mcp.tool", "trace_node");
        activity.AddTag("trace.node_id", nodeId);

        try
        {
            activity.AddTag("trace.direction", direction);
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
                    impactResult = await impactHandler
                        .Handle(
                            new ImpactAnalysisQuery(nodeId),
                            ct);

                    activity.AddTag("trace.edge.count", impactResult.Dependencies.Length);
                    nodes = MapImpactedNodes(impactResult.ImpactedNodes);
                    break;
                }
                case TraceDirection.Callee:
                    nodes = await traceHandler
                        .Handle(
                            new TraceQuery(nodeId),
                            ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(direction),
                        direction,
                        "Direction must be either caller or callee.");
            }

            activity.AddTag("trace.node.count", nodes.Length);
            return direction switch
            {
                TraceDirection.Caller => TokenObjectNotation.SerializeCallerTrace(rootNode, nodes, impactResult?.Dependencies ?? []),
                TraceDirection.Callee => TokenObjectNotation.SerializeCalleeTrace(rootNode, nodes),
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
            };
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerTool, Description("Acts like Ctrl+R, R in JetBrains Rider. Use this to semantically rename a method, class, or property. It automatically updates all callers and references across the entire codebase. Provide ONLY the new identifier name (e.g., 'ProcessPaymentAsync'), not a full signature.")]
    public static async Task<string> refactor_symbol(
        IRefactorSymbolService refactorSymbolService,
        [Description("The persisted integer ID of the symbol to rename.")] int nodeId,
        [Description("The new identifier name only, such as 'ProcessPaymentAsync'. Do not provide a signature or code block.")] string newName,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.tool.refactor_symbol");
        activity.AddTag("mcp.tool", "refactor_symbol");
        activity.AddTag("refactor.node_id", nodeId);

        try
        {
            var result = await refactorSymbolService.RenameSymbol(
                nodeId,
                newName,
                ct: ct);

            activity.AddTag("refactor.success", result.Success);
            activity.AddTag("refactor.modified_file.count", result.ModifiedFilePaths.Length);
            return TokenObjectNotation.SerializeRefactorResult(result);
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
            var result = await inheritorsHandler
                .Handle(
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

    private static string GetErrorMessage(IEnumerable<FluentResults.IError> errors)
        => string.Join("; ", errors.Select(static error => error.Message));
}
