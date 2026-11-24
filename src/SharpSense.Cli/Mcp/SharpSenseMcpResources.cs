using FluentResults;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.GraphStats.GetGraphStats.Models;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Errors;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace SharpSense.Cli.Mcp;

[McpServerResourceType]
internal sealed class SharpSenseMcpResources
{
    internal const string WorkspaceStatsUri = "sharpsense://workspace/stats";
    internal const string WorkspaceMemoriesUri = "sharpsense://workspace/memories";
    internal const string StaleMemoriesUri = "sharpsense://workspace/memories/stale";
    internal const string NodeContextUriTemplate = "sharpsense://node/{nodeId}/context";

    private const string NodeContextPrefix = "sharpsense://node/";
    private const string NodeContextSuffix = "/context";
    private const int DefaultMemoriesLimit = 100;
    private const int DefaultContextMaxRelated = 10;

    internal static bool RequiresDatabaseInitialization(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return false;
        }

        if (string.Equals(uri, WorkspaceMemoriesUri, StringComparison.Ordinal) ||
            string.Equals(uri, StaleMemoriesUri, StringComparison.Ordinal))
        {
            return true;
        }

        return TryParseNodeContextNodeId(uri, out var nodeId) && nodeId > 0;
    }

    [McpServerResource(
        UriTemplate = WorkspaceStatsUri,
        Name = "workspace_stats",
        Title = "Workspace graph statistics",
        MimeType = "application/json")]
    [Description("Read workspace graph statistics, language and embedding coverage, the last successful index, indexing phase timings, and actionable diagnostics. Does not modify missing or incompatible databases.")]
    public static async Task<TextResourceContents> GetWorkspaceStats(
        IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot> handler,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.resource.workspace_stats");
        activity.AddTag("mcp.resource", WorkspaceStatsUri);

        try
        {
            var snapshot = await handler.Handle(new GetGraphStatsQuery(), ct);
            var json = JsonSerializer.Serialize(snapshot, McpJsonUtilities.DefaultOptions);

            return new TextResourceContents
            {
                Uri = WorkspaceStatsUri,
                MimeType = "application/json",
                Text = json
            };
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerResource(
        UriTemplate = WorkspaceMemoriesUri,
        Name = "workspace_memories",
        Title = "Workspace semantic memories",
        MimeType = "text/plain")]
    [Description("Compact summary of up to 100 persisted semantic memories in the workspace, ordered newest first. Fetch full Markdown with get_memory or get_memories.")]
    public static async Task<TextResourceContents> GetWorkspaceMemories(
        IQueryHandler<ListMemoriesQuery, Result<ListMemoriesResult>> handler,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.resource.workspace_memories");
        activity.AddTag("mcp.resource", WorkspaceMemoriesUri);

        try
        {
            var result = await handler.Handle(new ListMemoriesQuery(Limit: DefaultMemoriesLimit), ct);
            if (result.IsFailed)
            {
                throw CreateResourceException("Failed to read workspace memories", result.Errors);
            }

            activity.AddTag("memory.returned", result.Value.Items.Length);
            activity.AddTag("memory.total", result.Value.TotalCount);
            activity.AddTag("memory.stale_count", result.Value.StaleCount);

            return new TextResourceContents
            {
                Uri = WorkspaceMemoriesUri,
                MimeType = "text/plain",
                Text = TokenObjectNotation.SerializeMemoryList(result.Value)
            };
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerResource(
        UriTemplate = StaleMemoriesUri,
        Name = "stale_memories",
        Title = "Stale workspace memories",
        MimeType = "text/plain")]
    [Description("Compact summary of up to 100 persisted semantic memories whose target symbol hash changed since the note was recorded.")]
    public static async Task<TextResourceContents> GetStaleMemories(
        IQueryHandler<ListMemoriesQuery, Result<ListMemoriesResult>> handler,
        CancellationToken ct = default)
    {
        using var activity = SharpSenseTraceSpan.Start("mcp.resource.stale_memories");
        activity.AddTag("mcp.resource", StaleMemoriesUri);

        try
        {
            var result = await handler.Handle(
                new ListMemoriesQuery(StaleOnly: true, Limit: DefaultMemoriesLimit),
                ct);
            if (result.IsFailed)
            {
                throw CreateResourceException("Failed to read stale workspace memories", result.Errors);
            }

            activity.AddTag("memory.returned", result.Value.Items.Length);
            activity.AddTag("memory.total", result.Value.TotalCount);

            return new TextResourceContents
            {
                Uri = StaleMemoriesUri,
                MimeType = "text/plain",
                Text = TokenObjectNotation.SerializeMemoryList(result.Value)
            };
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    [McpServerResource(
        UriTemplate = NodeContextUriTemplate,
        Name = "node_context",
        Title = "Node architectural context",
        MimeType = "text/plain")]
    [Description("Compact 360-degree architectural snapshot (immediate callers, callees, inheritance, hierarchy, and attached memory metadata) for a persisted integer node ID.")]
    public static async Task<TextResourceContents> GetNodeContext(
        IQueryHandler<GetNodeContextQuery, Result<Context360Result>> handler,
        IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>> memoryHandler,
        [Description("The persisted integer ID of the target node.")] int nodeId,
        CancellationToken ct = default)
    {
        var uri = $"{NodeContextPrefix}{nodeId.ToString(CultureInfo.InvariantCulture)}{NodeContextSuffix}";
        using var activity = SharpSenseTraceSpan.Start("mcp.resource.node_context");
        activity.AddTag("mcp.resource", uri);
        activity.AddTag("context.node_id", nodeId);

        try
        {
            if (nodeId <= 0)
            {
                throw new McpProtocolException(
                    "NodeId must be greater than zero.",
                    McpErrorCode.InvalidParams);
            }

            var result = await handler.Handle(
                new GetNodeContextQuery(nodeId, DefaultContextMaxRelated),
                ct);
            if (result.IsFailed)
            {
                throw CreateResourceException($"Failed to read context for node {nodeId}", result.Errors);
            }

            var memoriesResult = await memoryHandler.Handle(new GetNodeMemoriesQuery(nodeId), ct);
            var semanticContext = memoriesResult.IsSuccess
                ? memoriesResult.Value
                : [];

            activity.AddTag("context.callers.count", result.Value.Callers.Length);
            activity.AddTag("context.callees.count", result.Value.Callees.Length);
            activity.AddTag("context.semantic.count", semanticContext.Length);

            return new TextResourceContents
            {
                Uri = uri,
                MimeType = "text/plain",
                Text = TokenObjectNotation.SerializeContext360(result.Value, semanticContext)
            };
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    private static bool TryParseNodeContextNodeId(string uri, out int nodeId)
    {
        nodeId = 0;
        if (!uri.StartsWith(NodeContextPrefix, StringComparison.Ordinal) ||
            !uri.EndsWith(NodeContextSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var start = NodeContextPrefix.Length;
        var length = uri.Length - start - NodeContextSuffix.Length;
        if (length <= 0)
        {
            return false;
        }

        return int.TryParse(
            uri.AsSpan(start, length),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out nodeId);
    }

    private static McpProtocolException CreateResourceException(string prefix, IReadOnlyList<IError> errors)
    {
        var message = $"{prefix}: {string.Join("; ", errors.Select(static error => error.Message))}";
        var serviceError = errors.OfType<ServiceError>().FirstOrDefault();
        var errorCode = serviceError?.ErrorCode switch
        {
            ServiceErrorCode.NotFound => McpErrorCode.ResourceNotFound,
            ServiceErrorCode.InvalidArgument => McpErrorCode.InvalidParams,
            _ => McpErrorCode.InternalError
        };

        return new McpProtocolException(message, errorCode);
    }
}
