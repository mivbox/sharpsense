using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.DependencyGraph.GetGraphPages.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class GraphPageEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet(
            "/api/graph/nodes/page",
            async (
                int[]? directoryIds, string? cursor, string? revision, int? pageSize, bool? includeTotal,
                IQueryHandler<GetGraphNodesPageQuery, GraphNodesPage> handler, CancellationToken ct) =>
                await handler.Handle(
                    new GetGraphNodesPageQuery(new GraphPageRequest(
                        directoryIds ?? [],
                        pageSize ?? 2_000,
                        cursor,
                        revision,
                        includeTotal ?? false)),
                    ct))
            .WithName("GetGraphNodesPage")
            .WithTags("Graph")
            .Produces<GraphNodesPage>()
            .ProducesProblem(400)
            .ProducesProblem(409);

        app.MapGet(
            "/api/graph/edges/page",
            async (
                int[]? directoryIds, string? cursor, string? revision, int? pageSize, bool? includeTotal,
                IQueryHandler<GetGraphEdgesPageQuery, GraphEdgesPage> handler, CancellationToken ct) =>
                await handler.Handle(
                    new GetGraphEdgesPageQuery(new GraphPageRequest(
                        directoryIds ?? [],
                        pageSize ?? 2_000,
                        cursor,
                        revision,
                        includeTotal ?? false)),
                    ct))
            .WithName("GetGraphEdgesPage")
            .WithTags("Graph")
            .Produces<GraphEdgesPage>()
            .ProducesProblem(400)
            .ProducesProblem(409);

        app.MapGet(
            "/api/graph/nodes/{nodeId:int}/connections",
            async (
                int nodeId, string? cursor, string? revision, int? pageSize, bool? includeTotal,
                IQueryHandler<GetGraphNodeConnectionsQuery, GraphNodeConnectionsPage> handler, CancellationToken ct) =>
                await handler.Handle(
                    new GetGraphNodeConnectionsQuery(new GraphNodeConnectionsRequest(
                        nodeId,
                        pageSize ?? 100,
                        cursor,
                        revision,
                        includeTotal ?? false)),
                    ct))
            .WithName("GetGraphNodeConnections")
            .WithTags("Graph")
            .Produces<GraphNodeConnectionsPage>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);
    }
}
