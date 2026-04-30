using ModelContextProtocol.Server;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Cli.Shared;
using System.ComponentModel;

namespace SharpSense.Cli.Mcp;

[McpServerToolType]
internal sealed class SharpSenseMcpTools
{
    private static readonly ToonOutputFormatter _toonOutputFormatter = new();

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

            return string.Join(
                Environment.NewLine,
                result.Hits.Select(static hit => ToonOutputFormatter.FormatNode(
                    hit.NodeType,
                    hit.Id,
                    hit.DisplayName,
                    hit.RelativeFilePath,
                    hit.StartLine,
                    hit.EndLine)));
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

            CodeNodeResult[] nodes;

            switch (direction)
            {
                case TraceDirection.Caller:
                {
                    var result = await impactHandler
                        .Handle(
                            new ImpactAnalysisQuery(nodeId),
                            ct);

                    activity.AddTag("trace.edge.count", result.Dependencies.Length);
                    nodes = MapImpactedNodes(result.ImpactedNodes);
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
            return _toonOutputFormatter.Format(nodes);
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
            return _toonOutputFormatter.Format(result);
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
}
